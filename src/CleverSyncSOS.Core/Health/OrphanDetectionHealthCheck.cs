using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Health;

/// <summary>
/// Health check that detects orphaned entities in school databases.
/// Orphans are defined as:
/// 1. Entities that exist locally but weren't included in recent syncs (stale data)
/// 2. Students or teachers not associated with any section
/// </summary>
public class OrphanDetectionHealthCheck : IHealthCheck
{
    private readonly SessionDbContext _sessionDb;
    private readonly SchoolDatabaseConnectionFactory _schoolDbFactory;
    private readonly ILogger<OrphanDetectionHealthCheck> _logger;

    // Cache results for 5 minutes since this check queries multiple databases
    private static OrphanDetectionResult? _cachedResult;
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    // Threshold for considering an entity "stale" (not synced recently)
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(7);

    public OrphanDetectionHealthCheck(
        SessionDbContext sessionDb,
        SchoolDatabaseConnectionFactory schoolDbFactory,
        ILogger<OrphanDetectionHealthCheck> logger)
    {
        _sessionDb = sessionDb;
        _schoolDbFactory = schoolDbFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Return cached result if still valid
            if (_cachedResult != null && DateTime.UtcNow - _lastCheckTime < CacheDuration)
            {
                return BuildHealthResult(_cachedResult);
            }

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check after acquiring lock
                if (_cachedResult != null && DateTime.UtcNow - _lastCheckTime < CacheDuration)
                {
                    return BuildHealthResult(_cachedResult);
                }

                // Perform the orphan detection
                var result = await DetectOrphansAsync(cancellationToken);

                // Update cache
                _cachedResult = result;
                _lastCheckTime = DateTime.UtcNow;

                return BuildHealthResult(result);
            }
            finally
            {
                _cacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orphan detection health check failed");
            return HealthCheckResult.Unhealthy(
                "Orphan detection failed",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }

    private async Task<OrphanDetectionResult> DetectOrphansAsync(CancellationToken cancellationToken)
    {
        var result = new OrphanDetectionResult();
        var staleDate = DateTime.UtcNow - StaleThreshold;

        // Get all active schools
        var schools = await _sessionDb.Schools
            .Where(s => s.IsActive && !string.IsNullOrEmpty(s.KeyVaultSchoolPrefix))
            .ToListAsync(cancellationToken);

        foreach (var school in schools)
        {
            try
            {
                using var schoolDb = await _schoolDbFactory.CreateSchoolContextAsync(school);
                var schoolOrphans = await DetectSchoolOrphansAsync(schoolDb, school, staleDate, cancellationToken);
                result.SchoolResults.Add(schoolOrphans);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check orphans for school {SchoolName}", school.Name);
                result.SchoolResults.Add(new SchoolOrphanResult
                {
                    SchoolId = school.SchoolId,
                    SchoolName = school.Name,
                    Error = ex.Message
                });
            }
        }

        return result;
    }

    private async Task<SchoolOrphanResult> DetectSchoolOrphansAsync(
        SchoolDbContext schoolDb,
        School school,
        DateTime staleDate,
        CancellationToken cancellationToken)
    {
        var result = new SchoolOrphanResult
        {
            SchoolId = school.SchoolId,
            SchoolName = school.Name
        };

        // Category 1: Stale entities (not synced recently - may no longer exist in Clever)
        result.StaleStudentCount = await schoolDb.Students
            .Where(s => s.DeletedAt == null && s.LastSyncedAt < staleDate)
            .CountAsync(cancellationToken);

        result.StaleTeacherCount = await schoolDb.Teachers
            .Where(t => t.DeletedAt == null && t.LastSyncedAt < staleDate)
            .CountAsync(cancellationToken);

        result.StaleSectionCount = await schoolDb.Sections
            .Where(s => s.DeletedAt == null && s.LastSyncedAt < staleDate)
            .CountAsync(cancellationToken);

        // Category 2: Students without section enrollments
        var studentsWithSections = schoolDb.StudentSections
            .Select(ss => ss.StudentId)
            .Distinct();

        result.StudentsWithoutSections = await schoolDb.Students
            .Where(s => s.DeletedAt == null && !studentsWithSections.Contains(s.StudentId))
            .CountAsync(cancellationToken);

        // Category 2: Teachers without section assignments
        var teachersWithSections = schoolDb.TeacherSections
            .Select(ts => ts.TeacherId)
            .Distinct();

        result.TeachersWithoutSections = await schoolDb.Teachers
            .Where(t => t.DeletedAt == null && !teachersWithSections.Contains(t.TeacherId))
            .CountAsync(cancellationToken);

        // Get total counts for context
        result.TotalStudents = await schoolDb.Students
            .Where(s => s.DeletedAt == null)
            .CountAsync(cancellationToken);

        result.TotalTeachers = await schoolDb.Teachers
            .Where(t => t.DeletedAt == null)
            .CountAsync(cancellationToken);

        result.TotalSections = await schoolDb.Sections
            .Where(s => s.DeletedAt == null)
            .CountAsync(cancellationToken);

        return result;
    }

    private HealthCheckResult BuildHealthResult(OrphanDetectionResult result)
    {
        var data = new Dictionary<string, object>
        {
            { "schools_checked", result.SchoolResults.Count },
            { "check_time_utc", _lastCheckTime },
            { "stale_threshold_days", StaleThreshold.TotalDays }
        };

        // Aggregate totals
        var totalStaleStudents = result.SchoolResults.Sum(r => r.StaleStudentCount);
        var totalStaleTeachers = result.SchoolResults.Sum(r => r.StaleTeacherCount);
        var totalStaleSections = result.SchoolResults.Sum(r => r.StaleSectionCount);
        var totalStudentsWithoutSections = result.SchoolResults.Sum(r => r.StudentsWithoutSections);
        var totalTeachersWithoutSections = result.SchoolResults.Sum(r => r.TeachersWithoutSections);

        data["stale_students"] = totalStaleStudents;
        data["stale_teachers"] = totalStaleTeachers;
        data["stale_sections"] = totalStaleSections;
        data["students_without_sections"] = totalStudentsWithoutSections;
        data["teachers_without_sections"] = totalTeachersWithoutSections;

        // Add per-school breakdown
        var schoolDetails = result.SchoolResults
            .Where(r => r.HasOrphans || r.Error != null)
            .Select(r => new Dictionary<string, object>
            {
                { "school_id", r.SchoolId },
                { "school_name", r.SchoolName },
                { "stale_students", r.StaleStudentCount },
                { "stale_teachers", r.StaleTeacherCount },
                { "stale_sections", r.StaleSectionCount },
                { "students_without_sections", r.StudentsWithoutSections },
                { "teachers_without_sections", r.TeachersWithoutSections },
                { "total_students", r.TotalStudents },
                { "total_teachers", r.TotalTeachers },
                { "total_sections", r.TotalSections },
                { "error", r.Error ?? string.Empty }
            })
            .ToList();

        if (schoolDetails.Any())
        {
            data["schools_with_orphans"] = schoolDetails;
        }

        // Determine health status
        HealthStatus status;
        string description;

        var hasErrors = result.SchoolResults.Any(r => r.Error != null);
        var hasStaleEntities = totalStaleStudents > 0 || totalStaleTeachers > 0 || totalStaleSections > 0;
        var hasUnassociatedEntities = totalStudentsWithoutSections > 0 || totalTeachersWithoutSections > 0;

        if (hasErrors)
        {
            status = HealthStatus.Degraded;
            description = "Some schools could not be checked for orphans";
        }
        else if (hasStaleEntities)
        {
            status = HealthStatus.Degraded;
            description = $"Found {totalStaleStudents} stale students, {totalStaleTeachers} stale teachers, {totalStaleSections} stale sections (not synced in {StaleThreshold.TotalDays} days)";
        }
        else if (hasUnassociatedEntities)
        {
            // Unassociated entities are less critical - informational
            status = HealthStatus.Healthy;
            description = $"All entities are current. Note: {totalStudentsWithoutSections} students and {totalTeachersWithoutSections} teachers have no section associations.";
        }
        else
        {
            status = HealthStatus.Healthy;
            description = "No orphaned entities detected";
        }

        data["status"] = status.ToString();
        data["description"] = description;

        return new HealthCheckResult(status, description, data: data);
    }

    /// <summary>
    /// Clears the cached health check result.
    /// </summary>
    public static void ClearCache()
    {
        _cachedResult = null;
        _lastCheckTime = DateTime.MinValue;
    }

    /// <summary>
    /// Result of orphan detection across all schools.
    /// </summary>
    private class OrphanDetectionResult
    {
        public List<SchoolOrphanResult> SchoolResults { get; set; } = new();
    }

    /// <summary>
    /// Result of orphan detection for a single school.
    /// </summary>
    private class SchoolOrphanResult
    {
        public int SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string? Error { get; set; }

        // Category 1: Stale entities (not synced recently)
        public int StaleStudentCount { get; set; }
        public int StaleTeacherCount { get; set; }
        public int StaleSectionCount { get; set; }

        // Category 2: Entities without associations
        public int StudentsWithoutSections { get; set; }
        public int TeachersWithoutSections { get; set; }

        // Totals for context
        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalSections { get; set; }

        public bool HasOrphans =>
            StaleStudentCount > 0 ||
            StaleTeacherCount > 0 ||
            StaleSectionCount > 0 ||
            StudentsWithoutSections > 0 ||
            TeachersWithoutSections > 0;
    }
}
