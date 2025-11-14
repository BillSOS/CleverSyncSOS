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
            entity.Property(e => e.KeyVaultSecretPrefix).HasMaxLength(100);
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
            entity.Property(e => e.DatabaseName).HasMaxLength(100);
            entity.Property(e => e.KeyVaultConnectionStringSecretName).HasMaxLength(200);
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
    }
}
