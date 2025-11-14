using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleverSyncSOS.Core.Database.SchoolDb;

/// <summary>
/// Design-time factory for SchoolDbContext to support EF Core migrations.
/// </summary>
public class SchoolDbContextFactory : IDesignTimeDbContextFactory<SchoolDbContext>
{
    public SchoolDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SchoolDbContext>();

        // Use a placeholder connection string for migrations
        // The actual connection string will be provided at runtime from Key Vault per school
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=SchoolDb_Template;Trusted_Connection=True;");

        return new SchoolDbContext(optionsBuilder.Options);
    }
}
