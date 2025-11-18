using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using CleverSyncSOS.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Database.SchoolDb;

/// <summary>
/// Factory for creating SchoolDbContext instances with dynamic connection strings.
/// </summary>
public class SchoolDatabaseConnectionFactory
{
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<SchoolDatabaseConnectionFactory> _logger;

    public SchoolDatabaseConnectionFactory(
        ICredentialStore credentialStore,
        ILogger<SchoolDatabaseConnectionFactory> logger)
    {
        _credentialStore = credentialStore;
        _logger = logger;
    }

    /// <summary>
    /// Creates a SchoolDbContext for the specified school using its connection string from Key Vault.
    /// </summary>
    /// <param name="school">The school entity containing connection string reference.</param>
    /// <returns>A configured SchoolDbContext instance.</returns>
    public async Task<SchoolDbContext> CreateSchoolContextAsync(School school)
    {
        if (string.IsNullOrEmpty(school.KeyVaultConnectionStringSecretName))
        {
            throw new InvalidOperationException($"School {school.Name} (ID: {school.SchoolId}) does not have a Key Vault connection string secret name configured.");
        }

        // FR-010: Log secret name retrieval (safe - no connection string value)
        _logger.LogInformation("Retrieving connection string for school {SchoolName} from Key Vault",
            school.Name);

        try
        {
            var connectionString = await _credentialStore.GetSecretAsync(school.KeyVaultConnectionStringSecretName);

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"Failed to retrieve connection string for school {school.Name} from Key Vault.");
            }

            var options = new DbContextOptionsBuilder<SchoolDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new SchoolDbContext(options);
        }
        catch (Exception ex)
        {
            // FR-010: Sanitize exception to prevent connection string leakage
            var sanitizedError = SensitiveDataSanitizer.CreateSafeErrorSummary(ex, $"School: {school.Name}");
            _logger.LogError("Failed to create database context. {SanitizedError}", sanitizedError);
            throw;
        }
    }
}
