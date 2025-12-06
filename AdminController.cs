[Route("admin")]
public class AdminController : Controller
{
    private readonly IBypassAuthenticationService _bypassAuthService;
    private readonly IAuditLogService _auditLogService;

    public AdminController(IBypassAuthenticationService bypassAuthService, IAuditLogService auditLogService)
    {
        _bypassAuthService = bypassAuthService;
        _auditLogService = auditLogService;
    }

    [HttpPost("bypass-login-submit")]
    [ValidateAntiForgeryToken] // recommended
    public async Task<IActionResult> BypassLoginSubmit([FromForm] string password)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers["User-Agent"].ToString();

        if (!await _bypassAuthService.ValidatePasswordAsync(password))
        {
            await _auditLogService.LogAuthenticationEventAsync("BypassLoginFailed", false, "Super Admin", details: "Invalid password", ipAddress: ip, userAgent: ua);
            // Return a view or redirect back with error - for full page flow you can redirect to the page and show error
            return Redirect("/admin/bypass-login");
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Super Admin"),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim("authentication_source", "Bypass")
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        await _auditLogService.LogAuthenticationEventAsync("BypassLogin", true, "Super Admin", details: "Bypass login successful", ipAddress: ip, userAgent: ua);

        return Redirect("/");
    }
}