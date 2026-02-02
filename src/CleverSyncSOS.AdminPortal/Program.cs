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
using Microsoft.Extensions.Options;

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
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ISchoolScopeService, SchoolScopeService>();
builder.Services.AddScoped<ISyncCoordinatorService, SyncCoordinatorService>();
builder.Services.AddScoped<IEventsCheckService, EventsCheckService>();

// Register Core sync service and dependencies (from CleverSyncSOS.Core)
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ILocalTimeService, CleverSyncSOS.Core.Services.LocalTimeService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ISyncScheduleService, CleverSyncSOS.Core.Services.SyncScheduleService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Services.ISyncLockService, CleverSyncSOS.Core.Services.SyncLockService>();
builder.Services.AddScoped<CleverSyncSOS.Core.Sync.Workshop.IWorkshopSyncService, CleverSyncSOS.Core.Sync.Workshop.WorkshopSyncService>();
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
        options.Cookie.SameSite = SameSiteMode.Strict;
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

        options.SaveTokens = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
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
    options.AddPolicy("bypass-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions?.BypassLogin.PermitLimit ?? 5,
                Window = rateLimitOptions?.BypassLogin.Window ?? TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Add Blazor services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

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

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Rate limiting middleware
app.UseRateLimiter();

// Add a POST endpoint to handle bypass login submissions from a full-page POST.
// This runs inside a normal HTTP request so SignInAsync can set the authentication cookie.
app.MapPost("/admin/bypass-login-submit", async (HttpContext httpContext, IBypassAuthenticationService bypassAuthService, IAuditLogService auditLogService) =>
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

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Super Admin"),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim("authentication_source", "Bypass")
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        await auditLogService.LogAuthenticationEventAsync(
            action: "BypassLogin",
            success: true,
            userIdentifier: "Super Admin",
            details: "Bypass login successful",
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

app.MapBlazorHub();

// Map SignalR hub for sync progress updates
app.MapHub<CleverSyncSOS.AdminPortal.Hubs.SyncProgressHub>("/hubs/syncProgress");

app.MapFallbackToPage("/_Host");

app.Run();
