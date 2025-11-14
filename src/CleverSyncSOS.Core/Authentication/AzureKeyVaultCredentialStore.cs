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
    /// Retrieves the Clever API Client ID from Azure Key Vault.
    /// Source: FR-002 - Store Client ID in Azure Key Vault
    /// </summary>
    public async Task<string> GetClientIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // FR-010: Structured logging with contextual information
            _logger.LogDebug("Retrieving Client ID from Key Vault secret: {SecretName}",
                _configuration.ClientIdSecretName);

            var secret = await _secretClient.GetSecretAsync(
                _configuration.ClientIdSecretName,
                cancellationToken: cancellationToken);

            // FR-011: Audit Key Vault access (logged by Azure SDK telemetry)
            _logger.LogDebug("Successfully retrieved Client ID from Key Vault");

            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            // FR-010: Log errors without exposing sensitive data
            _logger.LogError(ex, "Failed to retrieve Client ID from Key Vault. Secret: {SecretName}",
                _configuration.ClientIdSecretName);
            throw;
        }
    }

    /// <summary>
    /// Retrieves the Clever API Client Secret from Azure Key Vault.
    /// Source: FR-002 - Store Client Secret in Azure Key Vault
    /// </summary>
    public async Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // FR-010: Structured logging - sanitize to prevent credential leakage
            _logger.LogDebug("Retrieving Client Secret from Key Vault secret: {SecretName}",
                _configuration.ClientSecretSecretName);

            var secret = await _secretClient.GetSecretAsync(
                _configuration.ClientSecretSecretName,
                cancellationToken: cancellationToken);

            // FR-011: Audit Key Vault access (logged by Azure SDK telemetry)
            _logger.LogDebug("Successfully retrieved Client Secret from Key Vault");

            // FR-011: Never log the actual secret value
            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            // FR-010: Log errors without exposing sensitive data
            _logger.LogError(ex, "Failed to retrieve Client Secret from Key Vault. Secret: {SecretName}",
                _configuration.ClientSecretSecretName);
            throw;
        }
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
}
