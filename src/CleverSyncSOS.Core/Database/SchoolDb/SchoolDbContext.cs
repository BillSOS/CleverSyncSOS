using CleverSyncSOS.Core.Database.SchoolDb.Entities;

using Microsoft.EntityFrameworkCore;

namespace CleverSyncSOS.Core.Database.SchoolDb;

/// <summary>
/// DbContext for per-school databases.
/// Each school has its own dedicated database containing student and teacher data.
/// </summary>
public class SchoolDbContext : DbContext
{
    public SchoolDbContext(DbContextOptions<SchoolDbContext> options) : base(options)
    {
    }

    public DbSet<Student> Students { get; set; } = null!;
    public DbSet<Teacher> Teachers { get; set; } = null!;
    public DbSet<Course> Courses { get; set; } = null!;
    public DbSet<Section> Sections { get; set; } = null!;
    public DbSet<TeacherSection> TeacherSections { get; set; } = null!;
    public DbSet<StudentSection> StudentSections { get; set; } = null!;
    public DbSet<Workshop> Workshops { get; set; } = null!;
    public DbSet<WorkshopSection> WorkshopSections { get; set; } = null!;
    public DbSet<WorkshopSchedule> WorkshopSchedules { get; set; } = null!;
    public DbSet<WorkshopSyncAudit> WorkshopSyncAudits { get; set; } = null!;
    public DbSet<Term> Terms { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Student entity
        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(e => e.StudentId);
            entity.HasIndex(e => e.CleverStudentId).IsUnique();
            entity.Property(e => e.CleverStudentId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(32);
            entity.Property(e => e.MiddleName).HasMaxLength(32);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(32);
            entity.Property(e => e.StateStudentId).IsRequired().HasMaxLength(32);
            entity.Property(e => e.StudentNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.GradeLevel).HasMaxLength(20);
            entity.Property(e => e.BlendedLearningAssignment).HasMaxLength(255);

            // Create index for soft-deleted students (null = active)
            entity.HasIndex(e => e.DeletedAt);
        });

