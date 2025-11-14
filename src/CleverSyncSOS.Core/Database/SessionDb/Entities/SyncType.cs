namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Defines the type of synchronization operation.
/// </summary>
public enum SyncType
{
    /// <summary>
    /// Complete refresh of all data from Clever.
    /// Used for new schools or beginning of school year.
    /// Marks records not in Clever as inactive.
    /// </summary>
    Full = 1,

    /// <summary>
    /// Syncs only records modified since last sync.
    /// Used for routine updates during school year.
    /// </summary>
    Incremental = 2,

    /// <summary>
    /// Validates data integrity between Clever and local database.
    /// Used for periodic reconciliation checks.
    /// </summary>
    Reconciliation = 3
}
