// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-002 Credential Storage
//   plan: SpecKit/Plans/003-key-vault-naming-standardization/plan.md
//   phase: Core Implementation
//   version: 2.0.0
// ---

using System;

namespace CleverSyncSOS.Core.Authentication;

/// <summary>
/// Interface for secure credential storage and retrieval.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-002)
/// Requirement: Store Client ID and Client Secret in Azure Key Vault, retrieve using managed identity.
///
/// Naming Convention (v2.0):
/// - Global secrets: {FunctionalName} (e.g., "ClientId", "SuperAdminPassword")
/// - District secrets: {KeyVaultDistrictPrefix}--{FunctionalName} (e.g., "NorthCentral--ApiToken")
/// - School secrets: {KeyVaultSchoolPrefix}--{FunctionalName} (e.g., "CityHighSchool--ConnectionString")
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Retrieves a global system-wide secret by functional name.
    /// Format: {functionalName} (e.g., "ClientId", "SuperAdminPassword")
    /// Use constants from KeyVaultSecretNaming.Global for functional names.
    /// </summary>
    /// <param name="functionalName">The functional name of the secret (e.g., "ClientId", "SuperAdminPassword")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The secret value</returns>
    Task<string> GetGlobalSecretAsync(string functionalName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a secret scoped to a specific district.
    /// Format: {keyVaultDistrictPrefix}--{functionalName}
    /// The keyVaultDistrictPrefix comes from Districts.KeyVaultDistrictPrefix column.
    /// Use constants from KeyVaultSecretNaming.District for functional names.
    /// </summary>
    /// <param name="keyVaultDistrictPrefix">District prefix from Districts.KeyVaultDistrictPrefix (e.g., "NorthCentral")</param>
    /// <param name="functionalName">Functional name (e.g., "ApiToken", "ContactEmail")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The secret value</returns>
    Task<string> GetDistrictSecretAsync(string keyVaultDistrictPrefix, string functionalName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a secret scoped to a specific school.
    /// Format: {keyVaultSchoolPrefix}--{functionalName}
    /// The keyVaultSchoolPrefix comes from Schools.KeyVaultSchoolPrefix column.
    /// Use constants from KeyVaultSecretNaming.School for functional names.
    /// </summary>
    /// <param name="keyVaultSchoolPrefix">School prefix from Schools.KeyVaultSchoolPrefix (e.g., "CityHighSchool")</param>
    /// <param name="functionalName">Functional name (e.g., "ConnectionString", "ApiKey")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The secret value</returns>
    Task<string> GetSchoolSecretAsync(string keyVaultSchoolPrefix, string functionalName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a generic secret from secure storage by exact secret name.
    /// Use this when you need to retrieve a secret with a non-standard name.
    /// </summary>
    /// <param name="secretName">The exact name of the secret to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The secret value</returns>
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);

    // ========================================
    // DEPRECATED METHODS - For backward compatibility only
    // These will be removed in a future version
    // ========================================

    /// <summary>
    /// DEPRECATED: Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId) instead.
    /// Retrieves the Clever API Client ID from secure storage.
    /// </summary>
    [Obsolete("Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientId) instead. This method will be removed in v3.0.")]
    Task<string> GetClientIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// DEPRECATED: Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret) instead.
    /// Retrieves the Clever API Client Secret from secure storage.
    /// </summary>
    [Obsolete("Use GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret) instead. This method will be removed in v3.0.")]
    Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default);
}
