// ---
// speckit:
//   type: implementation
//   source: SpecKit/Plans/001-clever-api-auth/plan.md
//   section: Configuration Management
//   constitution: SpecKit/Constitution/constitution.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace CleverSyncSOS.Infrastructure.Extensions;

/// <summary>
/// Extension methods for loading configuration values from Azure Key Vault.
/// Source: FR-002 - Credential storage with Azure Key Vault
/// </summary>
public static class KeyVaultConfigurationExtensions
{
    /// <summary>
    /// Adds SessionDb connection string from Azure Key Vault to the configuration builder.
    /// This should be called during ConfigureAppConfiguration phase.
    /// </summary>
    /// <param name="builder">The configuration builder</param>
    /// <param name="configuration">The configuration built so far (use context.Configuration)</param>
    /// <returns>The configuration builder for chaining</returns>
    public static IConfigurationBuilder AddSessionDbConnectionStringFromKeyVault(
        this IConfigurationBuilder builder,
        IConfiguration configuration)
    {
        var keyVaultUri = configuration["CleverAuth:KeyVaultUri"];

        if (string.IsNullOrEmpty(keyVaultUri))
        {
            Console.WriteLine("Warning: KeyVault URI is not configured. Skipping Key Vault connection string loading.");
            Console.WriteLine("To use Key Vault: Set CleverAuth:KeyVaultUri in appsettings.json or use environment variable CleverAuth__KeyVaultUri");
            return builder;
        }

        var secretName = configuration["KeyVault:SessionDbConnectionStringSecretName"];
        if (string.IsNullOrEmpty(secretName))
        {
            secretName = "SessionDb-ConnectionString";
        }

        try
        {
            var credential = new DefaultAzureCredential();
            var secretClient = new SecretClient(new Uri(keyVaultUri), credential);

            var secret = secretClient.GetSecret(secretName);
            var connectionString = secret.Value.Value;

            // Add the connection string to the configuration builder as an in-memory collection
            var connectionStringDict = new Dictionary<string, string?>
            {
                { "ConnectionStrings:SessionDb", connectionString }
            };

            builder.AddInMemoryCollection(connectionStringDict);

            Console.WriteLine($"✓ Loaded SessionDb connection string from Key Vault secret: {secretName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load SessionDb connection string from Key Vault: {ex.Message}");
            Console.WriteLine("The application will attempt to use connection string from appsettings.json or environment variables.");
        }

        return builder;
    }
}
