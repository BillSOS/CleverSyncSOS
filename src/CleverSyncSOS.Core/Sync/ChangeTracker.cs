using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CleverSyncSOS.Core.Sync;

/// <summary>
/// Tracks detailed field-level changes during sync operations for auditing and transparency.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The ChangeTracker provides a detailed audit trail of what changed during sync:</para>
/// <list type="bullet">
///   <item><description>Which records were created, updated, or deleted</description></item>
///   <item><description>Which specific fields changed on each record</description></item>
///   <item><description>Old and new values for each changed field</description></item>
///   <item><description>Timestamps for when changes occurred</description></item>
/// </list>
/// 
/// <para><b>Storage:</b> Changes are stored in the <see cref="SyncChangeDetail"/> table in SessionDb.</para>
/// 
/// <para><b>Usage Pattern:</b></para>
/// <code>
/// // Create tracker at start of sync
/// var changeTracker = new ChangeTracker(_sessionDb, _logger);
/// 
/// // Track changes as they happen
/// changeTracker.TrackStudentChange(syncId, existingStudent, newStudent, "Updated");
/// 
/// // Save all tracked changes at end of sync
/// await changeTracker.SaveChangesAsync(cancellationToken);
/// </code>
/// 
/// <para><b>Change Types:</b></para>
/// <list type="bullet">
///   <item><description><b>"Created"</b> - New record added (existingEntity is null)</description></item>
///   <item><description><b>"Updated"</b> - Existing record modified (both entities provided)</description></item>
///   <item><description><b>"Deleted"</b> - Record soft-deleted (handled separately, not via ChangeTracker)</description></item>
/// </list>
/// 
/// <para><b>Tracked Entities:</b></para>
/// <list type="bullet">
///   <item><description>Students: FirstName, LastName, Grade, StudentNumber</description></item>
///   <item><description>Teachers: FirstName, LastName, FullName, StaffNumber, TeacherNumber, UserName</description></item>
///   <item><description>Sections: SectionName, Period, Subject, TermId</description></item>
///   <item><description>Courses: Name, Number, Subject, GradeLevels</description></item>
/// </list>
/// 
/// <para><b>Batching:</b></para>
/// <para>Changes are accumulated in memory and saved in a single batch via <see cref="SaveChangesAsync"/>.
/// This improves performance by reducing database round-trips during sync.</para>
/// 
/// <para><b>User Manual Reference:</b></para>
/// <list type="bullet">
///   <item><description>Admin Portal ? School ? Sync History ? Click row ? Change Details tab</description></item>
///   <item><description>Shows list of all changes with old/new values</description></item>
/// </list>
/// </remarks>
/// <seealso cref="SyncChangeDetail"/>
/// <seealso cref="SyncHistory"/>
public class ChangeTracker
{
    private readonly SessionDbContext _sessionDb;
    private readonly ILogger _logger;
    private readonly List<SyncChangeDetail> _pendingChanges = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeTracker"/> class.
    /// </summary>
    /// <param name="sessionDb">SessionDb context for persisting change details</param>
    /// <param name="logger">Logger for diagnostic output</param>
    public ChangeTracker(SessionDbContext sessionDb, ILogger logger)
    {
        _sessionDb = sessionDb;
        _logger = logger;
    }

    /// <summary>
    /// Tracks changes for a student record.
    /// </summary>
    /// <remarks>
    /// <para><b>Tracked Fields:</b> FirstName, LastName, Grade, StudentNumber, UpdatedAt</para>
    /// <para><b>For Creates:</b> Records initial values for key fields.</para>
    /// <para><b>For Updates:</b> Only records fields that actually changed.</para>
    /// </remarks>
    /// <param name="syncId">The SyncId to associate changes with</param>
    /// <param name="existingStudent">Current state (null for creates)</param>
    /// <param name="newStudent">New state being applied</param>
    /// <param name="changeType">"Created" or "Updated"</param>
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
    /// <remarks>
    /// <para><b>Tracked Fields:</b> FirstName, LastName, FullName, StaffNumber, TeacherNumber, UserName, UpdatedAt</para>
    /// </remarks>
    /// <param name="syncId">The SyncId to associate changes with</param>
    /// <param name="existingTeacher">Current state (null for creates)</param>
    /// <param name="newTeacher">New state being applied</param>
    /// <param name="changeType">"Created" or "Updated"</param>
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
    /// <remarks>
    /// <para><b>Tracked Fields:</b> Name, Number, Subject, GradeLevels, LastEventReceivedAt</para>
    /// </remarks>
    /// <param name="syncId">The SyncId to associate changes with</param>
    /// <param name="existingCourse">Current state (null for creates)</param>
    /// <param name="newCourse">New state being applied</param>
    /// <param name="changeType">"Created" or "Updated"</param>
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

