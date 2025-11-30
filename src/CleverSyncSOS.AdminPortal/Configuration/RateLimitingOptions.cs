namespace CleverSyncSOS.AdminPortal.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public BypassLoginRateLimitOptions BypassLogin { get; set; } = new();
}

public class BypassLoginRateLimitOptions
{
    public int PermitLimit { get; set; } = 5;
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);
}
