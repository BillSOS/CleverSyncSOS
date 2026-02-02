using Azure.Security.KeyVault.Secrets;
using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Configuration;

namespace CleverSyncSOS.Core.Authentication;

/// <summary>
/// Implementation of ICredentialStore using Azure Key Vault.
/// Uses standardized naming convention v2.0:
/// - Global: {FunctionalName}
/// - District: {KeyVaultDistrictPrefix}--{FunctionalName}
/// - School: {KeyVaultSchoolPrefix}--{FunctionalName}
/// </summary>
public class KeyVaultCredentialStore : ICredentialStore
{
    private readonly SecretClient _client;

    public KeyVaultCredentialStore(SecretClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Retrieves a global secret by functional name.
    /// Format: {functionalName}
    /// </summary>
    public async Task<string> GetGlobalSecretAsync(string functionalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(functionalName))
            throw new ArgumentException("Functional name cannot be null or empty", nameof(functionalName));

        var secret = await _client.GetSecretAsync(functionalName, cancellationToken: cancellationToken);
        return secret.Value.Value ?? string.Empty;
    }

    /// <summary>
    /// Retrieves a district-scoped secret.
    /// Format: {keyVaultDistrictPrefix}--{functionalName}
    /// </summary>
    public async Task<string> GetDistrictSecretAsync(string keyVaultDistrictPrefix, string functionalName, CancellationToken cancellationToken = default)
    {
        var secretName = KeyVaultSecretNaming.BuildDistrictSecretName(keyVaultDistrictPrefix, functionalName);
        var secret = await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return secret.Value.Value ?? string.Empty;
    }

    /// <summary>
    /// Retrieves a school-scoped secret.
    /// Format: {keyVaultSchoolPrefix}--{functionalName}
    /// </summary>
    public async Task<string> GetSchoolSecretAsync(string keyVaultSchoolPrefix, string functionalName, CancellationToken cancellationToken = default)
    {
        var secretName = KeyVaultSecretNaming.BuildSchoolSecretName(keyVaultSchoolPrefix, functionalName);
        var secret = await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return secret.Value.Value ?? string.Empty;
    }

    /// <summary>
    /// Retrieves a secret by exact name (for non-standard secrets).
    /// </summary>
    public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("Secret name cannot be null or empty", nameof(secretName));

        var secret = await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return secret.Value.Value ?? string.Empty;
    }

    // ========================================
    // DEPRECATED METHODS - For backward compatibility
    // ========================================

    /// <summary>
    /// DEPRECATED: Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId) instead.
    /// </summary>
    [Obsolete("Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId) instead")]
    public async Task<string> GetClientIdAsync(CancellationToken cancellationToken = default)
    {
        return await GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId, cancellationToken);
    }

    /// <summary>
    /// DEPRECATED: Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret) instead.
    /// </summary>
    [Obsolete("Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret) instead")]
    public async Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default)
    {
        return await GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret, cancellationToken);
    }
}