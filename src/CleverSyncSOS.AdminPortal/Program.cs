using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using CleverSyncSOS.AdminPortal.Authentication;
using CleverSyncSOS.AdminPortal.Configuration;
using CleverSyncSOS.AdminPortal.Services;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database; // <-- Make sure this matches the actual namespace of SchoolDatabaseConnectionFactory
using CleverSyncSOS.Core.Database.SchoolDb; // <-- Add this line if SchoolDatabaseConnectionFactory is in this namespace
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
// Add an alias for the configuration AuthenticationOptions to avoid ambiguity
using ConfigAuthenticationOptions = CleverSyncSOS.AdminPortal.Configuration.AuthenticationOptions;

// Added usings to reference types provided by the Core project
using CleverSyncSOS.Core.Configuration;
using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.Services;
using Microsoft.Extensions.Options;
using FluentValidation;
using CleverSyncSOS.AdminPortal.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Configure options
builder.Services.Configure<CleverOAuthOptions>(
    builder.Configuration.GetSection(CleverOAuthOptions.SectionName));
builder.Services.Configure<ConfigAuthenticationOptions>(
    builder.Configuration.GetSection(ConfigAuthenticationOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(
    builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.Configure<AzureKeyVaultOptions>(
    builder.Configuration.GetSection(AzureKeyVaultOptions.SectionName));
builder.Services.Configure<SessionSecurityOptions>(
    builder.Configuration.GetSection(SessionSecurityOptions.SectionName));

// Configure HSTS (HTTP Strict Transport Security) for Clever IL Security compliance
// Requirement: MaxAge >= 1 year, IncludeSubDomains, Preload
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365); // 1 year minimum per IL Security
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// Configure Azure Key Vault
var keyVaultOptions = builder.Configuration
    .GetSection(AzureKeyVaultOptions.SectionName)
    .Get<AzureKeyVaultOptions>();
//start
var vaultUri = builder.Configuration["AzureKeyVault:VaultUri"];
var rawConnectionString = builder.Configuration.GetConnectionString("SessionDb");

// Validation (Optional but recommended)
if (string.IsNullOrEmpty(vaultUri)) throw new InvalidOperationException("VaultUri is missing in appsettings.");
if (string.IsNullOrEmpty(rawConnectionString)) throw new InvalidOperationException("SessionDb connection string is missing.");

// =============================================================================
// 2. Fetch Password from Azure Key Vault
// =============================================================================
// DefaultAzureCredential looks for credentials in this order:
// Env Vars -> Workload Identity -> Managed Identity -> VS Code -> Visual Studio -> CLI
var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri(vaultUri), credential);

// register for DI so BypassAuthenticationService can receive it
builder.Services.AddSingleton(client);

// Retrieve SessionDb password using new naming convention: {FunctionalName}
KeyVaultSecret secret = await client.GetSecretAsync(
    CleverSyncSOS.Core.Configuration.KeyVaultSecretNaming.Global.SessionDbPassword);
string dbPassword = secret.Value;

// Retrieve Clever API credentials from Key Vault
KeyVaultSecret clientIdSecret = await client.GetSecretAsync(
    CleverSyncSOS.Core.Configuration.KeyVaultSecretNaming.Global.ClientId);
string cleverClientId = clientIdSecret.Value;

KeyVaultSecret clientSecretSecret = await client.GetSecretAsync(
    CleverSyncSOS.Core.Configuration.KeyVaultSecretNaming.Global.ClientSecret);
string cleverClientSecret = clientSecretSecret.Value;

// =============================================================================
// 3. Construct Final Connection String
// =============================================================================
// Use SqlConnectionStringBuilder to safely inject the password.
// It handles the removal of {your_password} and insertion of the real value automatically.
var connectionBuilder = new SqlConnectionStringBuilder(rawConnectionString);
connectionBuilder.Password = dbPassword;

string finalConnectionString = connectionBuilder.ToString();
//stop

// Quick runtime validation: attempt to open a SQL connection (fail-fast so you see exact error).
// This surfaces login/permission/login-failure problems during startup instead of later during a request.
if (string.IsNullOrWhiteSpace(finalConnectionString))
{
    Console.Error.WriteLine("SessionDb connection string is empty.");
    throw new InvalidOperationException("SessionDb connection string is not configured.");
}

try
{
    await using var testConn = new SqlConnection(finalConnectionString);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await testConn.OpenAsync(cts.Token);
    await testConn.CloseAsync();
    Console.WriteLine("Successfully validated SessionDb connection string.");
}
catch (SqlException ex)
{
    // SqlException includes error numbers (e.g. 18456 for login failed). Fail fast so root cause is visible.
    Console.Error.WriteLine($"Failed to open SQL connection. SqlException {ex.Number}: {ex.Message}");
    throw;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Timed out while attempting to open SQL connection (5s).");
    throw;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error while validating SQL connection: {ex.Message}");
    throw;
}

// Register DbContextFactory for components that need to create DbContext instances
// outside of the normal request scope (e.g., timer callbacks in Blazor components)
// Using AddPooledDbContextFactory which provides both DbContextFactory and AddDbContext-like scoped behavior
builder.Services.AddPooledDbContextFactory<SessionDbContext>(options =>
    options.UseSqlServer(finalConnectionString));

// Register a scoped DbContext that uses the factory (for backwards compatibility with existing code)
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<SessionDbContext>>().CreateDbContext());

