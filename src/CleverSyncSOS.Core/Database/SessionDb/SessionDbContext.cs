using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleverSyncSOS.Core.Database.SessionDb;

/// <summary>
/// DbContext for the SessionDb (orchestration database).
/// Stores metadata about districts, schools, and sync operations.
/// </summary>
public class SessionDbContext : DbContext
{
    public SessionDbContext(DbContextOptions<SessionDbContext> options) : base(options)
    {
    }

    public DbSet<District> Districts { get; set; } = null!;
    public DbSet<School> Schools { get; set; } = null!;
    public DbSet<SyncHistory> SyncHistory { get; set; } = null!;
    public DbSet<SyncChangeDetail> SyncChangeDetails { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<EventsLog> EventsLogs { get; set; } = null!;
    public DbSet<SyncWarning> SyncWarnings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure District entity
        modelBuilder.Entity<District>(entity =>
        {
            entity.HasKey(e => e.DistrictId);
            entity.HasIndex(e => e.CleverDistrictId).IsUnique();
            entity.Property(e => e.CleverDistrictId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.KeyVaultDistrictPrefix).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Configure relationship with Schools
            entity.HasMany(d => d.Schools)
                .WithOne(s => s.District)
                .HasForeignKey(s => s.DistrictId)
                .HasPrincipalKey(d => d.CleverDistrictId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure School entity
        modelBuilder.Entity<School>(entity =>
        {
            entity.HasKey(e => e.SchoolId);
            entity.HasIndex(e => e.CleverSchoolId).IsUnique();
            entity.Property(e => e.DistrictId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CleverSchoolId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.KeyVaultSchoolPrefix).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DatabaseName).HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.RequiresFullSync).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            // Foreign key to District is configured via the District entity (uses CleverDistrictId as principal key)

            // Configure relationship with SyncHistory
            entity.HasMany(s => s.SyncHistories)
                .WithOne(h => h.School)
                .HasForeignKey(h => h.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure SyncHistory entity
        modelBuilder.Entity<SyncHistory>(entity =>
        {
            entity.HasKey(e => e.SyncId);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SyncType).IsRequired().HasDefaultValue(SyncType.Incremental);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.SyncStartTime).IsRequired();
            entity.Property(e => e.RecordsProcessed).HasDefaultValue(0);
            entity.Property(e => e.RecordsFailed).HasDefaultValue(0);

            // Create composite index for efficient queries
            entity.HasIndex(e => new { e.SchoolId, e.EntityType, e.SyncEndTime });

            // Foreign key to School is configured via the School entity
        });

        // Configure SyncChangeDetail entity
        modelBuilder.Entity<SyncChangeDetail>(entity =>
        {
            entity.HasKey(e => e.ChangeDetailId);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntityId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntityName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ChangeType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.FieldsChanged).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.ChangedAt).IsRequired();

            // Create indexes for efficient queries
            entity.HasIndex(e => new { e.SyncId, e.EntityType });
            entity.HasIndex(e => e.ChangeType);
            entity.HasIndex(e => e.ChangedAt);

            // Configure relationship with SyncHistory
            entity.HasOne(c => c.SyncHistory)
                .WithMany()
                .HasForeignKey(c => c.SyncId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.CleverUserId);
            entity.HasIndex(e => new { e.Role, e.IsActive });
            entity.HasIndex(e => e.SchoolId);
            entity.HasIndex(e => e.DistrictId);
            entity.HasIndex(e => e.AuthenticationSource);

            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AuthenticationSource).IsRequired().HasMaxLength(20);
            entity.Property(e => e.DistrictId).HasMaxLength(50);
            entity.Property(e => e.CleverUserId).HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(256);

            // Configure relationship with School
            entity.HasOne(u => u.School)
                .WithMany()
                .HasForeignKey(u => u.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure relationship with District
            entity.HasOne(u => u.District)
                .WithMany()
                .HasForeignKey(u => u.DistrictId)
                .HasPrincipalKey(d => d.CleverDistrictId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure AuditLog entity
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.Action, e.Success });
            entity.HasIndex(e => e.IpAddress);

            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.UserIdentifier).HasMaxLength(256);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 max length
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.Success).IsRequired();
            entity.Property(e => e.Resource).HasMaxLength(200);

            // Configure relationship with User
            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
        // Configure EventsLog entity
        modelBuilder.Entity<EventsLog>(entity =>
        {
            entity.HasKey(e => e.EventsLogId);
            entity.HasIndex(e => e.CheckedAt);
            entity.Property(e => e.CheckedAt).IsRequired();
            entity.Property(e => e.CheckedBy).HasMaxLength(256);
            entity.Property(e => e.ApiAccessible).IsRequired();
            entity.Property(e => e.EventCount).HasDefaultValue(0);
            entity.Property(e => e.CreatedCount).HasDefaultValue(0);
            entity.Property(e => e.UpdatedCount).HasDefaultValue(0);
            entity.Property(e => e.DeletedCount).HasDefaultValue(0);
            entity.Property(e => e.LatestEventId).HasMaxLength(100);
            entity.Property(e => e.ObjectTypeSummary).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        // Configure SyncWarning entity
        modelBuilder.Entity<SyncWarning>(entity =>
        {
            entity.HasKey(e => e.SyncWarningId);
            entity.HasIndex(e => e.SyncId);
            entity.HasIndex(e => e.WarningType);
            entity.HasIndex(e => e.IsAcknowledged);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.WarningType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CleverEntityId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntityName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.AffectedWorkshops).HasMaxLength(4000);
            entity.Property(e => e.AcknowledgedBy).HasMaxLength(256);
            entity.Property(e => e.CreatedAt).IsRequired();

            // Configure relationship with SyncHistory
            entity.HasOne(w => w.SyncHistory)
                .WithMany()
                .HasForeignKey(w => w.SyncId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}