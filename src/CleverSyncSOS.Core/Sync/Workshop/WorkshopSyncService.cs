using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Workshop;

/// <summary>
/// Service for executing workshop synchronization after sync operations.
/// Calls the stored procedure spSyncWorkshops_FromSectionsAndGrades_WithAudit
/// when workshop-relevant changes are detected.
/// </summary>
public class WorkshopSyncService : IWorkshopSyncService
{
    private readonly SessionDbContext _sessionDb;
    private readonly ILogger<WorkshopSyncService> _logger;

    public WorkshopSyncService(
        SessionDbContext sessionDb,
        ILogger<WorkshopSyncService> logger)
    {
        _sessionDb = sessionDb;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkshopSyncResult> ExecuteWorkshopSyncAsync(
        SchoolDbContext schoolDb,
        int syncId,
        WorkshopSyncTracker workshopTracker,
        CancellationToken cancellationToken = default)
    {
        if (!workshopTracker.RequiresWorkshopSync)
        {
            _logger.LogDebug("Workshop sync not required - no workshop-relevant changes detected");
            return new WorkshopSyncResult
            {
                Success = true,
                Skipped = true,
                ChangesSummary = workshopTracker.GetSummary()
            };
        }

        _logger.LogInformation(
            "Executing workshop sync stored procedure for SyncId {SyncId}. Changes: {Summary}",
            syncId, workshopTracker.GetSummary());

        try
        {
            // Execute the stored procedure using ADO.NET for raw SQL
            var connection = schoolDb.Database.GetDbConnection();
            var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;

            if (!connectionWasOpen)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "EXEC spSyncWorkshops_FromSectionsAndGrades_WithAudit @SyncId";
                command.CommandType = System.Data.CommandType.Text;

                var syncIdParam = command.CreateParameter();
                syncIdParam.ParameterName = "@SyncId";
                syncIdParam.Value = syncId;
                syncIdParam.DbType = System.Data.DbType.Int32;
                command.Parameters.Add(syncIdParam);

                // Execute with timeout
                command.CommandTimeout = 120; // 2 minutes for potentially large operations

                await command.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation(
                    "Workshop sync stored procedure completed successfully for SyncId {SyncId}",
                    syncId);

                return new WorkshopSyncResult
                {
                    Success = true,
                    Skipped = false,
                    ChangesSummary = workshopTracker.GetSummary()
                };
            }
            finally
            {
                if (!connectionWasOpen)
                {
                    await connection.CloseAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to execute workshop sync stored procedure for SyncId {SyncId}",
                syncId);

            // Create a SyncWarning record for the failure
            await CreateWarningAsync(syncId, workshopTracker, ex.Message, cancellationToken);

            return new WorkshopSyncResult
            {
                Success = false,
                Skipped = false,
                ErrorMessage = ex.Message,
                ChangesSummary = workshopTracker.GetSummary()
            };
        }
    }

    /// <inheritdoc />
    public async Task<HashSet<int>> GetWorkshopLinkedSectionIdsAsync(
        SchoolDbContext schoolDb,
        CancellationToken cancellationToken = default)
    {
        return await schoolDb.WorkshopSections
            .Select(ws => ws.SectionId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a SyncWarning record for a failed workshop sync.
    /// </summary>
    private async Task CreateWarningAsync(
        int syncId,
        WorkshopSyncTracker workshopTracker,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var warning = new SyncWarning
        {
            SyncId = syncId,
            WarningType = "WorkshopSyncFailed",
            EntityType = "Workshop",
            EntityId = 0,
            CleverEntityId = string.Empty,
            EntityName = "Workshop Sync",
            Message = $"Workshop sync stored procedure failed: {errorMessage}. " +
                     $"Changes that triggered sync: {workshopTracker.GetSummary()}",
            AffectedWorkshops = null,
            AffectedWorkshopCount = 0,
            IsAcknowledged = false,
            CreatedAt = DateTime.UtcNow
        };

        _sessionDb.SyncWarnings.Add(warning);
        await _sessionDb.SaveChangesAsync(cancellationToken);
    }
}