// Register application services
builder.Services.AddSingleton<ActiveSyncTracker>(); // Singleton to track active syncs across all scopes
builder.Services.AddScoped<ICleverRoleMappingService, CleverRoleMappingService>();
builder.Services.AddScoped<IBypassAuthenticationService, BypassAuthenticationService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.IAuditLogService, CleverSyncSOS.Core.Services.AuditLogService>();
builder.Services.AddScoped<ISchoolScopeService, SchoolScopeService>();
builder.Services.AddScoped<ISyncCoordinatorService, SyncCoordinatorService>();
builder.Services.AddScoped<IEventsCheckService, EventsCheckService>();
builder.Services.AddScoped<IKeyVaultManagementService, KeyVaultManagementService>();
builder.Services.AddScoped<IDiagnosticsService, DiagnosticsService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ITermManagementService, TermManagementService>();

// Register Session Security services (Shared Device Mode)
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<SessionSecurityOptions>>().Value;
    return new SessionSecurityConfiguration
    {
        DefaultIdleTimeoutMinutes = options.DefaultIdleTimeoutMinutes,
        DefaultAbsoluteTimeoutMinutes = options.DefaultAbsoluteTimeoutMinutes,
        SharedDeviceIdleTimeoutMinutes = options.SharedDeviceIdleTimeoutMinutes,
        SharedDeviceAbsoluteTimeoutMinutes = options.SharedDeviceAbsoluteTimeoutMinutes,
        SessionWarningMinutes = options.SessionWarningMinutes,
        DefaultMaxConcurrentSessions = options.DefaultMaxConcurrentSessions,
        SharedDeviceMaxConcurrentSessions = options.SharedDeviceMaxConcurrentSessions,
        EnableDeviceFingerprinting = options.EnableDeviceFingerprinting,
        LogFingerprintMismatches = options.LogFingerprintMismatches
    };
});
builder.Services.AddScoped<ISessionSecurityService, SessionSecurityService>();
builder.Services.AddScoped<ISessionSettingsService, SessionSettingsService>();

// Add IMemoryCache for session settings caching
builder.Services.AddMemoryCache();

