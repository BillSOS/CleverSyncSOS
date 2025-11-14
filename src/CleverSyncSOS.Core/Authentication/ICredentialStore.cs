// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-002 Credential Storage
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

namespace CleverSyncSOS.Core.Authentication;

/// <summary>
/// Interface for secure credential storage and retrieval.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-002)
/// Requirement: Store Client ID and Client Secret in Azure Key Vault, retrieve using managed identity.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Retrieves the Clever API Client ID from secure storage.
    /// Source: FR-002 - Credential Storage
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The Client ID</returns>
    Task<string> GetClientIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the Clever API Client Secret from secure storage.
    /// Source: FR-002 - Credential Storage
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The Client Secret</returns>
    Task<string> GetClientSecretAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a generic secret from secure storage by secret name.
    /// Source: FR-019 - Connection Management (Stage 2)
    /// </summary>
    /// <param name="secretName">The name of the secret to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The secret value</returns>
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}