                if (existingCourse.LastEventReceivedAt != newCourse.LastEventReceivedAt)
                    changes["LastEventReceivedAt"] = (existingCourse.LastEventReceivedAt?.ToString("O"), newCourse.LastEventReceivedAt?.ToString("O"));
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
    /// Tracks changes for a term record.
    /// </summary>
    /// <remarks>
    /// <para><b>Tracked Fields:</b> Name, StartDate, EndDate, LastEventReceivedAt</para>
    /// </remarks>
    /// <param name="syncId">The SyncId to associate changes with</param>
    /// <param name="existingTerm">Current state (null for creates)</param>
    /// <param name="newTerm">New state being applied</param>
    /// <param name="changeType">"Created" or "Updated"</param>
    public void TrackTermChange(int syncId, Term? existingTerm, Term newTerm, string changeType)
    {
        try
        {
            var changes = new Dictionary<string, (string? OldValue, string? NewValue)>();

            if (changeType == "Created")
            {
                if (!string.IsNullOrEmpty(newTerm.Name))
                    changes["Name"] = (null, newTerm.Name);
                if (newTerm.StartDate.HasValue)
                    changes["StartDate"] = (null, newTerm.StartDate.Value.ToString("yyyy-MM-dd"));
                if (newTerm.EndDate.HasValue)
                    changes["EndDate"] = (null, newTerm.EndDate.Value.ToString("yyyy-MM-dd"));
            }
            else if (changeType == "Updated" && existingTerm != null)
            {
                if (!StringsEqual(existingTerm.Name, newTerm.Name))
                    changes["Name"] = (existingTerm.Name, newTerm.Name);

                if (existingTerm.StartDate != newTerm.StartDate)
                    changes["StartDate"] = (existingTerm.StartDate?.ToString("yyyy-MM-dd"), newTerm.StartDate?.ToString("yyyy-MM-dd"));

                if (existingTerm.EndDate != newTerm.EndDate)
                    changes["EndDate"] = (existingTerm.EndDate?.ToString("yyyy-MM-dd"), newTerm.EndDate?.ToString("yyyy-MM-dd"));

                if (existingTerm.LastEventReceivedAt != newTerm.LastEventReceivedAt)
                    changes["LastEventReceivedAt"] = (existingTerm.LastEventReceivedAt?.ToString("O"), newTerm.LastEventReceivedAt?.ToString("O"));
            }

            if (changes.Any())
            {
                var changeDetail = new SyncChangeDetail
                {
                    SyncId = syncId,
                    EntityType = "Term",
                    EntityId = newTerm.CleverTermId,
                    EntityName = newTerm.Name ?? $"Term {newTerm.CleverTermId}",
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
            _logger.LogWarning(ex, "Failed to track change for term {TermId}", newTerm.CleverTermId);
        }
    }

    /// <summary>
    /// Tracks changes for a section record.
    /// </summary>
    /// <remarks>
    /// <para><b>Tracked Fields:</b> SectionName, Period, Subject, TermId, UpdatedAt</para>
    /// </remarks>
    /// <param name="syncId">The SyncId to associate changes with</param>
    /// <param name="existingSection">Current state (null for creates)</param>
    /// <param name="newSection">New state being applied</param>
    /// <param name="changeType">"Created", "Updated", or "Deleted"</param>
    public void TrackSectionChange(int syncId, Section? existingSection, Section newSection, string changeType)
    {
        try
        {
            var changes = new Dictionary<string, (string? OldValue, string? NewValue)>();

            if (changeType == "Created")
            {
                if (!string.IsNullOrEmpty(newSection.SectionName))
                    changes["SectionName"] = (null, newSection.SectionName);
                if (!string.IsNullOrEmpty(newSection.Period))
                    changes["Period"] = (null, newSection.Period);
                if (!string.IsNullOrEmpty(newSection.Subject))
                    changes["Subject"] = (null, newSection.Subject);
                if (!string.IsNullOrEmpty(newSection.TermId))
                    changes["TermId"] = (null, newSection.TermId);
            }
            else if (changeType == "Updated" && existingSection != null)
            {
                if (!StringsEqual(existingSection.SectionName, newSection.SectionName))
                    changes["SectionName"] = (existingSection.SectionName, newSection.SectionName);

                if (!StringsEqual(existingSection.Period, newSection.Period))
                    changes["Period"] = (existingSection.Period, newSection.Period);

                if (!StringsEqual(existingSection.Subject, newSection.Subject))
                    changes["Subject"] = (existingSection.Subject, newSection.Subject);

                if (!StringsEqual(existingSection.TermId, newSection.TermId))
                    changes["TermId"] = (existingSection.TermId, newSection.TermId);

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
                    EntityName = newSection.SectionName ?? $"Section {newSection.CleverSectionId}",
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
    /// Saves all pending changes to the database in a single batch.
    /// </summary>
    /// <remarks>
    /// <para>Call this at the end of a sync operation to persist all tracked changes.</para>
    /// <para>Clears the pending changes list after successful save.</para>
    /// <para>Errors during save are logged but do not propagate (non-fatal to sync).</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of change records saved</returns>
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
    /// Gets the count of pending changes not yet saved to the database.
    /// </summary>
    /// <returns>Number of pending change records</returns>
    public int GetPendingChangeCount() => _pendingChanges.Count;

    /// <summary>
    /// Compares two strings treating null/whitespace as equivalent.
    /// </summary>
    private static bool StringsEqual(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
            return true;

        return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Serializes a dictionary of field values to JSON for storage.
    /// </summary>
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
