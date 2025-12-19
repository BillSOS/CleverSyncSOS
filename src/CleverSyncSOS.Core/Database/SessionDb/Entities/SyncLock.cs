using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Represents a distributed lock for sync operations.
/// Uses database row-level locking to prevent concurrent syncs across Admin Portal and Azure Functions.
/// </summary>
[Table("SyncLock")]
public class SyncLock
{
    /// <summary>
    /// The scope being locked (e.g., "school:123", "district:abc123", "all").
    /// This is the primary key and lock identifier.
    /// </summary>
    [Key]
    [MaxLength(100)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for this lock acquisition.
    /// Used to verify ownership when releasing the lock.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string LockId { get; set; } = string.Empty;

    /// <summary>
    /// The source that acquired the lock (e.g., "AdminPortal", "AzureFunction", "Timer").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string AcquiredBy { get; set; } = string.Empty;

    /// <summary>
    /// Optional identifier for the user or process that initiated the sync.
    /// </summary>
    [MaxLength(255)]
    public string? InitiatedBy { get; set; }

    /// <summary>
    /// When the lock was acquired (UTC).
    /// </summary>
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the lock expires (UTC).
    /// Locks automatically expire after this time to prevent orphaned locks.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Optional heartbeat timestamp to extend lock duration for long-running syncs.
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// Machine/instance name that holds the lock (for debugging).
    /// </summary>
    [MaxLength(100)]
    public string? MachineName { get; set; }
}