// Register Core sync service and dependencies (from CleverSyncSOS.Core)
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ILocalTimeService, CleverSyncSOS.Core.Services.LocalTimeService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ISyncScheduleService, CleverSyncSOS.Core.Services.SyncScheduleService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ISyncLockService, CleverSyncSOS.Core.Services.SyncLockService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Workshop.IWorkshopSyncService, CleverSyncSOS.Core.Sync.Workshop.WorkshopSyncService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ISessionCleanupService, CleverSyncSOS.Core.Services.SessionCleanupService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ILogCleanupService, CleverSyncSOS.Core.Services.LogCleanupService>();
builder.Services.AddSingleton<CleverSyncSOS.Core.Sync.ISyncValidationService, CleverSyncSOS.Core.Sync.SyncValidationService>();

// Register entity sync handlers (required by SyncService)
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Handlers.StudentSyncHandler>();
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Handlers.TeacherSyncHandler>();
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Handlers.SectionSyncHandler>();
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Handlers.TermSyncHandler>();
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Handlers.CleverEventProcessor>();

builder.Services.AddScoped<CleverSyncSOS.Core.Sync.ISyncService, CleverSyncSOS.Core.Sync.SyncService>();

// Register the school DB factory (concrete type used directly by SyncService)
builder.Services.AddScoped<SchoolDatabaseConnectionFactory>();

// Register the credential store implementation (replace with your concrete class name)
builder.Services.AddScoped<ICredentialStore, KeyVaultCredentialStore>();

