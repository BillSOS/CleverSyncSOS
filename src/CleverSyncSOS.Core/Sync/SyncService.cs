// ---
// speckit:
//   type: implementation
//   source: SpecKit/Plans/001-clever-api-auth/plan.md
//   section: Stage 2 - Database Synchronization
//   constitution: SpecKit/Constitution/constitution.md
//   phase: Database Sync
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync;

/// <summary>
/// Synchronization service that orchestrates data sync from Clever API to databases.
/// Source: SpecKit/Plans/001-clever-api-auth/plan.md (Stage 2)
/// Implements dual-database architecture with SessionDb for orchestration and per-school databases for data.
/// </summary>
public class SyncService : ISyncService
{
    private readonly ICleverApiClient _cleverClient;
    private readonly SessionDbContext _sessionDb;
    private readonly SchoolDatabaseConnectionFactory _schoolDbFactory;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        ICleverApiClient cleverClient,
        SessionDbContext sessionDb,
        SchoolDatabaseConnectionFactory schoolDbFactory,
        ILogger<SyncService> logger)
    {
        _cleverClient = cleverClient;
        _sessionDb = sessionDb;
        _schoolDbFactory = schoolDbFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SyncSummary> SyncAllDistrictsAsync(bool forceFullSync = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sync for all districts (forceFullSync: {ForceFullSync})", forceFullSync);

        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Step 1: Query all districts from SessionDb
            var districts = await _sessionDb.Districts
                .Include(d => d.Schools)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} districts to sync", districts.Count);

            // Step 2: Sync each district
            foreach (var district in districts)
            {
                try
                {
                    var districtResult = await SyncDistrictAsync(district.DistrictId, forceFullSync, cancellationToken);
                    summary.TotalSchools += districtResult.TotalSchools;
                    summary.SuccessfulSchools += districtResult.SuccessfulSchools;
                    summary.FailedSchools += districtResult.FailedSchools;
                    summary.TotalRecordsProcessed += districtResult.TotalRecordsProcessed;
                    summary.TotalRecordsFailed += districtResult.TotalRecordsFailed;
                    summary.SchoolResults.AddRange(districtResult.SchoolResults);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync district {DistrictId} ({DistrictName})",
                        district.DistrictId, district.Name);
                }
            }

            summary.EndTime = DateTime.UtcNow;
            _logger.LogInformation(
                "Completed sync for all districts: {SuccessfulSchools}/{TotalSchools} schools successful, {TotalRecords} records processed in {Duration}",
                summary.SuccessfulSchools, summary.TotalSchools, summary.TotalRecordsProcessed, summary.Duration);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during sync of all districts");
            summary.EndTime = DateTime.UtcNow;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SyncSummary> SyncDistrictAsync(int districtId, bool forceFullSync = false, CancellationToken cancellationToken = default)
    {
        var district = await _sessionDb.Districts
            .Include(d => d.Schools)
            .FirstOrDefaultAsync(d => d.DistrictId == districtId, cancellationToken);

        if (district == null)
        {
            throw new InvalidOperationException($"District {districtId} not found in SessionDb");
        }

        _logger.LogInformation("Starting sync for district {DistrictId} ({DistrictName}) with {SchoolCount} schools",
            district.DistrictId, district.Name, district.Schools.Count);

        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow,
            TotalSchools = district.Schools.Count(s => s.IsActive)
        };

        // Step 3: Sync active schools in parallel (max 5 concurrent)
        var activeSchools = district.Schools.Where(s => s.IsActive).ToList();

        var semaphore = new SemaphoreSlim(5, 5); // Max 5 concurrent school syncs
        var syncTasks = activeSchools.Select(async school =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await SyncSchoolAsync(school.SchoolId, forceFullSync, cancellationToken);
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(syncTasks);

        summary.SchoolResults.AddRange(results);
        summary.SuccessfulSchools = results.Count(r => r.Success);
        summary.FailedSchools = results.Count(r => !r.Success);
        summary.TotalRecordsProcessed = results.Sum(r => r.StudentsProcessed + r.TeachersProcessed);
        summary.TotalRecordsFailed = results.Sum(r => r.StudentsFailed + r.TeachersFailed);
        summary.EndTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Completed sync for district {DistrictId}: {SuccessfulSchools}/{TotalSchools} schools successful",
            districtId, summary.SuccessfulSchools, summary.TotalSchools);

        return summary;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncSchoolAsync(int schoolId, bool forceFullSync = false, CancellationToken cancellationToken = default)
    {
        var result = new SyncResult
        {
            SchoolId = schoolId,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Step 4a: Read school from SessionDb
            var school = await _sessionDb.Schools
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

            if (school == null)
            {
                throw new InvalidOperationException($"School {schoolId} not found in SessionDb");
            }

            result.SchoolName = school.Name;

            _logger.LogInformation("Starting sync for school {SchoolId} ({SchoolName})", schoolId, school.Name);

            // Step 4b: Connect to school's dedicated database
            await using var schoolDb = await _schoolDbFactory.CreateSchoolContextAsync(school);

            // Step 4d: Determine sync type (Full or Incremental)
            var lastSync = await _sessionDb.SyncHistory
                .Where(h => h.SchoolId == schoolId && h.Status == "Success")
                .OrderByDescending(h => h.SyncEndTime)
                .FirstOrDefaultAsync(cancellationToken);

            bool isFullSync = forceFullSync || school.RequiresFullSync || lastSync == null;
            result.SyncType = isFullSync ? SyncType.Full : SyncType.Incremental;

            _logger.LogInformation("Sync type for school {SchoolId}: {SyncType}", schoolId, result.SyncType);

            // Step 4e-4g: Sync students and teachers
            if (isFullSync)
            {
                await PerformFullSyncAsync(school, schoolDb, result, cancellationToken);
            }
            else
            {
                await PerformIncrementalSyncAsync(school, schoolDb, result, lastSync!.LastSyncTimestamp, cancellationToken);
            }

            // Reset RequiresFullSync flag after successful full sync
            if (isFullSync && school.RequiresFullSync)
            {
                school.RequiresFullSync = false;
                school.UpdatedAt = DateTime.UtcNow;
                await _sessionDb.SaveChangesAsync(cancellationToken);
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation(
                "Completed sync for school {SchoolId}: {StudentsProcessed} students, {TeachersProcessed} teachers processed in {Duration}",
                schoolId, result.StudentsProcessed, result.TeachersProcessed, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync school {SchoolId}", schoolId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            return result;
        }
    }

    /// <summary>
    /// Performs a full sync with hard-delete for inactive records.
    /// Source: FR-025 - Beginning-of-year sync with hard-delete behavior
    /// </summary>
    private async Task PerformFullSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing FULL sync for school {SchoolId}", school.SchoolId);

        // Step 1: Mark all existing students and teachers as inactive
        var allStudents = await schoolDb.Students.ToListAsync(cancellationToken);
        var allTeachers = await schoolDb.Teachers.ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var student in allStudents)
        {
            student.IsActive = false;
            student.DeactivatedAt = now;
        }
        foreach (var teacher in allTeachers)
        {
            teacher.IsActive = false;
            teacher.DeactivatedAt = now;
        }
        await schoolDb.SaveChangesAsync(cancellationToken);

        // Step 2: Fetch all students and teachers from Clever API
        await SyncStudentsAsync(school, schoolDb, result, null, cancellationToken);
        await SyncTeachersAsync(school, schoolDb, result, null, cancellationToken);

        // Step 3: Hard-delete inactive students and teachers
        var inactiveStudents = await schoolDb.Students.Where(s => !s.IsActive).ToListAsync(cancellationToken);
        var inactiveTeachers = await schoolDb.Teachers.Where(t => !t.IsActive).ToListAsync(cancellationToken);

        result.StudentsDeleted = inactiveStudents.Count;
        result.TeachersDeleted = inactiveTeachers.Count;

        schoolDb.Students.RemoveRange(inactiveStudents);
        schoolDb.Teachers.RemoveRange(inactiveTeachers);
        await schoolDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Full sync complete for school {SchoolId}: Deleted {StudentsDeleted} students, {TeachersDeleted} teachers",
            school.SchoolId, result.StudentsDeleted, result.TeachersDeleted);
    }

    /// <summary>
    /// Performs an incremental sync using lastModified timestamp.
    /// Source: FR-024 - Incremental sync with lastModified filter
    /// </summary>
    private async Task PerformIncrementalSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing INCREMENTAL sync for school {SchoolId} (lastModified: {LastModified})",
            school.SchoolId, lastModified?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "null");

        // Fetch only modified students and teachers
        await SyncStudentsAsync(school, schoolDb, result, lastModified, cancellationToken);
        await SyncTeachersAsync(school, schoolDb, result, lastModified, cancellationToken);
    }

    /// <summary>
    /// Syncs students from Clever API to school database.
    /// </summary>
    private async Task SyncStudentsAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        CancellationToken cancellationToken)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Student",
            SyncType = result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = lastModified
        };

        try
        {
            // Fetch students from Clever API
            var cleverStudents = await _cleverClient.GetStudentsAsync(school.CleverSchoolId, lastModified, cancellationToken);

            _logger.LogDebug("Fetched {Count} students from Clever API for school {SchoolId}",
                cleverStudents.Length, school.SchoolId);

            // Upsert students
            foreach (var cleverStudent in cleverStudents)
            {
                try
                {
                    await UpsertStudentAsync(schoolDb, cleverStudent, cancellationToken);
                    result.StudentsProcessed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert student {CleverStudentId} for school {SchoolId}",
                        cleverStudent.Id, school.SchoolId);
                    result.StudentsFailed++;
                }
            }

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = result.StudentsProcessed;
            syncHistory.RecordsFailed = result.StudentsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync students for school {SchoolId}", school.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            _sessionDb.SyncHistory.Add(syncHistory);
            await _sessionDb.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Syncs teachers from Clever API to school database.
    /// </summary>
    private async Task SyncTeachersAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        CancellationToken cancellationToken)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Teacher",
            SyncType = result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = lastModified
        };

        try
        {
            // Fetch teachers from Clever API
            var cleverTeachers = await _cleverClient.GetTeachersAsync(school.CleverSchoolId, lastModified, cancellationToken);

            _logger.LogDebug("Fetched {Count} teachers from Clever API for school {SchoolId}",
                cleverTeachers.Length, school.SchoolId);

            // Upsert teachers
            foreach (var cleverTeacher in cleverTeachers)
            {
                try
                {
                    await UpsertTeacherAsync(schoolDb, cleverTeacher, cancellationToken);
                    result.TeachersProcessed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert teacher {CleverTeacherId} for school {SchoolId}",
                        cleverTeacher.Id, school.SchoolId);
                    result.TeachersFailed++;
                }
            }

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = result.TeachersProcessed;
            syncHistory.RecordsFailed = result.TeachersFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync teachers for school {SchoolId}", school.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            _sessionDb.SyncHistory.Add(syncHistory);
            await _sessionDb.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Upserts a student record (insert if new, update if exists).
    /// Source: FR-014 - Upsert logic for students
    /// </summary>
    private async Task UpsertStudentAsync(
        SchoolDbContext schoolDb,
        CleverStudent cleverStudent,
        CancellationToken cancellationToken)
    {
        var student = await schoolDb.Students
            .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudent.Id, cancellationToken);

        var now = DateTime.UtcNow;

        if (student == null)
        {
            // Insert new student
            student = new Student
            {
                CleverStudentId = cleverStudent.Id,
                FirstName = cleverStudent.Name.First,
                LastName = cleverStudent.Name.Last,
                Email = cleverStudent.Email,
                Grade = cleverStudent.Grade,
                StudentNumber = cleverStudent.StudentNumber,
                LastModifiedInClever = cleverStudent.LastModified,
                IsActive = true,
                DeactivatedAt = null,
                CreatedAt = now,
                UpdatedAt = now
            };
            schoolDb.Students.Add(student);
        }
        else
        {
            // Update existing student
            student.FirstName = cleverStudent.Name.First;
            student.LastName = cleverStudent.Name.Last;
            student.Email = cleverStudent.Email;
            student.Grade = cleverStudent.Grade;
            student.StudentNumber = cleverStudent.StudentNumber;
            student.LastModifiedInClever = cleverStudent.LastModified;
            student.IsActive = true; // Reactivate if it was marked inactive
            student.DeactivatedAt = null;
            student.UpdatedAt = now;
        }

        await schoolDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Upserts a teacher record (insert if new, update if exists).
    /// Source: FR-014 - Upsert logic for teachers
    /// </summary>
    private async Task UpsertTeacherAsync(
        SchoolDbContext schoolDb,
        CleverTeacher cleverTeacher,
        CancellationToken cancellationToken)
    {
        var teacher = await schoolDb.Teachers
            .FirstOrDefaultAsync(t => t.CleverTeacherId == cleverTeacher.Id, cancellationToken);

        var now = DateTime.UtcNow;

        if (teacher == null)
        {
            // Insert new teacher
            teacher = new Teacher
            {
                CleverTeacherId = cleverTeacher.Id,
                FirstName = cleverTeacher.Name.First,
                LastName = cleverTeacher.Name.Last,
                Email = cleverTeacher.Email,
                Title = cleverTeacher.Title,
                LastModifiedInClever = cleverTeacher.LastModified,
                IsActive = true,
                DeactivatedAt = null,
                CreatedAt = now,
                UpdatedAt = now
            };
            schoolDb.Teachers.Add(teacher);
        }
        else
        {
            // Update existing teacher
            teacher.FirstName = cleverTeacher.Name.First;
            teacher.LastName = cleverTeacher.Name.Last;
            teacher.Email = cleverTeacher.Email;
            teacher.Title = cleverTeacher.Title;
            teacher.LastModifiedInClever = cleverTeacher.LastModified;
            teacher.IsActive = true; // Reactivate if it was marked inactive
            teacher.DeactivatedAt = null;
            teacher.UpdatedAt = now;
        }

        await schoolDb.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SyncHistory[]> GetSyncHistoryAsync(
        int schoolId,
        string? entityType = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var query = _sessionDb.SyncHistory
            .Where(h => h.SchoolId == schoolId);

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(h => h.EntityType == entityType);
        }

        var history = await query
            .OrderByDescending(h => h.SyncStartTime)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return history;
    }
}
