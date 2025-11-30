using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CleverSyncSOS.AdminPortal.Configuration;
using CleverSyncSOS.AdminPortal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace CleverSyncSOS.AdminPortal.Authentication;

/// <summary>
/// Custom OAuth2 authentication handler for Clever
/// </summary>
public class CleverOAuthHandler : OAuthHandler<CleverOAuthOptions>
{
    private readonly ICleverRoleMappingService _roleMappingService;
    private readonly IAuditLogService _auditLogService;

    public CleverOAuthHandler(
        IOptionsMonitor<CleverOAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ICleverRoleMappingService roleMappingService,
        IAuditLogService auditLogService)
        : base(options, logger, encoder)
    {
        _roleMappingService = roleMappingService;
        _auditLogService = auditLogService;
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity,
        AuthenticationProperties properties,
        OAuthTokenResponse tokens)
    {
        // Get user info from Clever API
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to retrieve Clever user information: {response.StatusCode}");
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = payload.RootElement.GetProperty("data");

        // Extract user information
        var cleverUserId = data.GetProperty("id").GetString() ?? throw new Exception("Missing Clever user ID");
        var userType = data.GetProperty("type").GetString() ?? "staff";

        // Extract district and school information
        string? districtId = null;
        List<string>? schoolIds = null;

        if (data.TryGetProperty("district", out var districtElement))
        {
            districtId = districtElement.GetString();
        }

        if (data.TryGetProperty("schools", out var schoolsElement))
        {
            schoolIds = schoolsElement.EnumerateArray()
                .Select(s => s.GetString())
                .Where(s => s != null)
                .Cast<string>()
                .ToList();
        }

        // Add basic claims
        var userEmail = data.GetProperty("email").GetString() ?? cleverUserId;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, cleverUserId));
        identity.AddClaim(new Claim(ClaimTypes.Name, userEmail));

        // Map Clever user to role-based claims
        var roleClaims = await _roleMappingService.MapCleverUserToClaimsAsync(
            cleverUserId,
            userType,
            schoolIds,
            districtId);

        foreach (var claim in roleClaims)
        {
            identity.AddClaim(claim);
        }

        var principal = new ClaimsPrincipal(identity);
        var context = new OAuthCreatingTicketContext(principal, properties, Context, Scheme, Options, Backchannel, tokens, payload.RootElement);

        await Events.CreatingTicket(context);

        // Log successful Clever login
        var ipAddress = Context.Connection.RemoteIpAddress?.ToString();
        var userAgent = Context.Request.Headers["User-Agent"].ToString();
        var role = roleClaims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "Unknown";

        await _auditLogService.LogAuthenticationEventAsync(
            action: "CleverLogin",
            success: true,
            userIdentifier: userEmail,
            details: $"Clever OAuth login successful. Role: {role}",
            ipAddress: ipAddress,
            userAgent: userAgent);

        return new AuthenticationTicket(context.Principal!, context.Properties, Scheme.Name);
    }
}