        // Configure Teacher entity
        modelBuilder.Entity<Teacher>(entity =>
        {
            entity.HasKey(e => e.TeacherId);
            entity.HasIndex(e => e.CleverTeacherId);
            entity.HasIndex(e => e.StaffNumber).IsUnique();
            entity.Property(e => e.CleverTeacherId).HasMaxLength(50);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(32);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(32);
            entity.Property(e => e.FullName).HasMaxLength(255);
            entity.Property(e => e.UserName).HasMaxLength(255);
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.VirtualMeeting).HasMaxLength(256);
            entity.Property(e => e.StaffNumber).IsRequired().HasMaxLength(32);
            entity.Property(e => e.LegacyId).HasMaxLength(50);
            entity.Property(e => e.TeacherNumber).HasMaxLength(50);
            entity.Property(e => e.PriorityId).HasDefaultValue(2);
            entity.Property(e => e.Administrator).HasDefaultValue(false);
            entity.Property(e => e.IgnoreImport).HasDefaultValue(false);
            entity.Property(e => e.AllStudentsWorkshops).HasDefaultValue(false);
            entity.Property(e => e.NoWorkshops).HasDefaultValue(false);

            // Create index for soft-deleted teachers (null = active)
            entity.HasIndex(e => e.DeletedAt);
        });

        // Configure Course entity
        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(e => e.CourseId);
            entity.HasIndex(e => e.CleverCourseId).IsUnique();
            entity.HasIndex(e => e.SchoolId);
            entity.HasIndex(e => e.Subject);
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CleverCourseId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Number).HasMaxLength(100);
            entity.Property(e => e.Subject).HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        });

        // Configure Section entity
        modelBuilder.Entity<Section>(entity =>
        {
            entity.ToTable("Section");
            entity.HasKey(e => e.SectionId);
            entity.HasIndex(e => e.CleverSectionId).IsUnique();
            entity.HasIndex(e => e.DeletedAt);
            entity.HasIndex(e => e.LastEventReceivedAt);

            entity.Property(e => e.CleverSectionId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SectionName).HasMaxLength(100);
            entity.Property(e => e.Subject).HasMaxLength(100);
            entity.Property(e => e.Period).HasMaxLength(100);
            entity.Property(e => e.TermId).HasMaxLength(255);
            entity.Property(e => e.TermName).HasMaxLength(200);
        });

        // Configure TeacherSection entity
        modelBuilder.Entity<TeacherSection>(entity =>
        {
            entity.ToTable("Section_X_Teacher");
            entity.HasKey(e => e.TeacherSectionId);
            entity.HasIndex(e => e.TeacherId);
            entity.HasIndex(e => e.SectionId);
            entity.HasIndex(e => e.IsPrimary);
            entity.HasIndex(e => new { e.TeacherId, e.SectionId }).IsUnique();

            // Relationship: TeacherSection -> Teacher
            entity.HasOne(e => e.Teacher)
                  .WithMany()
                  .HasForeignKey(e => e.TeacherId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relationship: TeacherSection -> Section
            entity.HasOne(e => e.Section)
                  .WithMany(s => s.TeacherSections)
                  .HasForeignKey(e => e.SectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure StudentSection entity
        modelBuilder.Entity<StudentSection>(entity =>
        {
            entity.HasKey(e => e.StudentSectionId);
            entity.HasIndex(e => e.StudentId);
            entity.HasIndex(e => e.SectionId);
            entity.HasIndex(e => new { e.StudentId, e.SectionId }).IsUnique();

            // Relationship: StudentSection -> Student
            entity.HasOne(e => e.Student)
                  .WithMany()
                  .HasForeignKey(e => e.StudentId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relationship: StudentSection -> Section
            entity.HasOne(e => e.Section)
                  .WithMany(s => s.StudentSections)
                  .HasForeignKey(e => e.SectionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Workshop entity
        modelBuilder.Entity<Workshop>(entity =>
        {
            entity.HasKey(e => e.WorkshopId);
            entity.Property(e => e.WorkshopName).IsRequired().HasMaxLength(255);
        });

        // Configure WorkshopSection entity (Workshop_X_Section table)
        modelBuilder.Entity<WorkshopSection>(entity =>
        {
            entity.ToTable("Workshop_X_Section");
            entity.HasKey(e => e.WorkshopXSectionId);
            entity.HasIndex(e => e.WorkshopId);
            entity.HasIndex(e => e.SectionId);

            entity.Property(e => e.KeepAssignments).HasDefaultValue(false);

            // Relationship: WorkshopSection -> Workshop
            entity.HasOne(e => e.Workshop)
                  .WithMany(w => w.WorkshopSections)
                  .HasForeignKey(e => e.WorkshopId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relationship: WorkshopSection -> Section
            // RESTRICT delete - sync should alert if a linked section would be deleted
            entity.HasOne(e => e.Section)
                  .WithMany()
                  .HasForeignKey(e => e.SectionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure WorkshopSchedule entity
        modelBuilder.Entity<WorkshopSchedule>(entity =>
        {
            entity.HasKey(e => e.WorkshopScheduleId);
            entity.HasIndex(e => e.WorkshopId);
            entity.HasIndex(e => e.WorkshopDate);

            // Relationship: WorkshopSchedule -> Workshop
            entity.HasOne(e => e.Workshop)
                  .WithMany()
                  .HasForeignKey(e => e.WorkshopId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure WorkshopSyncAudit entity
        modelBuilder.Entity<WorkshopSyncAudit>(entity =>
        {
            entity.HasKey(e => e.WorkshopSyncAuditId);
            entity.HasIndex(e => e.StudentId);
            entity.HasIndex(e => e.ImportLogId); // For querying by SyncId
            entity.HasIndex(e => e.WorkshopDate);

            entity.Property(e => e.ActionType).IsRequired().HasMaxLength(50);

            // Relationship: WorkshopSyncAudit -> Student
            entity.HasOne(e => e.Student)
                  .WithMany()
                  .HasForeignKey(e => e.StudentId)
                  .OnDelete(DeleteBehavior.NoAction);

            // Relationship: WorkshopSyncAudit -> OldWorkshopSchedule
            entity.HasOne(e => e.OldWorkshopSchedule)
                  .WithMany()
                  .HasForeignKey(e => e.OldWorkshopScheduleId)
                  .OnDelete(DeleteBehavior.NoAction);

            // Relationship: WorkshopSyncAudit -> NewWorkshopSchedule
            entity.HasOne(e => e.NewWorkshopSchedule)
                  .WithMany()
                  .HasForeignKey(e => e.NewWorkshopScheduleId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        // Configure Term entity
        modelBuilder.Entity<Term>(entity =>
        {
            entity.HasKey(e => e.TermId);
            entity.HasIndex(e => e.CleverTermId).IsUnique();
            entity.HasIndex(e => e.CleverDistrictId);
            entity.HasIndex(e => e.DeletedAt);
            entity.HasIndex(e => e.LastSyncedAt);

            entity.Property(e => e.CleverTermId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CleverDistrictId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(200);
        });
    }
}
