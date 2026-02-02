using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Handles synchronization of Term entities from Clever API.
/// Terms are district-level entities in Clever but stored per-school for data isolation.
/// </summary>
public class TermSyncHandler : IEntitySyncHandler<CleverTerm>, IOrphanDetectingSyncHandler
{
    private readonly ICleverApiClient _cleverClient;
    private readonly ISyncValidationService _validationService;
    private readonly ILogger<TermSyncHandler> _logger;

    public string EntityType => "Term";

    public TermSyncHandler(
        ICleverApiClient cleverClient,
        ISyncValidationService validationService,
        ILogger<TermSyncHandler> logger)
    {
        _cleverClient = cleverClient;
        _validationService = validationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SyncAllAsync(SyncContext context, int startPercent, int endPercent)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = context.School.SchoolId,
            EntityType = EntityType,
            SyncType = context.Result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = context.SyncStartTime
        };

        context.SessionDb.SyncHistory.Add(syncHistory);
        await context.SessionDb.SaveChangesAsync(context.CancellationToken);

        var changeTracker = new ChangeTracker(context.SessionDb, _logger);

        try
        {
            var cleverTerms = await _cleverClient.GetTermsAsync(
                context.School.CleverSchoolId,
                context.CancellationToken);

            _logger.LogDebug("Fetched {Count} terms from Clever API for school {SchoolId}",
                cleverTerms.Length, context.School.SchoolId);

            int totalTerms = cleverTerms.Length;

            for (int i = 0; i < cleverTerms.Length; i++)
            {
                var cleverTerm = cleverTerms[i];
                try
                {
                    context.Result.TermsProcessed++;
                    bool hasChanges = await UpsertAsync(context, cleverTerm, syncHistory.SyncId, changeTracker);
                    if (hasChanges)
                    {
                        context.Result.TermsUpdated++;
                    }

                    if ((i + 1) % 10 == 0 || i == totalTerms - 1)
                    {
                        context.Progress?.Report(new SyncProgress
                        {
                            CurrentOperation = $"Processing {context.Result.TermsProcessed}/{totalTerms} terms, {context.Result.TermsUpdated} updated",
                            TermsProcessed = context.Result.TermsProcessed,
                            TermsUpdated = context.Result.TermsUpdated,
                            TermsFailed = context.Result.TermsFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert term {CleverTermId} for school {SchoolId}",
                        cleverTerm.Id, context.School.SchoolId);
                    context.Result.TermsFailed++;
                }
            }

            await changeTracker.SaveChangesAsync(context.CancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = context.Result.TermsProcessed;
            syncHistory.RecordsUpdated = context.Result.TermsUpdated;
            syncHistory.RecordsFailed = context.Result.TermsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync terms for school {SchoolId}", context.School.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            await context.SessionDb.SaveChangesAsync(context.CancellationToken);
        }

        return syncHistory.SyncId;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        SyncContext context,
        CleverTerm cleverTerm,
        int syncId,
        ChangeTracker changeTracker)
    {
        var term = await context.SchoolDb.Terms
            .FirstOrDefaultAsync(t => t.CleverTermId == cleverTerm.Id, context.CancellationToken);

        var now = context.SyncStartTime;
        bool hasChanges = false;

        // Parse dates - StartDate and EndDate are required
        if (string.IsNullOrEmpty(cleverTerm.StartDate) || string.IsNullOrEmpty(cleverTerm.EndDate))
        {
            _logger.LogWarning("Skipping term {CleverTermId} ({Name}) - missing start or end date",
                cleverTerm.Id, cleverTerm.Name);
            return false;
        }

        if (!DateTime.TryParse(cleverTerm.StartDate, out var startDate) ||
            !DateTime.TryParse(cleverTerm.EndDate, out var endDate))
        {
            _logger.LogWarning("Skipping term {CleverTermId} ({Name}) - invalid start or end date format",
                cleverTerm.Id, cleverTerm.Name);
            return false;
        }

        if (term == null)
        {
            term = new Term
            {
                CleverTermId = cleverTerm.Id,
                CleverDistrictId = cleverTerm.District,
                Name = cleverTerm.Name ?? "Unnamed Term",
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = now,
                UpdatedAt = now,
                LastSyncedAt = now
            };
            context.SchoolDb.Terms.Add(term);
            hasChanges = true;

            changeTracker.TrackTermChange(syncId, null, term, "Created");
        }
        else
        {
            term.LastSyncedAt = now;

            var nameChanged = !_validationService.StringsEqual(term.Name, cleverTerm.Name);
            var startDateChanged = term.StartDate != startDate;
            var endDateChanged = term.EndDate != endDate;
            var wasDeleted = term.DeletedAt != null;

            if (nameChanged || startDateChanged || endDateChanged || wasDeleted)
            {
                var oldTerm = new Term
                {
                    CleverTermId = term.CleverTermId,
                    CleverDistrictId = term.CleverDistrictId,
                    Name = term.Name,
                    StartDate = term.StartDate,
                    EndDate = term.EndDate
                };

                term.Name = cleverTerm.Name;
                term.StartDate = startDate;
                term.EndDate = endDate;
                term.UpdatedAt = now;
                term.DeletedAt = null;
                hasChanges = true;

                changeTracker.TrackTermChange(syncId, oldTerm, term, "Updated");

                if (wasDeleted)
                {
                    _logger.LogInformation("Term {CleverTermId} ({Name}) was restored (previously deleted)",
                        term.CleverTermId, term.Name);
                }
            }
        }

        if (hasChanges)
        {
            await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
        }

        return hasChanges;
    }

    /// <inheritdoc />
    public async Task<bool> HandleDeleteAsync(
        SyncContext context,
        string cleverId,
        int syncId,
        ChangeTracker changeTracker)
    {
        var term = await context.SchoolDb.Terms
            .FirstOrDefaultAsync(t => t.CleverTermId == cleverId, context.CancellationToken);

        if (term == null || term.DeletedAt != null)
        {
            return false;
        }

        var now = context.TimeContext.Now;
        term.DeletedAt = now;
        term.UpdatedAt = now;

        changeTracker.TrackTermChange(syncId, term, null, "Deleted");
        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Soft-deleted term {TermId} ({CleverId}) via event",
            term.TermId, cleverId);

        return true;
    }

    /// <inheritdoc />
    public async Task DetectOrphansAsync(SyncContext context, int syncId, ChangeTracker changeTracker)
    {
        // Only detect orphans for Clever-synced terms (not manual terms)
        var orphanedTerms = await context.SchoolDb.Terms
            .Where(t => t.LastSyncedAt < context.SyncStartTime && t.DeletedAt == null && !t.IsManual)
            .ToListAsync(context.CancellationToken);

        if (orphanedTerms.Count > 0)
        {
            _logger.LogInformation("Found {Count} orphaned terms to soft-delete for school {SchoolId}",
                orphanedTerms.Count, context.School.SchoolId);

            var now = context.TimeContext.Now;
            foreach (var term in orphanedTerms)
            {
                term.DeletedAt = now;
                term.UpdatedAt = now;
                changeTracker.TrackTermChange(syncId, term, null, "Orphaned");
            }

            await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
        }
    }
}
