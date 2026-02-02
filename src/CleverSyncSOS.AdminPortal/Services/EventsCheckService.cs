using System.Text.Json;
using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for checking Clever Events API and logging results
/// </summary>
public class EventsCheckService : IEventsCheckService
{
    private readonly IDbContextFactory<SessionDbContext> _dbContextFactory;
    private readonly ICleverApiClient _cleverApiClient;
    private readonly ILogger<EventsCheckService> _logger;

    public EventsCheckService(
        IDbContextFactory<SessionDbContext> dbContextFactory,
        ICleverApiClient cleverApiClient,
        ILogger<EventsCheckService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _cleverApiClient = cleverApiClient;
        _logger = logger;
    }

    public async Task<EventsLog> CheckAndLogEventsAsync(string? checkedBy = null)
    {
        _logger.LogInformation("Checking Clever Events API, initiated by {CheckedBy}", checkedBy ?? "system");

        var eventsLog = new EventsLog
        {
            CheckedAt = DateTime.UtcNow,
            CheckedBy = checkedBy
        };

        try
        {
            // Fetch events from Clever API (limit to 100 for the check)
            var events = await _cleverApiClient.GetEventsAsync(limit: 100);

            eventsLog.ApiAccessible = true;
            eventsLog.EventCount = events.Length;

            if (events.Length > 0)
            {
                // Count by action type (e.g., "users.created" -> "created")
                eventsLog.CreatedCount = events.Count(e => e.ActionType == "created");
                eventsLog.UpdatedCount = events.Count(e => e.ActionType == "updated");
                eventsLog.DeletedCount = events.Count(e => e.ActionType == "deleted");

                // Get time range (handle nullable Created)
                var eventsWithTime = events.Where(e => e.Created.HasValue).ToList();
                if (eventsWithTime.Any())
                {
                    eventsLog.EarliestEventTime = eventsWithTime.Min(e => e.Created!.Value);
                    eventsLog.LatestEventTime = eventsWithTime.Max(e => e.Created!.Value);
                }
                eventsLog.LatestEventId = events.First().Id; // First is most recent
                _logger.LogInformation("Storing LatestEventId in EventsLog: {EventId}", eventsLog.LatestEventId);

                // Summarize object types (e.g., "users.created" -> "users")
                var objectTypeCounts = events
                    .GroupBy(e => e.ObjectType)
                    .Select(g => $"{g.Key}: {g.Count()}")
                    .ToList();
                eventsLog.ObjectTypeSummary = string.Join(", ", objectTypeCounts);

                // Store sample of first 5 events (summary info only)
                var sampleEvents = events.Take(5).Select(e => new
                {
                    e.Id,
                    e.Type,
                    e.Created,
                    ObjectType = e.ObjectType,
                    ActionType = e.ActionType,
                    ObjectId = e.Data.Id
                });
                eventsLog.SampleEventsJson = JsonSerializer.Serialize(sampleEvents);

                _logger.LogInformation(
                    "Found {EventCount} events: {Created} created, {Updated} updated, {Deleted} deleted. Object types: {ObjectTypes}",
                    events.Length, eventsLog.CreatedCount, eventsLog.UpdatedCount, eventsLog.DeletedCount,
                    eventsLog.ObjectTypeSummary);
            }
            else
            {
                _logger.LogInformation("Events API accessible but no events found");
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            eventsLog.ApiAccessible = false;
            eventsLog.ErrorMessage = "403 Forbidden - Events API scope not granted. Request 'read:events' scope in Clever app settings.";
            _logger.LogWarning(ex, "Events API returned 403 Forbidden");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            eventsLog.ApiAccessible = false;
            eventsLog.ErrorMessage = "404 Not Found - Events API not available for this app type.";
            _logger.LogWarning(ex, "Events API returned 404 Not Found");
        }
        catch (Exception ex)
        {
            eventsLog.ApiAccessible = false;
            eventsLog.ErrorMessage = $"Error checking Events API: {ex.Message}";
            _logger.LogError(ex, "Failed to check Events API");
        }

        // Save to database
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            dbContext.EventsLogs.Add(eventsLog);
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Events check logged with ID {EventsLogId}", eventsLog.EventsLogId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save events log to database");
            // Don't throw - still return the check results even if we couldn't log them
        }

