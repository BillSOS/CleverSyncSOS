using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using CleverSyncSOS.Core.Services;
using CleverSyncSOS.Core.Sync.Workshop;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Shared context passed to all entity sync handlers.
/// Contains common dependencies and state needed during sync operations.
/// </summary>
public class SyncContext
{
    /// <summary>
    /// The school being synced.
    /// </summary>
    public required School School { get; init; }

    /// <summary>
    /// The school-specific database context.
    /// </summary>
    public required SchoolDbContext SchoolDb { get; init; }

    /// <summary>
    /// The central session database context.
    /// </summary>
    public required SessionDbContext SessionDb { get; init; }

    /// <summary>
    /// The sync result being accumulated.
    /// </summary>
    public required SyncResult Result { get; init; }

    /// <summary>
    /// Time context for the school's timezone.
    /// </summary>
    public required ISchoolTimeContext TimeContext { get; init; }

    /// <summary>
    /// Optional progress reporter for UI updates.
    /// </summary>
    public IProgress<SyncProgress>? Progress { get; init; }

    /// <summary>
    /// Cancellation token for the operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// The sync start time (UTC) for orphan detection.
    /// </summary>
    public DateTime SyncStartTime { get; init; }

    /// <summary>
    /// Last modified timestamp for incremental sync filtering.
    /// Null for full sync.
    /// </summary>
    public DateTime? LastModified { get; init; }

    /// <summary>
    /// Workshop sync tracker for detecting grade changes and workshop-relevant modifications.
    /// </summary>
    public WorkshopSyncTracker? WorkshopTracker { get; init; }

    /// <summary>
    /// Set of section IDs that are linked to workshops (for warning generation).
    /// </summary>
    public HashSet<int> WorkshopLinkedSectionIds { get; init; } = new();

    /// <summary>
    /// The sync ID for the student sync (needed for workshop sync).
    /// </summary>
    public int StudentSyncId { get; set; }

    /// <summary>
    /// The sync ID for the section sync (needed for workshop sync).
    /// </summary>
    public int SectionSyncId { get; set; }
}
