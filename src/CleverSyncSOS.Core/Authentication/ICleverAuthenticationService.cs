// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-001 OAuth Authentication, FR-003 Token Management
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Configuration;

namespace CleverSyncSOS.Core.Authentication;

/// <summary>
/// Interface for Clever API authentication service.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-001, FR-003)
/// Requirement: Authenticate using OAuth 2.0, manage token lifecycle.
/// </summary>
public interface ICleverAuthenticationService
{
    /// <summary>
    /// Authenticates with Clever API using OAuth 2.0 client credentials flow.
    /// Source: FR-001 - OAuth Authentication
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An authenticated access token</returns>
    Task<CleverAuthToken> AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current cached token, refreshing if necessary.
    /// Source: FR-003 - Cache tokens in memory, refresh proactively at 75% of lifetime
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A valid access token</returns>
    Task<CleverAuthToken> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current cached token without forcing a refresh.
    /// Source: FR-003 - Token Management
    /// </summary>
    /// <returns>The current token or null if not authenticated</returns>
    CleverAuthToken? GetCurrentToken();

    /// <summary>
    /// Gets the timestamp of the last successful authentication.
    /// Source: FR-005 - Health check endpoint requires last successful authentication timestamp
    /// </summary>
    /// <returns>The timestamp of last successful authentication, or null if never authenticated</returns>
    DateTime? GetLastSuccessfulAuthTime();

    /// <summary>
    /// Gets the last error encountered during authentication, if any.
    /// Source: FR-005 - Health check endpoint requires error status
    /// </summary>
    /// <returns>The last error message, or null if no errors</returns>
    string? GetLastError();
}
