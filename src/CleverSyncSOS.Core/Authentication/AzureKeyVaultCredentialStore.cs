// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-002 Credential Storage, FR-011 Security
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using CleverSyncSOS.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleverSyncSOS.Core.Authentication;

/// <summary>
/// Azure Key Vault implementation of credential storage.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-002)
/// Requirement: Store credentials in Azure Key Vault, retrieve using managed identity.
/// </summary>
public class AzureKeyVaultCredentialStore : ICredentialStore
{
    private readonly SecretClient _secretClient;
    private readonly CleverAuthConfiguration _configuration;
    private readonly ILogger<AzureKeyVaultCredentialStore> _logger;

    /// <summary>
    /// Initializes a new instance of the AzureKeyVaultCredentialStore.
    /// Source: FR-002 - Retrieve secrets using Azure managed identity
    /// </summary>
    public AzureKeyVaultCredentialStore(
        IOptions<CleverAuthConfiguration> configuration,
        ILogger<AzureKeyVaultCredentialStore> logger)
    {
        _configuration = configuration.Value;
        _logger = logger;

        // FR-002: Retrieve secrets using Azure managed identity
        // FR-011: Enforce TLS 1.2+ (handled by Azure SDK)
        var keyVaultUri = new Uri(_configuration.KeyVaultUri);
        var credential = new DefaultAzureCredential();
        _secretClient = new SecretClient(keyVaultUri, credential);

        _logger.LogInformation("AzureKeyVaultCredentialStore initialized with Key Vault: {KeyVaultUri}",
            _configuration.KeyVaultUri);
    }

    /// <summary>
    /// Retrieves a global system-wide secret by functional name.
    /// Format: {functionalName}
    /// </summary>
    public async Task<string> GetGlobalSecretAsync(string functionalName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(functionalName))
                throw new ArgumentException("Functional name cannot be null or empty", nameof(functionalName));

            _logger.LogDebug("Retrieving global secret from Key Vault: {SecretName}", functionalName);

            var secret = await _secretClient.GetSecretAsync(
                functionalName,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Successfully retrieved global secret from Key Vault: {SecretName}", functionalName);

            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve global secret from Key Vault. Secret: {SecretName}", functionalName);
            throw;
        }
    }

    /// <summary>
    /// DEPRECATED: Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId) instead.
    /// </summary>
    [Obsolete("Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId) instead")]
    public async Task<string> GetClientIdAsync(CancellationToken cancellationToken = default)
    {
        return await GetGlobalSecretAsync(Configuration.KeyVaultSecretNaming.Global.ClientId, cancellationToken);
    }

    /// <summary>
    /// DEPRECATED: Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret) instead.
    /// </summary>
    [Obsolete("Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret) instead")]
    public async Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default)
    {
        return await GetGlobalSecretAsync(Configuration.KeyVaultSecretNaming.Global.ClientSecret, cancellationToken);
    }

    /// <summary>
    /// Retrieves a generic secret from Azure Key Vault by secret name.
    /// Source: FR-019 - Connection Management (Stage 2)
    /// </summary>
    public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving secret from Key Vault: {SecretName}", secretName);

            var secret = await _secretClient.GetSecretAsync(
                secretName,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Successfully retrieved secret from Key Vault: {SecretName}", secretName);

            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret from Key Vault. Secret: {SecretName}", secretName);
            throw;
        }
    }

    /// <summary>
    /// Retrieves a secret scoped to a specific district.
    /// Format: {keyVaultDistrictPrefix}--{functionalName}
    /// </summary>
    public async Task<string> GetDistrictSecretAsync(string keyVaultDistrictPrefix, string functionalName, CancellationToken cancellationToken = default)
    {
        var secretName = Configuration.KeyVaultSecretNaming.BuildDistrictSecretName(keyVaultDistrictPrefix, functionalName);
        return await GetSecretAsync(secretName, cancellationToken);
    }

    /// <summary>
    /// Retrieves a secret scoped to a specific school.
    /// Format: {keyVaultSchoolPrefix}--{functionalName}
    /// </summary>
    public async Task<string> GetSchoolSecretAsync(string keyVaultSchoolPrefix, string functionalName, CancellationToken cancellationToken = default)
    {
        var secretName = Configuration.KeyVaultSecretNaming.BuildSchoolSecretName(keyVaultSchoolPrefix, functionalName);
        return await GetSecretAsync(secretName, cancellationToken);
    }
}