// Configure Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        var authOptions = builder.Configuration
            .GetSection(ConfigAuthenticationOptions.SectionName)
            .Get<ConfigAuthenticationOptions>();

        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(authOptions?.SessionTimeoutMinutes ?? 30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        // Use Lax instead of Strict - Strict blocks cookies on cross-site navigations
        // which breaks OAuth redirects from Clever back to our site
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddOAuth<CleverOAuthOptions, CleverOAuthHandler>("Clever", options =>
    {
        var cleverConfig = builder.Configuration.GetSection(CleverOAuthOptions.SectionName);
        // Use credentials fetched from Key Vault instead of configuration
        options.ClientId = cleverClientId;
        options.ClientSecret = cleverClientSecret;

        // Add scopes
        var scopeStr = cleverConfig["Scope"] ?? "read:user_id read:sis";
        foreach (var scope in scopeStr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }

        // Don't persist Clever access token - it's only used during initial auth
        // to fetch user info, then discarded. Reduces cookie size and attack surface.
        options.SaveTokens = false;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Configure correlation cookie for OAuth state - must be SameSite=None for cross-site redirects
        // When Clever redirects back to our callback, the browser needs to send this cookie
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

        // Add event handlers for OAuth security
        options.Events = new OAuthEvents
        {
            OnRedirectToAuthorizationEndpoint = context =>
            {
                // Check if district_id was passed in AuthenticationProperties
                if (context.Properties.Items.TryGetValue("district_id", out var districtId)
                    && !string.IsNullOrEmpty(districtId))
                {
                    // Append district_id to the redirect URL
                    context.RedirectUri = context.RedirectUri + "&district_id=" + Uri.EscapeDataString(districtId);
                }

                // Check if prompt=login was requested (for re-authentication)
                // This forces Clever to show the login screen even if user has existing Clever session
                if (context.Properties.Items.TryGetValue("prompt", out var prompt)
                    && !string.IsNullOrEmpty(prompt))
                {
                    context.RedirectUri = context.RedirectUri + "&prompt=" + Uri.EscapeDataString(prompt);
                }

                // Store timestamp in properties to detect stale OAuth flows
                context.Properties.Items["oauth_initiated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },

            OnCreatingTicket = context =>
            {
                // Validate OAuth flow timing (protection against bookmarked callback URLs)
                if (context.Properties.Items.TryGetValue("oauth_initiated", out var initiatedStr) &&
                    long.TryParse(initiatedStr, out var initiated))
                {
                    var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - initiated;
                    // OAuth flow should complete within 10 minutes
                    if (elapsed > 600)
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(
                            "OAuth flow took {Seconds} seconds (>10 min) - possible callback URL reuse",
                            elapsed);
                    }
                }
                return Task.CompletedTask;
            },

            OnRemoteFailure = context =>
            {
                // Log OAuth failures for security monitoring
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                var auditService = context.HttpContext.RequestServices.GetRequiredService<IAuditLogService>();

                logger.LogWarning(
                    "OAuth authentication failed: {Error}. Failure: {Failure}",
                    context.Failure?.Message ?? "Unknown",
                    context.Failure?.InnerException?.Message ?? "None");

                // Log to audit trail asynchronously
                _ = auditService.LogAuthenticationEventAsync(
                    action: "OAuthFailure",
                    success: false,
                    userIdentifier: "Unknown",
                    details: $"OAuth failure: {context.Failure?.Message}",
                    ipAddress: context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    userAgent: context.HttpContext.Request.Headers["User-Agent"].ToString());

                // Redirect to login with error
                context.Response.Redirect("/login?error=oauth_failed");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });

// Configure Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SchoolAdmin", policy =>
        policy.RequireRole("SchoolAdmin", "DistrictAdmin", "SuperAdmin"));
    options.AddPolicy("DistrictAdmin", policy =>
        policy.RequireRole("DistrictAdmin", "SuperAdmin"));
    options.AddPolicy("SuperAdmin", policy =>
        policy.RequireRole("SuperAdmin"));
});

// Configure Rate Limiting
var rateLimitOptions = builder.Configuration
    .GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>();

builder.Services.AddRateLimiter(options =>
{
    // Use sliding window limiter for bypass-login (harder to game timing than fixed window)
    // Default: 3 attempts per 15 minutes per IP address
    options.AddPolicy("bypass-login", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions?.BypassLogin.PermitLimit ?? 3,
                Window = rateLimitOptions?.BypassLogin.Window ?? TimeSpan.FromMinutes(15),
                SegmentsPerWindow = rateLimitOptions?.BypassLogin.SegmentsPerWindow ?? 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Global rejection handler - returns 429 with Retry-After header
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
        var path = context.HttpContext.Request.Path.Value;
        var userAgent = context.HttpContext.Request.Headers.UserAgent.ToString();

        // Diagnostic logging for troubleshooting
        var logger = context.HttpContext.RequestServices.GetService<ILogger<Program>>();
        logger?.LogWarning(
            "Rate limit exceeded for IP {IpAddress} on {Path}",
            ip, path);

        // Persistent audit logging for security monitoring
        var auditLogService = context.HttpContext.RequestServices.GetService<IAuditLogService>();
        if (auditLogService != null)
        {
            await auditLogService.LogEventAsync(
                action: "RateLimitExceeded",
                success: false,
                userId: null,
                userIdentifier: ip ?? "unknown",
                resource: path,
                details: $"Path={path}; IP={ip ?? "unknown"}; UserAgent={userAgent}",
                ipAddress: ip,
                userAgent: userAgent);
        }

        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again later.", cancellationToken);
    };
});

// Add Blazor services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add Anti-forgery services for CSRF protection (FR-016 T136)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Register FluentValidation validators (FR-016 T138)
builder.Services.AddValidatorsFromAssemblyContaining<CleverSyncSOS.AdminPortal.Validators.SyncScheduleValidator>();

// Add SignalR for real-time sync progress updates
builder.Services.AddSignalR();

// Add HttpContextAccessor for accessing user claims
builder.Services.AddHttpContextAccessor();

// Bind Clever API options from configuration (ensure appsettings has "CleverApi" section)
builder.Services.Configure<CleverApiConfiguration>(
    builder.Configuration.GetSection("CleverApi"));

// Bind Clever Auth options from configuration
builder.Services.Configure<CleverAuthConfiguration>(
    builder.Configuration.GetSection("CleverAuth"));

// Register authentication service used by CleverApiClient
builder.Services.AddScoped<ICleverAuthenticationService, CleverAuthenticationService>();

// Register CleverApiClient as a typed HttpClient (IHttpClientFactory)
builder.Services.AddHttpClient<ICleverApiClient, CleverApiClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IOptions<CleverApiConfiguration>>().Value;
    client.BaseAddress = new Uri(cfg.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds);
});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<CleverSyncSOS.Core.Health.CleverAuthenticationHealthCheck>("CleverAuthentication")
    .AddCheck<CleverSyncSOS.Core.Health.CleverEventsHealthCheck>("CleverEventsApi");

