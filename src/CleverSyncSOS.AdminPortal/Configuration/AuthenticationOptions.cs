namespace CleverSyncSOS.AdminPortal.Configuration;

public class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public bool BypassLoginEnabled { get; set; }
    public string SuperAdminPasswordSecretName { get; set; } = string.Empty;
    public string BypassLoginUrl { get; set; } = string.Empty;
    public int SessionTimeoutMinutes { get; set; } = 30;
}
