// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.Sync;
using CleverSyncSOS.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Console;

/// <summary>
/// Entry point for CleverSyncSOS console application.
/// Source: SpecKit/Plans/001-clever-api-auth/plan.md (Stage 1)
/// Demonstrates: OAuth authentication with Clever API
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Constitution: Use dependency injection for all services
            // FR-007: Configuration via Azure App Configuration or secure settings
            var host = CreateHostBuilder(args).Build();

            // Check command line arguments for demo mode
            if (args.Length > 0 && args[0].ToLower() == "sync")
            {
                // Stage 2: Demonstrate database synchronization
                await DemonstrateSyncAsync(host.Services);
            }
            else if (args.Length > 0 && args[0].ToLower() == "schools")
            {
                // Fetch and display schools from district
                await FetchSchoolsAsync(host.Services);
            }
            else if (args.Length > 0 && args[0].ToLower() == "students")
            {
                // Fetch and display students for a school
                var schoolId = args.Length > 1 ? args[1] : "67851a6997c11b0cb748506c"; // Default: City High School
                await FetchStudentsAsync(host.Services, schoolId);
            }
            else if (args.Length > 0 && args[0].ToLower() == "teachers")
            {
                // Fetch and display teachers for a school
                var schoolId = args.Length > 1 ? args[1] : "67851a6997c11b0cb748506c"; // Default: City High School
                await FetchTeachersAsync(host.Services, schoolId);
            }
            else
            {
                // Stage 1: Demonstrate authentication
                await DemonstrateAuthenticationAsync(host.Services);

                System.Console.WriteLine();
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("To test Stage 2 (Database Sync), run: dotnet run --project src/CleverSyncSOS.Console sync");
                System.Console.WriteLine("To fetch schools from Clever, run: dotnet run --project src/CleverSyncSOS.Console schools");
                System.Console.WriteLine("To fetch students from a school, run: dotnet run --project src/CleverSyncSOS.Console students [schoolId]");
                System.Console.WriteLine("To fetch teachers from a school, run: dotnet run --project src/CleverSyncSOS.Console teachers [schoolId]");
                System.Console.ResetColor();
            }

            return 0;
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"Fatal error: {ex.Message}");
            System.Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Creates and configures the host builder.
    /// Source: Constitution - Dependency injection, Configuration management
    /// </summary>
    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((context, config) =>
            {
                // Note: Host.CreateDefaultBuilder already adds to the config builder:
                // - appsettings.json
                // - appsettings.{Environment}.json
                // - User secrets (in Development)
                // - Environment variables
                // - Command line args

                // FR-002: Load SessionDb connection string from Azure Key Vault
                // Build current configuration to read KeyVaultUri from appsettings.json
                var tempConfig = config.Build();
                config.AddSessionDbConnectionStringFromKeyVault(tempConfig);
            })
            .ConfigureServices((context, services) =>
            {
                // Add CleverSyncSOS authentication services
                services.AddCleverAuthentication(context.Configuration);

                // Stage 2: Add Clever API client services
                services.AddCleverApiClient(context.Configuration);

                // Stage 2: Add database synchronization services
                services.AddCleverSync(context.Configuration);

                // FR-010: Add Application Insights (Stage 3)
                // Uncomment when Application Insights is configured:
                // services.AddCleverObservability(context.Configuration);
            })
            .ConfigureLogging((context, logging) =>
            {
                // FR-010: Structured logging
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();

                // Set log level from configuration
                var logLevel = context.Configuration.GetValue<LogLevel>("Logging:LogLevel:Default", LogLevel.Information);
                logging.SetMinimumLevel(logLevel);
            });

    /// <summary>
    /// Demonstrates Clever API authentication.
    /// Source: FR-001, FR-003 - OAuth authentication and token management
    /// </summary>
    static async Task DemonstrateAuthenticationAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var authService = services.GetRequiredService<ICleverAuthenticationService>();

        System.Console.WriteLine();
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine("CleverSyncSOS - Clever API Authentication Demo");
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine();

        try
        {
            // NFR-001: Authentication must complete within 5 seconds
            System.Console.WriteLine("Authenticating with Clever API...");
            var token = await authService.AuthenticateAsync();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("✓ Authentication successful!");
            System.Console.ResetColor();
            System.Console.WriteLine();

            // Display token information (sanitized - not showing actual token)
            System.Console.WriteLine("Token Information:");
            System.Console.WriteLine($"  Token Type: {token.TokenType}");
            System.Console.WriteLine($"  Expires In: {token.ExpiresIn} seconds");
            System.Console.WriteLine($"  Issued At: {token.IssuedAt:yyyy-MM-dd HH:mm:ss} UTC");
            System.Console.WriteLine($"  Expires At: {token.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
            System.Console.WriteLine($"  Time Until Expiration: {token.TimeUntilExpiration:hh\\:mm\\:ss}");
            System.Console.WriteLine($"  Should Refresh (75% threshold): {token.ShouldRefresh(75.0)}");
            System.Console.WriteLine();

            // Demonstrate token retrieval (should use cached token)
            System.Console.WriteLine("Retrieving token from cache...");
            var cachedToken = await authService.GetTokenAsync();

            if (cachedToken.AccessToken == token.AccessToken)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine("✓ Retrieved token from cache (no re-authentication needed)");
                System.Console.ResetColor();
            }

            System.Console.WriteLine();

            // Display health status
            var lastAuthTime = authService.GetLastSuccessfulAuthTime();
            var lastError = authService.GetLastError();

            System.Console.WriteLine("Health Status:");
            System.Console.WriteLine($"  Last Successful Auth: {lastAuthTime:yyyy-MM-dd HH:mm:ss} UTC");
            System.Console.WriteLine($"  Last Error: {lastError ?? "None"}");

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("Stage 1 (Core Implementation) demonstration complete!");
            System.Console.WriteLine("Next steps: Stage 2 (Database Sync) and Stage 3 (Health Endpoints)");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Authentication failed: {ex.Message}");
            System.Console.ResetColor();

            logger.LogError(ex, "Authentication demonstration failed");
            throw;
        }
    }

    /// <summary>
    /// Demonstrates database synchronization from Clever API.
    /// Source: Stage 2 - Database Sync
    /// </summary>
    static async Task DemonstrateSyncAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var syncService = services.GetRequiredService<ISyncService>();

        System.Console.WriteLine();
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine("CleverSyncSOS - Database Sync Demo");
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine();

        try
        {
            // Sync City High School (SchoolId = 3)
            const int cityHighSchoolId = 3;

            System.Console.WriteLine($"Starting sync for City High School (ID: {cityHighSchoolId})...");
            System.Console.WriteLine();

            System.Console.Write("Force full sync? (y/n) [default: y]: ");
            var input = System.Console.ReadLine();
            bool forceFullSync = string.IsNullOrEmpty(input) || input.ToLower() == "y";

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine($"Sync Type: {(forceFullSync ? "FULL SYNC (with hard-delete)" : "INCREMENTAL SYNC")}");
            System.Console.ResetColor();
            System.Console.WriteLine();

            var startTime = DateTime.UtcNow;
            var result = await syncService.SyncSchoolAsync(cityHighSchoolId, forceFullSync);
            var endTime = DateTime.UtcNow;

            System.Console.WriteLine();

            // Display results
            if (result.Success)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine("✓ Sync completed successfully!");
                System.Console.ResetColor();
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"✗ Sync failed: {result.ErrorMessage}");
                System.Console.ResetColor();
            }

            System.Console.WriteLine();
            System.Console.WriteLine("Sync Results:");
            System.Console.WriteLine($"  School: {result.SchoolName}");
            System.Console.WriteLine($"  Sync Type: {result.SyncType}");
            System.Console.WriteLine($"  Duration: {result.Duration:hh\\:mm\\:ss\\.fff}");
            System.Console.WriteLine();

            System.Console.WriteLine("Student Statistics:");
            System.Console.WriteLine($"  Processed: {result.StudentsProcessed}");
            System.Console.WriteLine($"  Failed: {result.StudentsFailed}");
            if (result.SyncType == Core.Database.SessionDb.Entities.SyncType.Full)
            {
                System.Console.WriteLine($"  Deleted (inactive): {result.StudentsDeleted}");
            }
            System.Console.WriteLine();

            System.Console.WriteLine("Teacher Statistics:");
            System.Console.WriteLine($"  Processed: {result.TeachersProcessed}");
            System.Console.WriteLine($"  Failed: {result.TeachersFailed}");
            if (result.SyncType == Core.Database.SessionDb.Entities.SyncType.Full)
            {
                System.Console.WriteLine($"  Deleted (inactive): {result.TeachersDeleted}");
            }
            System.Console.WriteLine();

            // Display sync history
            System.Console.WriteLine("Recent Sync History:");
            var history = await syncService.GetSyncHistoryAsync(cityHighSchoolId, limit: 5);

            if (history.Length > 0)
            {
                System.Console.WriteLine($"  {"Entity",-10} {"Type",-12} {"Status",-10} {"Records",-10} {"Started",-20}");
                System.Console.WriteLine($"  {new string('-', 70)}");

                foreach (var h in history)
                {
                    var statusColor = h.Status == "Success" ? ConsoleColor.Green : ConsoleColor.Red;
                    System.Console.Write($"  {h.EntityType,-10} {h.SyncType,-12} ");

                    var currentColor = System.Console.ForegroundColor;
                    System.Console.ForegroundColor = statusColor;
                    System.Console.Write($"{h.Status,-10}");
                    System.Console.ForegroundColor = currentColor;

                    System.Console.WriteLine($" {h.RecordsProcessed,-10} {h.SyncStartTime:yyyy-MM-dd HH:mm:ss}");
                }
            }
            else
            {
                System.Console.WriteLine("  No sync history found.");
            }

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("Stage 2 (Database Sync) demonstration complete!");
            System.Console.WriteLine("Check your CleverAspNetSession database to view synced students and teachers.");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Sync failed: {ex.Message}");
            System.Console.ResetColor();

            logger.LogError(ex, "Sync demonstration failed");
            throw;
        }
    }

    /// <summary>
    /// Fetches and displays schools from the Clever district.
    /// </summary>
    static async Task FetchSchoolsAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var apiClient = services.GetRequiredService<ICleverApiClient>();

        System.Console.WriteLine();
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine("Fetch Schools from Clever District");
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine();

        try
        {
            const string districtId = "67851920ae0217f745c67360";

            System.Console.WriteLine($"Fetching schools for district: {districtId}");
            System.Console.WriteLine($"District Name: #DEMO SOS Optimized Services (Dev) Sandbox");
            System.Console.WriteLine();

            var schools = await apiClient.GetSchoolsAsync(districtId);

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Found {schools.Length} schools in this district:");
            System.Console.ResetColor();
            System.Console.WriteLine();

            if (schools.Length == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("No schools found in this district.");
                System.Console.WriteLine("This might be a demo/sandbox district with no schools configured.");
                System.Console.ResetColor();
            }
            else
            {
                foreach (var school in schools)
                {
                    System.Console.WriteLine($"School ID: {school.Id}");
                    System.Console.WriteLine($"Name: {school.Name}");
                    if (!string.IsNullOrEmpty(school.District))
                    {
                        System.Console.WriteLine($"District: {school.District}");
                    }
                    System.Console.WriteLine();
                }

                System.Console.WriteLine();
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.WriteLine("To update your SessionDb.Schools table with the correct Clever School IDs,");
                System.Console.WriteLine("run an UPDATE statement using the School IDs shown above.");
                System.Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Failed to fetch schools: {ex.Message}");
            System.Console.ResetColor();

            logger.LogError(ex, "Failed to fetch schools from Clever");
            throw;
        }
    }

    /// <summary>
    /// Fetches and displays students for a school.
    /// </summary>
    static async Task FetchStudentsAsync(IServiceProvider services, string schoolId)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var apiClient = services.GetRequiredService<ICleverApiClient>();

        System.Console.WriteLine();
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine("Fetch Students from Clever School");
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine();

        try
        {
            System.Console.WriteLine($"Fetching students for school: {schoolId}");
            System.Console.WriteLine();

            var students = await apiClient.GetStudentsAsync(schoolId);

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Found {students.Length} students in this school:");
            System.Console.ResetColor();
            System.Console.WriteLine();

            if (students.Length == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("No students found in this school.");
                System.Console.ResetColor();
            }
            else
            {
                // Display first 10 students
                var displayCount = Math.Min(10, students.Length);
                for (int i = 0; i < displayCount; i++)
                {
                    var student = students[i];
                    System.Console.WriteLine($"Student #{i + 1}:");
                    System.Console.WriteLine($"  ID: {student.Id}");
                    System.Console.WriteLine($"  Name: {student.Name.First} {student.Name.Middle} {student.Name.Last}");
                    System.Console.WriteLine($"  Email: {student.Email ?? "N/A"}");
                    System.Console.WriteLine($"  Student Number: {student.StudentNumber ?? "N/A"}");
                    System.Console.WriteLine($"  SIS ID: {student.SisId ?? "N/A"}");
                    System.Console.WriteLine($"  Graduation Year: {student.Grade ?? "N/A"}");
                    System.Console.WriteLine();
                }

                if (students.Length > 10)
                {
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Console.WriteLine($"... and {students.Length - 10} more students");
                    System.Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Failed to fetch students: {ex.Message}");
            System.Console.ResetColor();

            logger.LogError(ex, "Failed to fetch students from Clever");
            throw;
        }
    }

    /// <summary>
    /// Fetches and displays teachers for a school.
    /// </summary>
    static async Task FetchTeachersAsync(IServiceProvider services, string schoolId)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var apiClient = services.GetRequiredService<ICleverApiClient>();

        System.Console.WriteLine();
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine("Fetch Teachers from Clever School");
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine();

        try
        {
            System.Console.WriteLine($"Fetching teachers for school: {schoolId}");
            System.Console.WriteLine();

            var teachers = await apiClient.GetTeachersAsync(schoolId);

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"✓ Found {teachers.Length} teachers in this school:");
            System.Console.ResetColor();
            System.Console.WriteLine();

            if (teachers.Length == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("No teachers found in this school.");
                System.Console.ResetColor();
            }
            else
            {
                // Display first 10 teachers
                var displayCount = Math.Min(10, teachers.Length);
                for (int i = 0; i < displayCount; i++)
                {
                    var teacher = teachers[i];
                    System.Console.WriteLine($"Teacher #{i + 1}:");
                    System.Console.WriteLine($"  ID: {teacher.Id}");
                    System.Console.WriteLine($"  Name: {teacher.Name.First} {teacher.Name.Last}");
                    System.Console.WriteLine($"  Email: {teacher.Email}");
                    System.Console.WriteLine($"  Title: {teacher.Title ?? "N/A"}");
                    System.Console.WriteLine($"  Teacher Number: {teacher.TeacherNumber ?? "N/A"}");
                    System.Console.WriteLine($"  SIS ID: {teacher.SisId ?? "N/A"}");
                    System.Console.WriteLine($"  State ID: {teacher.StateId ?? "N/A"}");
                    System.Console.WriteLine();
                }

                if (teachers.Length > 10)
                {
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Console.WriteLine($"... and {teachers.Length - 10} more teachers");
                    System.Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Failed to fetch teachers: {ex.Message}");
            System.Console.ResetColor();

            logger.LogError(ex, "Failed to fetch teachers from Clever");
            throw;
        }
    }
}
