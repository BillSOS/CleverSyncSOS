// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-003 Token Management
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

namespace CleverSyncSOS.Core.Configuration;

/// <summary>
/// Represents an OAuth access token from Clever API.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-003)
/// Requirement: Cache tokens in memory and prevent expired token usage.
/// </summary>
public class CleverAuthToken
{
    /// <summary>
    /// The OAuth access token.
    /// Source: FR-001 - OAuth 2.0 client credentials flow
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Token type (typically "Bearer").
    /// Source: FR-001 - OAuth 2.0 standard
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// Token expiration time in seconds from issuance.
    /// Source: FR-003 - Token lifetime tracking
    /// Note: A value of 0 or less indicates a non-expiring token (e.g., Clever district tokens).
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Timestamp when the token was issued (UTC).
    /// Source: FR-003 - Prevent expired token usage
    /// </summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculates the absolute expiration time of the token.
    /// Source: FR-003 - Prevent expired token usage
    /// Note: Returns DateTime.MaxValue for non-expiring tokens (ExpiresIn <= 0).
    /// </summary>
    public DateTime ExpiresAt => ExpiresIn > 0 ? IssuedAt.AddSeconds(ExpiresIn) : DateTime.MaxValue;

    /// <summary>
    /// Determines if the token is expired.
    /// Source: FR-003 - Prevent expired token usage
    /// Note: Non-expiring tokens (ExpiresIn <= 0) never expire.
    /// </summary>
    public bool IsExpired => ExpiresIn > 0 && DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Determines if the token should be refreshed based on the threshold percentage.
    /// Source: FR-003 - Refresh tokens proactively at 75% of their lifetime
    /// Note: Non-expiring tokens (ExpiresIn <= 0) never need refresh.
    /// </summary>
    /// <param name="thresholdPercent">Percentage of lifetime at which to refresh (default 75%)</param>
    /// <returns>True if the token should be refreshed</returns>
    public bool ShouldRefresh(double thresholdPercent = 75.0)
    {
        // Non-expiring tokens never need refresh
        if (ExpiresIn <= 0)
            return false;

        if (IsExpired)
            return true;

        var lifetime = ExpiresIn;
        var elapsedTime = (DateTime.UtcNow - IssuedAt).TotalSeconds;
        var percentageElapsed = (elapsedTime / lifetime) * 100;

        return percentageElapsed >= thresholdPercent;
    }

    /// <summary>
    /// Gets the remaining time until token expiration.
    /// Source: FR-003 - Token Management
    /// Note: Returns TimeSpan.MaxValue for non-expiring tokens (ExpiresIn <= 0).
    /// </summary>
    public TimeSpan TimeUntilExpiration => ExpiresIn > 0 ? ExpiresAt - DateTime.UtcNow : TimeSpan.MaxValue;
}
