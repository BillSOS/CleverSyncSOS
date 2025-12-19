namespace CleverSyncSOS.Core.Services;

/// <summary>
/// Result of a lock acquisition attempt.
/// </summary>
public class LockAcquisitionResult
{
    /// <summary>
    /// Whether the lock was successfully acquired.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The unique lock ID (only set if Success is true).
    /// Use this ID when releasing or extending the lock.
    /// </summary>
    public string? LockId { get; set; }

    /// <summary>
    /// Who currently holds the lock (set whether success or failure).
    /// </summary>
    public string? CurrentHolder { get; set; }

    /// <summary>
    /// Who initiated the sync that holds the lock.
    /// </summary>
    public string? CurrentHolderInitiatedBy { get; set; }

    /// <summary>
    /// When the current holder acquired the lock.
    /// </summary>
    public DateTime? CurrentHolderAcquiredAt { get; set; }

    /// <summary>
    /// Error message if acquisition failed for reasons other than existing lock.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Information about a current lock.
/// </summary>
public class LockInfo
{
    /// <summary>
    /// The scope being locked.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Whether the scope is currently locked.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// The source holding the lock (e.g., "AdminPortal", "AzureFunction").
    /// </summary>
    public string? AcquiredBy { get; set; }

    /// <summary>
    /// User or process that initiated the sync.
    /// </summary>
    public string? InitiatedBy { get; set; }

    /// <summary>
    /// When the lock was acquired.
    /// </summary>
    public DateTime? AcquiredAt { get; set; }

    /// <summary>
    /// When the lock expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Machine name holding the lock.
    /// </summary>
    public string? MachineName { get; set; }
}

/// <summary>
/// Service for distributed sync locking across Admin Portal and Azure Functions.
/// Uses database row-level locking to prevent concurrent sync operations.
/// </summary>
public interface ISyncLockService
{
    /// <summary>
    /// Attempts to acquire a lock for the specified scope.
    /// </summary>
    /// <param name="scope">The scope to lock (e.g., "school:123", "district:abc", "global").</param>
    /// <param name="acquiredBy">Source acquiring the lock (e.g., "AdminPortal", "AzureFunction").</param>
    /// <param name="initiatedBy">Optional user/process that initiated the operation.</param>
    /// <param name="durationMinutes">How long the lock should be held (default 30 minutes).</param>
    /// <returns>Result indicating success/failure and lock details.</returns>
    Task<LockAcquisitionResult> TryAcquireLockAsync(
        string scope,
        string acquiredBy,
        string? initiatedBy = null,
        int durationMinutes = 30);

    /// <summary>
    /// Releases a lock. Only succeeds if the provided lockId matches.
    /// </summary>
    /// <param name="scope">The scope to unlock.</param>
    /// <param name="lockId">The lock ID received when acquiring the lock.</param>
    /// <returns>True if released successfully, false if lock not found or not owned.</returns>
    Task<bool> ReleaseLockAsync(string scope, string lockId);

    /// <summary>
    /// Extends the duration of an existing lock (heartbeat).
    /// </summary>
    /// <param name="scope">The scope to extend.</param>
    /// <param name="lockId">The lock ID received when acquiring the lock.</param>
    /// <param name="extendMinutes">Additional minutes to extend (default 30).</param>
    /// <returns>True if extended successfully, false if lock not found or not owned.</returns>
    Task<bool> ExtendLockAsync(string scope, string lockId, int extendMinutes = 30);

    /// <summary>
    /// Checks if a scope is currently locked.
    /// </summary>
    /// <param name="scope">The scope to check.</param>
    /// <returns>True if locked, false otherwise.</returns>
    Task<bool> IsLockedAsync(string scope);

    /// <summary>
    /// Gets detailed information about a lock.
    /// </summary>
    /// <param name="scope">The scope to get info for.</param>
    /// <returns>Lock information, or null if not locked.</returns>
    Task<LockInfo?> GetLockInfoAsync(string scope);

    /// <summary>
    /// Cleans up expired locks. Called periodically or on-demand.
    /// </summary>
    /// <returns>Number of expired locks removed.</returns>
    Task<int> CleanupExpiredLocksAsync();
}
