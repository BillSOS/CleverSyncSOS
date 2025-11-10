// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-005 Health Check Endpoint
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Health & Observability (Stage 3)
//   version: 1.0.0
// ---

namespace CleverSyncSOS.Core.Health;

/// <summary>
/// Represents the health status of the Clever API authentication system.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-005)
/// Requirement: Health check endpoint returns last successful auth timestamp and error status.
/// Note: Full implementation scheduled for Stage 3 (Health & Observability).
/// </summary>
public class AuthenticationHealthStatus
{
    /// <summary>
    /// Indicates whether the authentication system is healthy.
    /// Source: FR-005 - Health Check Endpoint
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Timestamp of the last successful authentication (UTC).
    /// Source: FR-005 - Return last successful authentication timestamp
    /// </summary>
    public DateTime? LastSuccessfulAuthentication { get; set; }

    /// <summary>
    /// Current error message, if any.
    /// Source: FR-005 - Return error status
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Time when the health check was performed (UTC).
    /// Source: NFR-001 - Health check must respond in < 100ms
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the health check in milliseconds.
    /// Source: NFR-001 - Performance monitoring
    /// </summary>
    public long DurationMs { get; set; }
}
