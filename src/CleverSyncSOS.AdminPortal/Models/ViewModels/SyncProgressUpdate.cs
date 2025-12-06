namespace CleverSyncSOS.AdminPortal.Models.ViewModels;

/// <summary>
/// SignalR message model for real-time sync progress updates.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SyncProgressUpdate
{
    public string SyncId { get; set; } = Guid.NewGuid().ToString();
    public int PercentComplete { get; set; } // 0-100
    public string CurrentOperation { get; set; } = string.Empty; // "Fetching students..."

    // Counters
    public int StudentsProcessed { get; set; } // Total students examined
    public int StudentsUpdated { get; set; } // Students that actually changed
    public int StudentsFailed { get; set; }
    public int TeachersProcessed { get; set; } // Total teachers examined
    public int TeachersUpdated { get; set; } // Teachers that actually changed
    public int TeachersFailed { get; set; }

    // Estimates
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public DateTime StartTime { get; set; }
}