var app = builder.Build();

// Configure forwarded headers for Azure App Service reverse proxy
// This ensures the app sees HTTPS requests correctly when behind Azure's load balancer
// SECURITY: ForwardLimit=1 ensures we only trust the immediate proxy (Azure's load balancer).
// Azure App Service always sets X-Forwarded-For, and we trust only the first hop.
// Without this, attackers could spoof X-Forwarded-For headers to bypass IP-based rate limiting
// and falsify audit logs. See: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // Only trust one proxy hop (Azure's front-end load balancer)
    ForwardLimit = 1,
    // In Azure App Service, the platform handles the proxy trust chain.
    // Clear the default known networks/proxies and rely on ForwardLimit=1
    // to only process the rightmost (most recent) X-Forwarded-For value.
};
// Clear defaults to ensure we only trust what Azure provides
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Security Headers Middleware (FR-016 T137)
app.Use(async (context, next) =>
{
    // Prevent clickjacking - page cannot be embedded in iframes
    context.Response.Headers["X-Frame-Options"] = "DENY";

    // Prevent MIME type sniffing
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";

    // Enable XSS filter in browsers
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

    // Referrer policy - only send origin on cross-origin requests
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // Permissions policy - disable sensitive features
    context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    // Content Security Policy - restrict resource loading
    // Note: 'unsafe-inline' and 'unsafe-eval' needed for Blazor Server
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net; " +
        "font-src 'self' https://cdnjs.cloudflare.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self' wss: ws:; " +
        "frame-ancestors 'none';";

    await next();
});

app.UseStaticFiles();

// No-cache headers for dynamic/authenticated pages (prevents back button showing cached content)
app.UseNoCacheHeaders();

app.UseRouting();

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Session validation middleware (validates server-side sessions)
app.UseSessionValidation();

// Bookmark protection middleware (detects direct URL access)
app.UseBookmarkProtection();

// Rate limiting middleware
app.UseRateLimiter();

