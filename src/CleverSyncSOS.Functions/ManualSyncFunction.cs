// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-020
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Azure Functions
//   version: 1.0.0
// ---

using System.Net;
using CleverSyncSOS.Core.Logging;
using CleverSyncSOS.Core.Sync;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Functions;

/// <summary>
/// HTTP-triggered Azure Function for manual synchronization.
/// Source: FR-020 - Sync Orchestration
/// Spec: SpecKit/Specs/001-clever-api-auth/spec-1.md
/// </summary>
public class ManualSyncFunction
{
    private readonly ISyncService _syncService;
    private readonly ILogger<ManualSyncFunction> _logger;

    public ManualSyncFunction(ISyncService syncService, ILogger<ManualSyncFunction> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// HTTP-triggered function for manual sync.
    /// Source: FR-020 - Support manual trigger via HTTP endpoint
    ///
    /// Endpoints:
    /// - POST /api/sync - Sync all districts
    /// - POST /api/sync?districtId={id} - Sync specific district
    /// - POST /api/sync?schoolId={id} - Sync specific school
    /// - POST /api/sync?schoolId={id}&forceFullSync=true - Force full sync for school
    /// </summary>
    [Function("ManualSync")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sync")] HttpRequestData req,
        FunctionContext context)
    {
        _logger.LogInformation("Manual Sync Function triggered");

        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var schoolIdStr = query["schoolId"];
            var districtIdStr = query["districtId"];
            var forceFullSyncStr = query["forceFullSync"];

            bool forceFullSync = bool.TryParse(forceFullSyncStr, out var parsedFullSync) && parsedFullSync;

            var startTime = DateTime.UtcNow;

            // FR-020: Support school-level, district-level, or full sync
            if (!string.IsNullOrEmpty(schoolIdStr) && int.TryParse(schoolIdStr, out var schoolId))
            {
                // School-level sync
                _logger.LogInformation(
                    "Starting manual sync for school {SchoolId} (Full sync: {ForceFullSync})",
                    schoolId, forceFullSync);

                var result = await _syncService.SyncSchoolAsync(schoolId, forceFullSync);

                return await CreateSchoolSyncResponse(req, result, startTime);
            }
            else if (!string.IsNullOrEmpty(districtIdStr) && int.TryParse(districtIdStr, out var districtId))
            {
                // District-level sync
                _logger.LogInformation("Starting manual sync for district {DistrictId}", districtId);

                var result = await _syncService.SyncDistrictAsync(districtId, forceFullSync);

                return await CreateSummarySyncResponse(req, result, startTime, "district", districtId);
            }
            else
            {
                // Full sync (all districts)
                _logger.LogInformation("Starting manual full sync (all districts)");

                var result = await _syncService.SyncAllDistrictsAsync(forceFullSync);

                return await CreateSummarySyncResponse(req, result, startTime, "all", null);
            }
        }
        catch (Exception ex)
        {
            // FR-010: Sanitize exception before logging and returning to client
            _logger.LogSanitizedError(ex, "Manual Sync Function encountered an error");

            var sanitizedError = SensitiveDataSanitizer.CreateSafeErrorSummary(ex);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                success = false,
                error = sanitizedError, // Sanitized error message
                timestamp = DateTime.UtcNow
            });
            return errorResponse;
        }
    }

    /// <summary>
    /// Creates an HTTP response with school sync results.
    /// </summary>
    private async Task<HttpResponseData> CreateSchoolSyncResponse(
        HttpRequestData req,
        SyncResult result,
        DateTime startTime)
    {
        var duration = DateTime.UtcNow - startTime;

        if (result.Success)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                scope = "school",
                schoolId = result.SchoolId,
                schoolName = result.SchoolName,
                syncType = result.SyncType.ToString(),
                duration = duration.TotalSeconds,
                statistics = new
                {
                    studentsProcessed = result.StudentsProcessed,
                    studentsFailed = result.StudentsFailed,
                    studentsDeleted = result.StudentsDeleted,
                    teachersProcessed = result.TeachersProcessed,
                    teachersFailed = result.TeachersFailed,
                    teachersDeleted = result.TeachersDeleted
                },
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Manual sync completed successfully for school {SchoolId} in {Duration}s",
                result.SchoolId, duration.TotalSeconds);

            return response;
        }
        else
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                scope = "school",
                schoolId = result.SchoolId,
                error = result.ErrorMessage,
                duration = duration.TotalSeconds,
                timestamp = DateTime.UtcNow
            });

            _logger.LogWarning(
                "Manual sync failed for school {SchoolId}: {Error}",
                result.SchoolId, result.ErrorMessage);

            return response;
        }
    }

    /// <summary>
    /// Creates an HTTP response with district/all sync summary results.
    /// </summary>
    private async Task<HttpResponseData> CreateSummarySyncResponse(
        HttpRequestData req,
        SyncSummary result,
        DateTime startTime,
        string scope,
        int? entityId)
    {
        var duration = DateTime.UtcNow - startTime;
        bool hasFailures = result.FailedSchools > 0;

        var response = req.CreateResponse(hasFailures ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            success = result.FailedSchools == 0,
            scope,
            entityId,
            duration = duration.TotalSeconds,
            summary = new
            {
                totalSchools = result.TotalSchools,
                successfulSchools = result.SuccessfulSchools,
                failedSchools = result.FailedSchools,
                totalRecordsProcessed = result.TotalRecordsProcessed,
                totalRecordsFailed = result.TotalRecordsFailed
            },
            schools = result.SchoolResults.Select(s => new
            {
                schoolId = s.SchoolId,
                schoolName = s.SchoolName,
                success = s.Success,
                syncType = s.SyncType.ToString(),
                studentsProcessed = s.StudentsProcessed,
                teachersProcessed = s.TeachersProcessed,
                error = s.ErrorMessage
            }),
            timestamp = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Manual sync completed for {Scope} {EntityId}: {SuccessfulSchools}/{TotalSchools} schools succeeded in {Duration}s",
            scope, entityId, result.SuccessfulSchools, result.TotalSchools, duration.TotalSeconds);

        return response;
    }
}
