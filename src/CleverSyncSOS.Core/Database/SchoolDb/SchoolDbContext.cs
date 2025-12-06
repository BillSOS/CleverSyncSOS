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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Student entity
        modelBuilder.Entity<Student>(entity =>
        {
            entity.HasKey(e => e.StudentId);
            entity.HasIndex(e => e.CleverStudentId).IsUnique();
            entity.Property(e => e.CleverStudentId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Grade).HasMaxLength(20);
            entity.Property(e => e.StudentNumber).HasMaxLength(50);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Create index for active students
            entity.HasIndex(e => e.IsActive);
        });

        // Configure Teacher entity
        modelBuilder.Entity<Teacher>(entity =>
        {
            entity.HasKey(e => e.TeacherId);
            entity.HasIndex(e => e.CleverTeacherId).IsUnique();
            entity.Property(e => e.CleverTeacherId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Title).HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Create index for active teachers
            entity.HasIndex(e => e.IsActive);
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
            entity.HasKey(e => e.SectionId);
            entity.HasIndex(e => e.CleverSectionId).IsUnique();
            entity.HasIndex(e => e.CourseId);
            entity.HasIndex(e => e.SchoolId);
            entity.HasIndex(e => e.TermId);
            entity.HasIndex(e => e.Subject);
            entity.HasIndex(e => e.Grade);
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CleverSectionId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Period).HasMaxLength(100);
            entity.Property(e => e.Subject).HasMaxLength(100);
            entity.Property(e => e.SubjectNormalized).HasMaxLength(100);
            entity.Property(e => e.TermId).HasMaxLength(255);
            entity.Property(e => e.TermName).HasMaxLength(200);
            entity.Property(e => e.Grade).HasMaxLength(50);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

            // Relationship: Section -> Course (required)
            entity.HasOne(e => e.Course)
                  .WithMany(c => c.Sections)
                  .HasForeignKey(e => e.CourseId)
                  .OnDelete(DeleteBehavior.Restrict); // Don't allow course deletion if sections exist
        });

        // Configure TeacherSection entity
        modelBuilder.Entity<TeacherSection>(entity =>
        {
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
    }
}