// Add a POST endpoint to handle bypass login submissions from a full-page POST.
// This runs inside a normal HTTP request so SignInAsync can set the authentication cookie.
app.MapPost("/admin/bypass-login-submit", async (
    HttpContext httpContext,
    IBypassAuthenticationService bypassAuthService,
    IAuditLogService auditLogService,
    IUserSessionService sessionService,
    ISessionSecurityService securityService,
    IOptions<SessionSecurityOptions> sessionOptions) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var password = form["password"].ToString();

    var ip = httpContext.Connection.RemoteIpAddress?.ToString();
    var ua = httpContext.Request.Headers["User-Agent"].ToString();

    try
    {
        if (!await bypassAuthService.ValidatePasswordAsync(password))
        {
            await auditLogService.LogAuthenticationEventAsync(
                action: "BypassLoginFailed",
                success: false,
                userIdentifier: "Super Admin",
                details: "Invalid password provided",
                ipAddress: ip,
                userAgent: ua);

            return Results.Redirect("/admin/bypass-login");
        }

        // Create server-side session for Super Admin (uses normal mode, not shared device)
        var config = sessionOptions.Value;
        // Get effective limit for SuperAdmin role (userId=0 has no override, uses role default)
        var effectiveLimit = await securityService.GetEffectiveSessionLimitAsync(0, "SuperAdmin", null);
        // SuperAdmin bypass login: use system default for invalidation policy (schoolId=null, not shared device)
        var invalidateAllSessions = await securityService.ShouldInvalidateAllSessionsOnLoginAsync(null, false);
        var sessionResult = await sessionService.CreateSessionAsync(
            userId: 0, // Super Admin has no database user ID
            ipAddress: ip,
            userAgent: ua,
            deviceFingerprint: null,
            isSharedDeviceMode: false,
            idleTimeout: config.GetIdleTimeout(false),
            absoluteTimeout: config.GetAbsoluteTimeout(false),
            maxConcurrentSessions: effectiveLimit.MaxSessions,
            invalidateAllSessions: invalidateAllSessions);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Super Admin"),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim("authentication_source", "Bypass"),
            new Claim("session_token", sessionResult.SessionToken.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        // Set cookie expiration to match server-side session expiration
        var authProperties = new AuthenticationProperties
        {
            ExpiresUtc = sessionResult.ExpiresAt,
            IsPersistent = true
        };

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        await auditLogService.LogAuthenticationEventAsync(
            action: "BypassLogin",
            success: true,
            userIdentifier: "Super Admin",
            details: $"Bypass login successful. SessionToken={sessionResult.SessionToken:N}, MaxSessions={effectiveLimit.MaxSessions} (source: {effectiveLimit.Source}), RevokedSessions={sessionResult.RevokedSessionCount}, PolicyInvalidation={sessionResult.WasPolicyInvalidation}",
            ipAddress: ip,
            userAgent: ua);

        return Results.Redirect("/");
    }
    catch (Exception ex)
    {
        await auditLogService.LogAuthenticationEventAsync(
            action: "BypassLoginError",
            success: false,
            userIdentifier: "Super Admin",
            details: $"Error: {ex.Message}",
            ipAddress: ip,
            userAgent: ua);

        // On error, redirect back to the bypass login page
        return Results.Redirect("/admin/bypass-login");
    }
}).RequireRateLimiting("bypass-login");

