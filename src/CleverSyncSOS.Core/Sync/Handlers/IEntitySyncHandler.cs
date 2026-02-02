namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Base interface for entity-specific sync handlers.
/// Each handler is responsible for syncing a single entity type (Student, Teacher, Section, Term).
/// </summary>
/// <typeparam name="TCleverEntity">The Clever API model type for this entity.</typeparam>
public interface IEntitySyncHandler<TCleverEntity>
{
    /// <summary>
    /// The entity type name (e.g., "Student", "Teacher", "Section", "Term").
    /// Used for logging and sync history records.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Performs a full sync of all entities from Clever API.
    /// </summary>
    /// <param name="context">The sync context containing shared dependencies.</param>
    /// <param name="startPercent">Starting percentage for progress reporting.</param>
    /// <param name="endPercent">Ending percentage for progress reporting.</param>
    /// <returns>The SyncId from the created SyncHistory record.</returns>
    Task<int> SyncAllAsync(SyncContext context, int startPercent, int endPercent);

    /// <summary>
    /// Upserts a single entity from Clever API data.
    /// </summary>
    /// <param name="context">The sync context.</param>
    /// <param name="cleverEntity">The entity data from Clever API.</param>
    /// <param name="syncId">The current sync ID for change tracking.</param>
    /// <param name="changeTracker">The change tracker for audit logging.</param>
    /// <returns>True if changes were made, false if no changes detected.</returns>
    Task<bool> UpsertAsync(
        SyncContext context,
        TCleverEntity cleverEntity,
        int syncId,
        ChangeTracker changeTracker);

    /// <summary>
    /// Handles a delete event for this entity type.
    /// </summary>
    /// <param name="context">The sync context.</param>
    /// <param name="cleverId">The Clever ID of the entity to delete.</param>
    /// <param name="syncId">The current sync ID for change tracking.</param>
    /// <param name="changeTracker">The change tracker for audit logging.</param>
    /// <returns>True if the entity was found and deleted.</returns>
    Task<bool> HandleDeleteAsync(
        SyncContext context,
        string cleverId,
        int syncId,
        ChangeTracker changeTracker);
}

/// <summary>
/// Extended interface for handlers that need orphan detection during full sync.
/// </summary>
public interface IOrphanDetectingSyncHandler
{
    /// <summary>
    /// Detects and soft-deletes orphaned entities not seen during sync.
    /// Called after full sync to clean up records that no longer exist in Clever.
    /// </summary>
    /// <param name="context">The sync context.</param>
    /// <param name="syncId">The current sync ID.</param>
    /// <param name="changeTracker">The change tracker for audit logging.</param>
    Task DetectOrphansAsync(SyncContext context, int syncId, ChangeTracker changeTracker);
}
