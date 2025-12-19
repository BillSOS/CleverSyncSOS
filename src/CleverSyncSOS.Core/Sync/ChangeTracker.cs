using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CleverSyncSOS.Core.Sync;

/// <summary>
/// Tracks detailed changes during sync operations for auditing and transparency.
/// Records which fields changed, along with old and new values.
/// </summary>
public class ChangeTracker
{
    private readonly SessionDbContext _sessionDb;
    private readonly ILogger _logger;
    private readonly List<SyncChangeDetail> _pendingChanges = new();

    public ChangeTracker(SessionDbContext sessionDb, ILogger logger)
    {
        _sessionDb = sessionDb;
        _logger = logger;
    }

    /// <summary>
    /// Tracks changes for a student record.
    /// </summary>
    public void TrackStudentChange(int syncId, Student? existingStudent, Student newStudent, string changeType)
    {
        try
        {
            var changes = new Dictionary<string, (string? OldValue, string? NewValue)>();

            if (changeType == "Created")
            {
                // For new records, just note which fields were set
                changes["FirstName"] = (null, newStudent.FirstName);
                changes["LastName"] = (null, newStudent.LastName);

                if (newStudent.Grade.HasValue)
                    changes["Grade"] = (null, newStudent.Grade.Value.ToString());
                if (!string.IsNullOrEmpty(newStudent.StudentNumber))
                    changes["StudentNumber"] = (null, newStudent.StudentNumber);
            }
            else if (changeType == "Updated" && existingStudent != null)
            {
                // Compare fields and record what changed
                if (!StringsEqual(existingStudent.FirstName, newStudent.FirstName))
                    changes["FirstName"] = (existingStudent.FirstName, newStudent.FirstName);

                if (!StringsEqual(existingStudent.LastName, newStudent.LastName))
                    changes["LastName"] = (existingStudent.LastName, newStudent.LastName);

                if (existingStudent.Grade != newStudent.Grade)
                    changes["Grade"] = (existingStudent.Grade?.ToString(), newStudent.Grade?.ToString());

                if (!StringsEqual(existingStudent.StudentNumber, newStudent.StudentNumber))
                    changes["StudentNumber"] = (existingStudent.StudentNumber, newStudent.StudentNumber);

                if (existingStudent.UpdatedAt != newStudent.UpdatedAt)
                    changes["UpdatedAt"] = (existingStudent.UpdatedAt.ToString("O"), newStudent.UpdatedAt.ToString("O"));
            }

            // Only record if there are actual changes
            if (changes.Any())
            {
                var changeDetail = new SyncChangeDetail
                {
                    SyncId = syncId,
                    EntityType = "Student",
                    EntityId = newStudent.CleverStudentId,
                    EntityName = $"{newStudent.FirstName} {newStudent.LastName}",
                    ChangeType = changeType,
                    FieldsChanged = string.Join(", ", changes.Keys),
                    OldValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.OldValue)),
                    NewValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.NewValue)),
                    ChangedAt = DateTime.UtcNow
                };

                _pendingChanges.Add(changeDetail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track change for student {StudentId}", newStudent.CleverStudentId);
        }
    }

    /// <summary>
    /// Tracks changes for a teacher record.
    /// </summary>
    public void TrackTeacherChange(int syncId, Teacher? existingTeacher, Teacher newTeacher, string changeType)
    {
        try
        {
            var changes = new Dictionary<string, (string? OldValue, string? NewValue)>();

            if (changeType == "Created")
            {
                // For new records, just note which fields were set
                changes["FirstName"] = (null, newTeacher.FirstName);
                changes["LastName"] = (null, newTeacher.LastName);
                if (!string.IsNullOrEmpty(newTeacher.FullName))
                    changes["FullName"] = (null, newTeacher.FullName);
                if (!string.IsNullOrEmpty(newTeacher.StaffNumber))
                    changes["StaffNumber"] = (null, newTeacher.StaffNumber);
                if (!string.IsNullOrEmpty(newTeacher.TeacherNumber))
                    changes["TeacherNumber"] = (null, newTeacher.TeacherNumber);
                if (!string.IsNullOrEmpty(newTeacher.UserName))
                    changes["UserName"] = (null, newTeacher.UserName);
            }
            else if (changeType == "Updated" && existingTeacher != null)
            {
                // Compare fields and record what changed
                if (!StringsEqual(existingTeacher.FirstName, newTeacher.FirstName))
                    changes["FirstName"] = (existingTeacher.FirstName, newTeacher.FirstName);

                if (!StringsEqual(existingTeacher.LastName, newTeacher.LastName))
                    changes["LastName"] = (existingTeacher.LastName, newTeacher.LastName);

                if (!StringsEqual(existingTeacher.FullName, newTeacher.FullName))
                    changes["FullName"] = (existingTeacher.FullName, newTeacher.FullName);

                if (!StringsEqual(existingTeacher.StaffNumber, newTeacher.StaffNumber))
                    changes["StaffNumber"] = (existingTeacher.StaffNumber, newTeacher.StaffNumber);

                if (!StringsEqual(existingTeacher.TeacherNumber, newTeacher.TeacherNumber))
                    changes["TeacherNumber"] = (existingTeacher.TeacherNumber, newTeacher.TeacherNumber);

                if (!StringsEqual(existingTeacher.UserName, newTeacher.UserName))
                    changes["UserName"] = (existingTeacher.UserName, newTeacher.UserName);

                if (existingTeacher.UpdatedAt != newTeacher.UpdatedAt)
                    changes["UpdatedAt"] = (existingTeacher.UpdatedAt.ToString("O"), newTeacher.UpdatedAt.ToString("O"));
            }

            // Only record if there are actual changes
            if (changes.Any())
            {
                var changeDetail = new SyncChangeDetail
                {
                    SyncId = syncId,
                    EntityType = "Teacher",
                    EntityId = newTeacher.CleverTeacherId ?? string.Empty,
                    EntityName = $"{newTeacher.FirstName} {newTeacher.LastName}",
                    ChangeType = changeType,
                    FieldsChanged = string.Join(", ", changes.Keys),
                    OldValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.OldValue)),
                    NewValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.NewValue)),
                    ChangedAt = DateTime.UtcNow
                };

                _pendingChanges.Add(changeDetail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track change for teacher {TeacherId}", newTeacher.CleverTeacherId);
        }
    }

    /// <summary>
    /// Tracks changes for a course record.
    /// </summary>
    public void TrackCourseChange(int syncId, Course? existingCourse, Course newCourse, string changeType)
    {
        try
        {
            var changes = new Dictionary<string, (string? OldValue, string? NewValue)>();

            if (changeType == "Created")
            {
                changes["Name"] = (null, newCourse.Name);
                if (!string.IsNullOrEmpty(newCourse.Number))
                    changes["Number"] = (null, newCourse.Number);
                if (!string.IsNullOrEmpty(newCourse.Subject))
                    changes["Subject"] = (null, newCourse.Subject);
                if (!string.IsNullOrEmpty(newCourse.GradeLevels))
                    changes["GradeLevels"] = (null, newCourse.GradeLevels);
            }
            else if (changeType == "Updated" && existingCourse != null)
            {
                if (!StringsEqual(existingCourse.Name, newCourse.Name))
                    changes["Name"] = (existingCourse.Name, newCourse.Name);

                if (!StringsEqual(existingCourse.Number, newCourse.Number))
                    changes["Number"] = (existingCourse.Number, newCourse.Number);

                if (!StringsEqual(existingCourse.Subject, newCourse.Subject))
                    changes["Subject"] = (existingCourse.Subject, newCourse.Subject);

                if (!StringsEqual(existingCourse.GradeLevels, newCourse.GradeLevels))
                    changes["GradeLevels"] = (existingCourse.GradeLevels, newCourse.GradeLevels);

                if (existingCourse.LastModifiedInClever != newCourse.LastModifiedInClever)
                    changes["LastModifiedInClever"] = (existingCourse.LastModifiedInClever?.ToString("O"), newCourse.LastModifiedInClever?.ToString("O"));
            }

            if (changes.Any())
            {
                var changeDetail = new SyncChangeDetail
                {
                    SyncId = syncId,
                    EntityType = "Course",
                    EntityId = newCourse.CleverCourseId,
                    EntityName = newCourse.Name,
                    ChangeType = changeType,
                    FieldsChanged = string.Join(", ", changes.Keys),
                    OldValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.OldValue)),
                    NewValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.NewValue)),
                    ChangedAt = DateTime.UtcNow
                };

                _pendingChanges.Add(changeDetail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track change for course {CourseId}", newCourse.CleverCourseId);
        }
    }

    /// <summary>
    /// Tracks changes for a section record.
    /// Note: CourseId FK no longer exists - courses are not synced. CleverCourseId is stored for reference only.
    /// </summary>
    public void TrackSectionChange(int syncId, Section? existingSection, Section newSection, string changeType)
    {
        try
        {
            var changes = new Dictionary<string, (string? OldValue, string? NewValue)>();

            if (changeType == "Created")
            {
                if (!string.IsNullOrEmpty(newSection.SectionNumber))
                    changes["SectionNumber"] = (null, newSection.SectionNumber);
                if (!string.IsNullOrEmpty(newSection.SectionName))
                    changes["SectionName"] = (null, newSection.SectionName);
                if (!string.IsNullOrEmpty(newSection.Period))
                    changes["Period"] = (null, newSection.Period);
                if (!string.IsNullOrEmpty(newSection.Subject))
                    changes["Subject"] = (null, newSection.Subject);
                if (!string.IsNullOrEmpty(newSection.CleverCourseId))
                    changes["CleverCourseId"] = (null, newSection.CleverCourseId);
            }
            else if (changeType == "Updated" && existingSection != null)
            {
                if (!StringsEqual(existingSection.SectionNumber, newSection.SectionNumber))
                    changes["SectionNumber"] = (existingSection.SectionNumber, newSection.SectionNumber);

                if (!StringsEqual(existingSection.SectionName, newSection.SectionName))
                    changes["SectionName"] = (existingSection.SectionName, newSection.SectionName);

                if (!StringsEqual(existingSection.Period, newSection.Period))
                    changes["Period"] = (existingSection.Period, newSection.Period);

                if (!StringsEqual(existingSection.Subject, newSection.Subject))
                    changes["Subject"] = (existingSection.Subject, newSection.Subject);

                if (!StringsEqual(existingSection.CleverCourseId, newSection.CleverCourseId))
                    changes["CleverCourseId"] = (existingSection.CleverCourseId, newSection.CleverCourseId);

                if (existingSection.UpdatedAt != newSection.UpdatedAt)
                    changes["UpdatedAt"] = (existingSection.UpdatedAt.ToString("O"), newSection.UpdatedAt.ToString("O"));
            }

            if (changes.Any())
            {
                var changeDetail = new SyncChangeDetail
                {
                    SyncId = syncId,
                    EntityType = "Section",
                    EntityId = newSection.CleverSectionId,
                    EntityName = newSection.SectionName ?? $"Section {newSection.SectionNumber}",
                    ChangeType = changeType,
                    FieldsChanged = string.Join(", ", changes.Keys),
                    OldValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.OldValue)),
                    NewValues = SerializeValues(changes.ToDictionary(c => c.Key, c => c.Value.NewValue)),
                    ChangedAt = DateTime.UtcNow
                };

                _pendingChanges.Add(changeDetail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track change for section {SectionId}", newSection.CleverSectionId);
        }
    }

    /// <summary>
    /// Saves all pending changes to the database.
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!_pendingChanges.Any())
            return 0;

        try
        {
            _sessionDb.SyncChangeDetails.AddRange(_pendingChanges);
            await _sessionDb.SaveChangesAsync(cancellationToken);

            int count = _pendingChanges.Count;
            _pendingChanges.Clear();

            _logger.LogInformation("Saved {Count} change detail records", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save change details");
            _pendingChanges.Clear();
            return 0;
        }
    }

    /// <summary>
    /// Gets the count of pending changes.
    /// </summary>
    public int GetPendingChangeCount() => _pendingChanges.Count;

    private static bool StringsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return true;

        return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? SerializeValues(Dictionary<string, string?> values)
    {
        try
        {
            return JsonSerializer.Serialize(values, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch
        {
            return null;
        }
    }
}