// Session status API endpoint
app.MapGet("/api/session/status", async (
    HttpContext httpContext,
    IUserSessionService sessionService) =>
{
    var sessionTokenClaim = httpContext.User.FindFirst("session_token");
    if (sessionTokenClaim == null || !Guid.TryParse(sessionTokenClaim.Value, out var sessionToken))
    {
        return Results.Ok(new { isValid = false, reason = "No session token" });
    }

    var result = await sessionService.ValidateSessionAsync(sessionToken, updateActivity: false);
    return Results.Ok(new
    {
        isValid = result.IsValid,
        isExpiringSoon = result.IsExpiringSoon,
        expiresAt = result.ExpiresAt,
        isSharedDeviceMode = result.IsSharedDeviceMode,
        secondsRemaining = result.ExpiresAt.HasValue
            ? (int)Math.Max(0, (result.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds)
            : 0
    });
}).RequireAuthorization();

// Session extension API endpoint
// In shared device mode, this endpoint rejects extension requests - users must re-authenticate
app.MapPost("/api/session/extend", async (
    HttpContext httpContext,
    IUserSessionService sessionService,
    ISessionSecurityService securityService) =>
{
    var sessionTokenClaim = httpContext.User.FindFirst("session_token");
    if (sessionTokenClaim == null || !Guid.TryParse(sessionTokenClaim.Value, out var sessionToken))
    {
        return Results.BadRequest(new { error = "No session token" });
    }

    // Check if this is a shared device session - they cannot extend via this endpoint
    var sessionInfo = await sessionService.GetSessionAsync(sessionToken);
    if (sessionInfo?.IsSharedDeviceMode == true)
    {
        // Determine auth source to provide appropriate re-auth method
        var authSourceClaim = httpContext.User.FindFirst("authentication_source");
        var isCleverSso = authSourceClaim?.Value == "Clever";

        if (isCleverSso)
        {
            // Clever SSO users must re-authenticate through OAuth with prompt=login
            return Results.BadRequest(new
            {
                error = "Shared device sessions require re-authentication to extend",
                requiresReauth = true,
                reauthUrl = "/auth/clever-reauth"
            });
        }
        else
        {
            // Bypass users can use password revalidation
            return Results.BadRequest(new
            {
                error = "Shared device sessions require re-authentication to extend",
                requiresReauth = true,
                reauthUrl = "/api/session/revalidate"
            });
        }
    }

    var schoolIdClaim = httpContext.User.FindFirst("school_id");
    int? schoolId = schoolIdClaim != null && int.TryParse(schoolIdClaim.Value, out var sid) ? sid : null;

    var settings = await securityService.GetTimeoutSettingsAsync(schoolId);
    var newExpiry = await sessionService.ExtendSessionAsync(sessionToken, settings.IdleTimeout);

    if (newExpiry == null)
    {
        return Results.BadRequest(new { error = "Session not found or expired" });
    }

    return Results.Ok(new
    {
        success = true,
        newExpiresAt = newExpiry,
        secondsRemaining = (int)(newExpiry.Value - DateTime.UtcNow).TotalSeconds
    });
}).RequireAuthorization();

// Auth source API endpoint - returns how the user authenticated
app.MapGet("/api/session/auth-source", (HttpContext httpContext) =>
{
    var authSourceClaim = httpContext.User.FindFirst("authentication_source");
    return Results.Ok(new { authSource = authSourceClaim?.Value });
}).RequireAuthorization();

// Session revalidation endpoint for shared device mode re-authentication
app.MapPost("/api/session/revalidate", async (
    HttpContext httpContext,
    IUserSessionService sessionService,
    ISessionSecurityService securityService,
    IBypassAuthenticationService bypassAuthService,
    IAuditLogService auditLogService,
    RevalidateRequest request) =>
{
    var sessionTokenClaim = httpContext.User.FindFirst("session_token");
    var authSourceClaim = httpContext.User.FindFirst("authentication_source");
    var ip = httpContext.Connection.RemoteIpAddress?.ToString();

    if (sessionTokenClaim == null || !Guid.TryParse(sessionTokenClaim.Value, out var sessionToken))
    {
        return Results.BadRequest(new { error = "No session token" });
    }

    // Only bypass users can revalidate with password
    if (authSourceClaim?.Value != "Bypass")
    {
        return Results.BadRequest(new { error = "Password revalidation only available for bypass login users" });
    }

    // Validate the password
    if (string.IsNullOrEmpty(request.Password) || !await bypassAuthService.ValidatePasswordAsync(request.Password))
    {
        await auditLogService.LogAuthenticationEventAsync(
            action: "SessionRevalidationFailed",
            success: false,
            userIdentifier: httpContext.User.Identity?.Name,
            details: "Invalid password during session revalidation",
            ipAddress: ip);

        return Results.BadRequest(new { error = "Invalid password" });
    }

    // Password valid - extend the session
    var schoolIdClaim = httpContext.User.FindFirst("school_id");
    int? schoolId = schoolIdClaim != null && int.TryParse(schoolIdClaim.Value, out var sid) ? sid : null;

    var settings = await securityService.GetTimeoutSettingsAsync(schoolId);
    var newExpiry = await sessionService.ExtendSessionAsync(sessionToken, settings.IdleTimeout);

    if (newExpiry == null)
    {
        return Results.BadRequest(new { error = "Session not found or expired" });
    }

    await auditLogService.LogAuthenticationEventAsync(
        action: "SessionRevalidated",
        success: true,
        userIdentifier: httpContext.User.Identity?.Name,
        details: $"Session extended after password revalidation. SessionToken={sessionToken:N}",
        ipAddress: ip);

    return Results.Ok(new
    {
        success = true,
        newExpiresAt = newExpiry,
        secondsRemaining = (int)(newExpiry.Value - DateTime.UtcNow).TotalSeconds
    });
}).RequireAuthorization();

// Logout endpoint that revokes session
app.MapPost("/api/session/logout", async (
    HttpContext httpContext,
    IUserSessionService sessionService,
    IAuditLogService auditLogService) =>
{
    var sessionTokenClaim = httpContext.User.FindFirst("session_token");
    var ip = httpContext.Connection.RemoteIpAddress?.ToString();

    if (sessionTokenClaim != null && Guid.TryParse(sessionTokenClaim.Value, out var sessionToken))
    {
        await sessionService.RevokeSessionAsync(sessionToken, "Logout");

        await auditLogService.LogAuthenticationEventAsync(
            action: "Logout",
            success: true,
            userIdentifier: httpContext.User.Identity?.Name,
            details: $"Session revoked. SessionToken={sessionToken:N}",
            ipAddress: ip);
    }
    else
    {
        // Log edge case where session token is missing
        await auditLogService.LogAuthenticationEventAsync(
            action: "LogoutNoSession",
            success: true,
            userIdentifier: httpContext.User.Identity?.Name ?? "Unknown",
            details: "Logout called without valid session token claim",
            ipAddress: ip);
    }

    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    // Clear auxiliary cookies (consistent with GET /logout)
    httpContext.Response.Cookies.Delete("nav_ctx", new CookieOptions
    {
        Path = "/",
        Secure = true,
        SameSite = SameSiteMode.Strict
    });

    httpContext.Response.Cookies.Delete(".AspNetCore.Correlation.Clever", new CookieOptions
    {
        Path = "/",
        Secure = true,
        SameSite = SameSiteMode.None
    });

    return Results.Ok(new { success = true, redirectUrl = "/login" });
}).RequireAuthorization();

// GET logout for simple redirects
app.MapGet("/logout", async (
    HttpContext httpContext,
    IUserSessionService sessionService,
    IAuditLogService auditLogService) =>
{
    var sessionTokenClaim = httpContext.User.FindFirst("session_token");
    var ip = httpContext.Connection.RemoteIpAddress?.ToString();

    if (sessionTokenClaim != null && Guid.TryParse(sessionTokenClaim.Value, out var sessionToken))
    {
        await sessionService.RevokeSessionAsync(sessionToken, "Logout");

        await auditLogService.LogAuthenticationEventAsync(
            action: "Logout",
            success: true,
            userIdentifier: httpContext.User.Identity?.Name,
            details: $"Session revoked. SessionToken={sessionToken:N}",
            ipAddress: ip);
    }
    else
    {
        // Log edge case where session token is missing
        await auditLogService.LogAuthenticationEventAsync(
            action: "LogoutNoSession",
            success: true,
            userIdentifier: httpContext.User.Identity?.Name ?? "Unknown",
            details: "Logout called without valid session token claim",
            ipAddress: ip);
    }

    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    // Clear server-side cookies that could persist session state
    // Navigation context cookie (from BookmarkProtectionMiddleware)
    httpContext.Response.Cookies.Delete("nav_ctx", new CookieOptions
    {
        Path = "/",
        Secure = true,
        SameSite = SameSiteMode.Strict
    });

    // Clear OAuth correlation cookie if present
    httpContext.Response.Cookies.Delete(".AspNetCore.Correlation.Clever", new CookieOptions
    {
        Path = "/",
        Secure = true,
        SameSite = SameSiteMode.None
    });

    // Prevent caching of logout redirect
    httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    httpContext.Response.Headers.Pragma = "no-cache";
    httpContext.Response.Headers.Expires = "0";

    return Results.Redirect("/login");
});

app.MapBlazorHub();

// Map SignalR hub for sync progress updates
app.MapHub<CleverSyncSOS.AdminPortal.Hubs.SyncProgressHub>("/hubs/syncProgress");

app.MapFallbackToPage("/_Host");

app.Run();

// Request models for minimal API endpoints
public record RevalidateRequest(string Password);
