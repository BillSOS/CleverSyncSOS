using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleverSyncSOS.Core.Database.SessionDb;

/// <summary>
/// Design-time factory for SessionDbContext to support EF Core migrations.
/// </summary>
public class SessionDbContextFactory : IDesignTimeDbContextFactory<SessionDbContext>
{
    public SessionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SessionDbContext>();

        // Use a placeholder connection string for migrations
        // The actual connection string will be provided at runtime from Key Vault
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=SessionDb;Trusted_Connection=True;");

        return new SessionDbContext(optionsBuilder.Options);
    }
}
