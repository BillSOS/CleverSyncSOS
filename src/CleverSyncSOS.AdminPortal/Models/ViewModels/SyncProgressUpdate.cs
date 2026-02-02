namespace CleverSyncSOS.AdminPortal.Models.ViewModels;

/// <summary>
/// SignalR message model for real-time sync progress updates.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SyncProgressUpdate
{
    public string SyncId { get; set; } = Guid.NewGuid().ToString();
    public string Scope { get; set; } = string.Empty; // "school:123", "district:45", "all"
    public int PercentComplete { get; set; } // 0-100
    public string CurrentOperation { get; set; } = string.Empty; // "Fetching students..."

    // Counters for full sync
    public int StudentsProcessed { get; set; } // Total students examined
    public int StudentsUpdated { get; set; } // Students that actually changed
    public int StudentsFailed { get; set; }
    public int TeachersProcessed { get; set; } // Total teachers examined
    public int TeachersUpdated { get; set; } // Teachers that actually changed
    public int TeachersFailed { get; set; }
    public int CoursesProcessed { get; set; } // Total courses examined
    public int CoursesUpdated { get; set; } // Courses that actually changed
    public int CoursesFailed { get; set; }
    public int SectionsProcessed { get; set; } // Total sections examined
    public int SectionsUpdated { get; set; } // Sections that actually changed
    public int SectionsFailed { get; set; }

    // Counters for incremental sync (Created/Updated/Deleted breakdown)
    public int StudentsCreated { get; set; }
    public int StudentsDeleted { get; set; }
    public int TeachersCreated { get; set; }
    public int TeachersDeleted { get; set; }
    public int SectionsCreated { get; set; }
    public int SectionsDeleted { get; set; }
    public int EventsProcessed { get; set; } // Total events processed
    public int EventsSkipped { get; set; } // Events that were skipped

    // Indicates if this is an incremental sync (to show different UI)
    public bool IsIncrementalSync { get; set; }

    // Estimates
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public DateTime StartTime { get; set; }
}