        return eventsLog;
    }

    public async Task<List<EventsLog>> GetRecentChecksAsync(int count = 20)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.EventsLogs
            .OrderByDescending(e => e.CheckedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<EventsLog?> GetLastCheckWithEventsAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.EventsLogs
            .Where(e => e.ApiAccessible && e.EventCount > 0)
            .OrderByDescending(e => e.CheckedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<string?> InitializeEventBaselineAsync(int schoolId, string? initializedBy = null)
    {
        _logger.LogInformation("Initializing event baseline for school {SchoolId} by {InitializedBy}",
            schoolId, initializedBy ?? "system");

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // Get the school to verify it exists
        var school = await dbContext.Schools.FindAsync(schoolId);
        if (school == null)
        {
            throw new ArgumentException($"School with ID {schoolId} not found");
        }

        // Get the latest event ID from the most recent events check log
        // This uses the stored LatestEventId from when "Check Events Now" was clicked
        string? latestEventId = null;

        // First try to get from the most recent events log that found events
        // Check for both null and empty string since LatestEventId could be either
        var latestEventsLog = await dbContext.EventsLogs
            .Where(e => e.ApiAccessible && e.EventCount > 0 && e.LatestEventId != null && e.LatestEventId != "")
            .OrderByDescending(e => e.CheckedAt)
            .FirstOrDefaultAsync();

        _logger.LogInformation("Query for EventsLog with LatestEventId returned: {Found}, EventId: {EventId}",
            latestEventsLog != null, latestEventsLog?.LatestEventId ?? "(null)");

        if (latestEventsLog != null && !string.IsNullOrEmpty(latestEventsLog.LatestEventId))
        {
            latestEventId = latestEventsLog.LatestEventId;
            _logger.LogInformation("Using latest event ID from EventsLog: {EventId} (checked at {CheckedAt})",
                latestEventId, latestEventsLog.CheckedAt);
        }
        else
        {
            // Fallback: call the API directly
            _logger.LogInformation("No EventsLog with valid LatestEventId found, calling API directly");
            try
            {
                var events = await _cleverApiClient.GetEventsAsync(limit: 10);
                _logger.LogInformation("GetEventsAsync returned {Count} events", events.Length);

                if (events.Length > 0)
                {
                    latestEventId = events.First().Id;
                    _logger.LogInformation("Latest event ID from API: {EventId}, Type: {Type}", latestEventId, events.First().Type);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get latest event ID from Clever API");
                throw;
            }
        }

        // Create a baseline sync history entry
        var baselineEntry = new SyncHistory
        {
            SchoolId = schoolId,
            EntityType = "Baseline",
            SyncType = SyncType.Full,
            SyncStartTime = DateTime.UtcNow,
            SyncEndTime = DateTime.UtcNow,
            Status = "Success",
            RecordsProcessed = 0,
            RecordsUpdated = 0,
            RecordsFailed = 0,
            LastEventId = latestEventId,
            LastSyncTimestamp = DateTime.UtcNow
        };

        dbContext.SyncHistory.Add(baselineEntry);
        await dbContext.SaveChangesAsync();

        if (!string.IsNullOrEmpty(latestEventId))
        {
            _logger.LogInformation(
                "Event baseline initialized for school {SchoolId}. Latest event ID: {EventId}. Future incremental syncs will start from this point.",
                schoolId, latestEventId);
        }
        else
        {
            _logger.LogWarning(
                "Event baseline initialized for school {SchoolId} but no events exist yet. Incremental syncs will use timestamp-based change detection.",
                schoolId);
        }

        return latestEventId;
    }

    public async Task<Dictionary<int, string?>> GetEventBaselineStatusAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // Get all active schools
        var schools = await dbContext.Schools
            .Where(s => s.IsActive)
            .Select(s => new { s.SchoolId, s.Name })
            .ToListAsync();

        var result = new Dictionary<int, string?>();

        foreach (var school in schools)
        {
            // Get the most recent successful sync with a LastEventId for this school
            // Check for both null and empty string
            var lastEventId = await dbContext.SyncHistory
                .Where(h => h.SchoolId == school.SchoolId && h.Status == "Success" && h.LastEventId != null && h.LastEventId != "")
                .OrderByDescending(h => h.SyncEndTime)
                .Select(h => h.LastEventId)
                .FirstOrDefaultAsync();

            result[school.SchoolId] = lastEventId;
        }

        return result;
    }
}
