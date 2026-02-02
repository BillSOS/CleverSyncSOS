namespace CleverSyncSOS.AdminPortal.Configuration;

public class AzureKeyVaultOptions
{
    public const string SectionName = "AzureKeyVault";

    public string VaultUri { get; set; } = string.Empty;
}
