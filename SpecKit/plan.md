---
speckit:
  type: plan
  title: Clever API Authentication and Connection
  owner: Bill Martin
  version: 1.0.0
  branch: 001-clever-api-auth
  date: 2025-11-08
  linked_spec: SpecKit/spec.md
  input: SpecKit/spec.md
---


# Implementation Plan: Clever API Authentication and Connection

**Branch**: `001-clever-api-auth` | **Date**: 2025-11-08  
**Spec**: [spec.md](..\..\..\..\..\spec.md)  
**Input**: Feature specification from `/specs/001-clever-api-auth/spec.md`

* * *

## Summary

This feature establishes secure authentication and connection management for the Clever API using OAuth 2.0 client credentials flow. Credentials are stored in Azure Key Vault and retrieved using managed identity. The system implements robust retry logic, proactive token refresh, and comprehensive error handling with Azure Monitor alerting. It supports graceful degradation when dependencies are unavailable and provides health check endpoints for operational monitoring.

* * *

## Recent Clarifications (2025-12-02)

The following clarifications were added to the spec and implementation based on production deployment learnings:

1. **Events API Strategy**: Incremental sync prioritizes Clever Events API when available, with data API fallback for districts without Events enabled. This provides performance optimization by fetching only changed records.

2. **Baseline Event ID**: Baseline event ID is re-established after each successful full sync to ensure Events API can be used for subsequent incremental syncs, even if Events wasn't available during initial setup.

3. **Sync Metrics Accuracy**: "Updated" count reflects only records with actual data changes (not all processed records). "Processed" count shows all records examined. This prevents false positives in monitoring dashboards.

4. **String Comparison Normalization**: Change detection normalizes strings by treating null, empty string, and whitespace as equivalent. This prevents unnecessary database updates when Clever returns empty strings for fields stored as null.

5. **Entity Framework Change Tracker**: During full sync, EF change tracker is cleared after marking records inactive to prevent stale cached entities from affecting upsert logic.

These clarifications are reflected in the implementation sections below.

* * *

## Implementation Stages

### Stage 1: Azure Function with Clever Authentication [Phase: Core Implementation]

**Goal**: Create an Azure Function that successfully authenticates with Clever API using OAuth 2.0

**Scope**:

- Azure Function project setup
- Key Vault integration for credential storage
- Managed identity configuration
- OAuth 2.0 client credentials flow implementation
- Token acquisition and basic refresh logic
- Basic logging and error handling

### Stage 2: Clever-to-Azure Database Sync [Phase: Database Sync]

**Goal**: Add full sync functionality to read data from Clever and write to per-school Azure databases

**Scope**:

- Data fetching from Clever API endpoints (students, teachers)
- Dual database architecture: SessionDb (orchestration) + per-school databases (data)
- Data mapping and transformation logic
- Sync orchestration and scheduling
- Retry logic for API and database operations
- Comprehensive error handling and recovery
- Multi-district and multi-school support with data isolation
- Incremental sync with change detection

**Architecture Overview**:

```
SessionDb (Control Database)
  └─ Tracks districts, schools, sync history
  └─ Connection: Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;...

School Databases (Per-School Data Isolation)
  ├─ School_A_Database → Students, Teachers, Sections
  ├─ School_B_Database → Students, Teachers, Sections
  └─ Connection strings stored in Key Vault per school
```

**Sync Flow**:
1. Connect to SessionDb to retrieve list of schools to sync
2. For each school:
   - Retrieve school-specific connection string from Key Vault
   - Connect to that school's dedicated database
   - Fetch data from Clever API for that school
   - Sync students/teachers to school's database
   - Record sync results back to SessionDb.SyncHistory
3. Continue with next school (with data isolation guaranteed)

**Implementation Approach**:

#### 2.1 Database Architecture

**SessionDb Schema** (Orchestration & Metadata):
```sql
Districts
  ├─ DistrictId (PK, int, identity)
  ├─ CleverDistrictId (unique, nvarchar(50))
  ├─ Name (nvarchar(200))
  ├─ DistrictPrefix (nvarchar(100), indexed) -- e.g., "Lincoln-District" (NEW)
  ├─ KeyVaultSecretPrefix (nvarchar(100), nullable) -- DEPRECATED, use DistrictPrefix
  ├─ CreatedAt (datetime2)
  └─ UpdatedAt (datetime2)

Schools
  ├─ SchoolId (PK, int, identity)
  ├─ DistrictId (FK → Districts.DistrictId)
  ├─ CleverSchoolId (unique, nvarchar(50))
  ├─ Name (nvarchar(200))
  ├─ DatabaseName (nvarchar(100)) -- e.g., "School_Lincoln_Db"
  ├─ SchoolPrefix (nvarchar(100), indexed) -- e.g., "Lincoln-Elementary" (NEW)
  ├─ KeyVaultConnectionStringSecretName (nvarchar(200), nullable) -- DEPRECATED, use SchoolPrefix
  ├─ IsActive (bit, default 1)
  ├─ CreatedAt (datetime2)
  └─ UpdatedAt (datetime2)

SyncHistory
  ├─ SyncId (PK, int, identity)
  ├─ SchoolId (FK → Schools.SchoolId)
  ├─ EntityType (nvarchar(50)) -- "Student", "Teacher", "Section", "Baseline", "Event"
  ├─ SyncType (nvarchar(20)) -- "Full", "Incremental"
  ├─ SyncStartTime (datetime2)
  ├─ SyncEndTime (datetime2, nullable)
  ├─ Status (nvarchar(20)) -- "Success", "Failed", "Partial"
  ├─ RecordsProcessed (int) -- Count of all records examined/fetched from Clever
  ├─ RecordsUpdated (int) -- Count of records with actual data changes persisted (NEW)
  ├─ RecordsFailed (int)
  ├─ ErrorMessage (nvarchar(max), nullable)
  ├─ LastSyncTimestamp (datetime2, nullable) -- for incremental sync with data API
  └─ LastEventId (nvarchar(100), nullable) -- for incremental sync with Events API (NEW)
```

**Per-School Database Schema** (Student/Teacher Data):
```sql
Students
  ├─ StudentId (PK, int, identity)
  ├─ CleverStudentId (unique, nvarchar(50))
  ├─ FirstName (nvarchar(100))
  ├─ LastName (nvarchar(100))
  ├─ Email (nvarchar(200), nullable)
  ├─ Grade (nvarchar(20), nullable)
  ├─ StudentNumber (nvarchar(50), nullable)
  ├─ LastModifiedInClever (datetime2, nullable)
  ├─ CreatedAt (datetime2)
  └─ UpdatedAt (datetime2)

Teachers
  ├─ TeacherId (PK, int, identity)
  ├─ CleverTeacherId (unique, nvarchar(50))
  ├─ FirstName (nvarchar(100))
  ├─ LastName (nvarchar(100))
  ├─ Email (nvarchar(200))
  ├─ Title (nvarchar(100), nullable)
  ├─ LastModifiedInClever (datetime2, nullable)
  ├─ CreatedAt (datetime2)
  └─ UpdatedAt (datetime2)

-- Future: Sections table
```

**Entity Framework Core Setup**:
- Create `SessionDbContext` for SessionDb (Districts, Schools, SyncHistory)
- Create `SchoolDbContext` for per-school databases (Students, Teachers)
- Add `Microsoft.EntityFrameworkCore.SqlServer` package
- Add `Microsoft.EntityFrameworkCore.Design` package for migrations
- Implement separate migration paths for SessionDb vs School databases

#### 2.2 Connection String Management

**Key Vault Secret Structure**:

All secrets follow the standardized naming convention: `CleverSyncSOS--{Component}--{Property}`

```
# Global secrets (used by all components)
CleverSyncSOS--Clever--ClientId: "clever_client_id"
CleverSyncSOS--Clever--ClientSecret: "clever_secret"
CleverSyncSOS--Clever--AccessToken: "optional_pre_generated_token" (optional)

# SessionDb connection (orchestration database)
CleverSyncSOS--SessionDb--ConnectionString: "Server=tcp:sos-northcentral.database.windows.net,1433;Initial Catalog=SessionDb;..."

# Per-school database connections (using SchoolPrefix from database)
CleverSyncSOS--{SchoolPrefix}--ConnectionString: "Server=tcp:...;Initial Catalog=School_{SchoolPrefix}_Db;..."
# Examples:
#   CleverSyncSOS--Lincoln-Elementary--ConnectionString
#   CleverSyncSOS--Washington-Elementary--ConnectionString

# Per-district credentials (using DistrictPrefix from database) - if districts have separate Clever apps
CleverSyncSOS--{DistrictPrefix}--ClientId: "district_clever_client_id"
CleverSyncSOS--{DistrictPrefix}--ClientSecret: "district_clever_secret"
# Examples:
#   CleverSyncSOS--Lincoln-District--ClientId
#   CleverSyncSOS--Boone-District--ClientId

# Admin Portal
CleverSyncSOS--AdminPortal--SuperAdminPassword: "admin_password_hash"
```

**Secret Name Construction**:
- School secrets use `School.SchoolPrefix` field from database
- District secrets use `District.DistrictPrefix` field from database
- Global secrets use fixed component names (Clever, SessionDb, AdminPortal)
- All secrets follow double-dash delimiter pattern for consistency

**Dynamic Connection Resolution**:
```csharp
public class SchoolDatabaseConnectionFactory
{
    private readonly ICredentialStore _credentialStore;

    public async Task<SchoolDbContext> CreateSchoolContextAsync(School school)
    {
        // Use prefix-based secret lookup
        // Constructs: CleverSyncSOS--{SchoolPrefix}--ConnectionString
        var connectionString = await _credentialStore.GetSchoolSecretAsync(
            school.SchoolPrefix,
            "ConnectionString");

        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SchoolDbContext(options);
    }
}

// ICredentialStore provides standardized secret name builders
public interface ICredentialStore
{
    Task<string> GetGlobalSecretAsync(string component, string property);
    Task<string> GetSchoolSecretAsync(string schoolPrefix, string property);
    Task<string> GetDistrictSecretAsync(string districtPrefix, string property);
}
```

#### 2.3 Clever API Client

**Create `ICleverApiClient` Interface**:
- `Task<CleverSchool[]> GetSchoolsAsync(string districtId)`
- `Task<CleverStudent[]> GetStudentsAsync(string schoolId, DateTime? lastModified = null)`
- `Task<CleverTeacher[]> GetTeachersAsync(string schoolId, DateTime? lastModified = null)`
- `Task<CleverApiResponse<T>> GetPagedDataAsync<T>(string endpoint, int page, int pageSize)`

**Implementation Details**:
- Use `IHttpClientFactory` with typed client
- Inject `ICleverAuthenticationService` for token management
- Base URL: `https://api.clever.com/v3.0`
- Implement pagination (100 records per page)
- Handle rate limits (HTTP 429) with exponential backoff
- Parse Clever response format: `{ data: [...], paging: {...} }`

#### 2.4 Sync Service Implementation

**Create `ISyncService` Interface**:
```csharp
public interface ISyncService
{
    Task SyncAllDistrictsAsync();
    Task SyncDistrictAsync(int districtId);
    Task SyncSchoolAsync(int schoolId, bool fullSync = false);
    Task<SyncResult> SyncStudentsAsync(int schoolId, DateTime? lastModified = null);
    Task<SyncResult> SyncTeachersAsync(int schoolId, DateTime? lastModified = null);
}
```

**Orchestration Logic**:
```csharp
public async Task SyncAllDistrictsAsync()
{
    // 1. Query SessionDb for all districts
    await using var sessionDb = _sessionDbFactory.CreateContext();
    var districts = await sessionDb.Districts.ToListAsync();

    foreach (var district in districts)
    {
        await SyncDistrictAsync(district.DistrictId);
    }
}

public async Task SyncDistrictAsync(int districtId)
{
    await using var sessionDb = _sessionDbFactory.CreateContext();

    // 2. Get all active schools in district from SessionDb
    var schools = await sessionDb.Schools
        .Where(s => s.DistrictId == districtId && s.IsActive)
        .ToListAsync();

    // 3. Sync schools in parallel (configurable concurrency limit)
    var maxConcurrent = _configuration.Value.Sync.MaxSchoolsInParallel; // Default: 5
    var semaphore = new SemaphoreSlim(maxConcurrent);
    var tasks = schools.Select(async school =>
    {
        await semaphore.WaitAsync();
        try
        {
            await SyncSchoolAsync(school.SchoolId);
        }
        finally
        {
            semaphore.Release();
        }
    });

    await Task.WhenAll(tasks);
}

public async Task SyncSchoolAsync(int schoolId, bool fullSync = false)
{
    // 4. Get school info from SessionDb
    await using var sessionDb = _sessionDbFactory.CreateContext();
    var school = await sessionDb.Schools.FindAsync(schoolId);

    // 5. Determine sync type (Full vs Incremental)
    var lastSync = await sessionDb.SyncHistory
        .Where(h => h.SchoolId == schoolId && h.Status == "Success")
        .OrderByDescending(h => h.SyncEndTime)
        .FirstOrDefaultAsync();

    // Check if full sync is required
    bool isFullSync = school.RequiresFullSync || fullSync || lastSync == null;
    var syncType = isFullSync ? SyncType.Full : SyncType.Incremental;

    // For incremental sync, determine if Events API is available
    var lastModified = isFullSync ? null : lastSync?.LastSyncTimestamp;
    var lastEventId = isFullSync ? null : lastSync?.LastEventId;

    // 6. Connect to school's dedicated database
    await using var schoolDb = await _schoolDbFactory.CreateSchoolContextAsync(school);

    // 7. Sync students and teachers to school's database
    await SyncStudentsAsync(schoolId, school, schoolDb, lastModified, syncType);
    await SyncTeachersAsync(schoolId, school, schoolDb, lastModified, syncType);

    // 8. Reset RequiresFullSync flag after successful full sync
    if (isFullSync && school.RequiresFullSync)
    {
        school.RequiresFullSync = false;
        await sessionDb.SaveChangesAsync();
    }
}

private async Task<SyncResult> SyncStudentsAsync(
    int schoolId,
    School school,
    SchoolDbContext schoolDb,
    DateTime? lastModified,
    SyncType syncType)
{
    // 8. Record sync start in SessionDb
    var syncHistory = new SyncHistory
    {
        SchoolId = schoolId,
        EntityType = "Student",
        SyncType = syncType,
        SyncStartTime = DateTime.UtcNow,
        Status = "InProgress"
    };
    await _sessionDbFactory.CreateContext().SyncHistory.AddAsync(syncHistory);
    await _sessionDbFactory.CreateContext().SaveChangesAsync();

    try
    {
        // 9. If full sync, mark all existing students as inactive (soft-delete)
        if (syncType == SyncType.Full)
        {
            var existingStudents = await schoolDb.Students.ToListAsync();
            foreach (var student in existingStudents)
            {
                student.IsActive = false;
                student.DeactivatedAt = DateTime.UtcNow;
            }
            _logger.LogInformation("Marked {Count} students as inactive for full sync", existingStudents.Count);
        }

        // 10. Fetch from Clever API
        var cleverStudents = await _cleverClient.GetStudentsAsync(
            school.CleverSchoolId,
            lastModified);

        // 11. Upsert to school's database
        // For full sync: this will reactivate students still in Clever
        // For incremental sync: this will insert new or update existing
        foreach (var cleverStudent in cleverStudents)
        {
            await UpsertStudentAsync(schoolDb, cleverStudent, isActive: true);
        }

        await schoolDb.SaveChangesAsync();

        // 12. Hard-delete inactive students (only for full sync - beginning of year cleanup)
        var deletedCount = 0;
        if (syncType == SyncType.Full)
        {
            var inactiveStudents = await schoolDb.Students
                .Where(s => !s.IsActive)
                .ToListAsync();

            deletedCount = inactiveStudents.Count;

            if (deletedCount > 0)
            {
                schoolDb.Students.RemoveRange(inactiveStudents);
                await schoolDb.SaveChangesAsync();
                _logger.LogInformation("Full sync: Permanently deleted {DeletedCount} students (graduated/transferred)", deletedCount);
            }
        }

        // 13. Update sync history in SessionDb
        syncHistory.SyncEndTime = DateTime.UtcNow;
        syncHistory.Status = "Success";
        syncHistory.RecordsProcessed = cleverStudents.Length;
        syncHistory.LastSyncTimestamp = DateTime.UtcNow;
        await _sessionDbFactory.CreateContext().SaveChangesAsync();

        return new SyncResult
        {
            Success = true,
            RecordsProcessed = cleverStudents.Length,
            DeletedRecords = deletedCount
        };
    }
    catch (Exception ex)
    {
        // 14. Record failure in SessionDb
        syncHistory.SyncEndTime = DateTime.UtcNow;
        syncHistory.Status = "Failed";
        syncHistory.ErrorMessage = ex.Message;
        await _sessionDbFactory.CreateContext().SaveChangesAsync();

        _logger.LogError(ex, "Failed to sync students for school {SchoolId}", schoolId);
        throw;
    }
}
```

#### 2.5 Data Mapping and Upsert Logic

```csharp
private async Task<bool> UpsertStudentAsync(SchoolDbContext schoolDb, CleverStudent cleverStudent, bool isActive = true)
{
    var existing = await schoolDb.Students
        .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudent.Id);

    bool hasChanges = false;

    if (existing != null)
    {
        // Check if any fields actually changed using string normalization
        // Treats null, empty string, and whitespace as equivalent
        var firstNameChanged = !StringsEqual(existing.FirstName, cleverStudent.Name.First);
        var lastNameChanged = !StringsEqual(existing.LastName, cleverStudent.Name.Last);
        var emailChanged = !StringsEqual(existing.Email, cleverStudent.Email);
        var gradeChanged = !StringsEqual(existing.Grade, cleverStudent.Grade);
        var studentNumberChanged = !StringsEqual(existing.StudentNumber, cleverStudent.StudentNumber);
        var isInactive = !existing.IsActive;
        var hasDeactivationDate = existing.DeactivatedAt != null;

        if (firstNameChanged || lastNameChanged || emailChanged || gradeChanged ||
            studentNumberChanged || isInactive || hasDeactivationDate)
        {
            // Update existing record in school's database only if changed
            existing.FirstName = cleverStudent.Name.First;
            existing.LastName = cleverStudent.Name.Last;
            existing.Email = cleverStudent.Email;
            existing.Grade = cleverStudent.Grade;
            existing.StudentNumber = cleverStudent.StudentNumber;
            existing.LastModifiedInClever = cleverStudent.LastModified;
            existing.IsActive = isActive;  // Reactivate during full sync
            existing.DeactivatedAt = isActive ? null : existing.DeactivatedAt;  // Clear deactivation if reactivated
            existing.UpdatedAt = DateTime.UtcNow;
            hasChanges = true;
        }
    }
    else
    {
        // Insert new record into school's database
        schoolDb.Students.Add(new Student
        {
            CleverStudentId = cleverStudent.Id,
            FirstName = cleverStudent.Name.First,
            LastName = cleverStudent.Name.Last,
            Email = cleverStudent.Email,
            Grade = cleverStudent.Grade,
            StudentNumber = cleverStudent.StudentNumber,
            LastModifiedInClever = cleverStudent.LastModified,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        hasChanges = true;
    }

    if (hasChanges)
    {
        await schoolDb.SaveChangesAsync();
    }

    return hasChanges; // Return true if record was inserted or updated
}

// String comparison helper - normalizes null, empty, and whitespace
private static bool StringsEqual(string? a, string? b)
{
    var normalizedA = string.IsNullOrWhiteSpace(a) ? null : a.Trim();
    var normalizedB = string.IsNullOrWhiteSpace(b) ? null : b.Trim();
    return normalizedA == normalizedB;
}
```

**Sync Scenarios Handled:**

1. **New School (First-Time Sync)**:
   - No SyncHistory → triggers Full Sync
   - All students inserted with `IsActive = true`
   - No deletions (database is empty)

2. **Beginning-of-Year (Full Sync)**:
   - Administrator sets `School.RequiresFullSync = true`
   - Step 1: Mark all existing students as `IsActive = false`, `DeactivatedAt = NOW()`
   - Step 2: **Clear EF change tracker** to prevent cached inactive entities from affecting upserts
   - Step 3: Fetch ALL students from Clever (no `last_modified` filter)
   - Step 4: Upsert students in Clever (`IsActive = true`, `DeactivatedAt = null`)
   - Step 5: **Hard-delete** students that remain `IsActive = false` (graduated/transferred)
   - Step 6: **Establish baseline event ID** from Clever Events API for future incremental syncs
   - Provides clean database at start of school year

3. **During School Year (Incremental Sync)**:
   - `SyncType = Incremental`
   - **Primary**: Uses Clever Events API with `lastEventId` if available (only changed records)
   - **Fallback**: Uses data API with `last_modified` parameter if Events API unavailable
   - Change detection with `StringsEqual` prevents unnecessary updates
   - Only records with actual data changes count as "updated"
   - New students inserted, existing students updated
   - No deletion logic (students leaving handled by next full sync at beginning of year)

#### 2.6 Azure Function Configuration

**Timer Trigger** (Daily Sync):
```csharp
[Function("SyncTimerFunction")]
public async Task Run(
    [TimerTrigger("%CleverSync:Schedule:CronExpression%")] TimerInfo timer,
    FunctionContext context)
{
    _logger.LogInformation("Starting scheduled sync at {Time} (Schedule: {Cron})",
        DateTime.UtcNow,
        _configuration.Value.Schedule.CronExpression);
    await _syncService.SyncAllDistrictsAsync();
}
```

**Manual Trigger Endpoint**:
```csharp
[Function("ManualSyncFunction")]
public async Task<HttpResponseData> RunManual(
    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
{
    var query = ParseQueryString(req.Url.Query);

    if (query.TryGetValue("schoolId", out var schoolId))
        await _syncService.SyncSchoolAsync(int.Parse(schoolId));
    else if (query.TryGetValue("districtId", out var districtId))
        await _syncService.SyncDistrictAsync(int.Parse(districtId));
    else
        await _syncService.SyncAllDistrictsAsync();

    return req.CreateResponse(HttpStatusCode.OK);
}
```

#### 2.7 Error Handling and Resilience

**Polly Retry Policies**:
- Database operations: 3 retries, exponential backoff (1s, 2s, 4s)
- Clever API calls: 5 retries, exponential backoff (2s, 4s, 8s, 16s, 32s)
- Per-school isolation: Failure in one school doesn't affect others

**Monitoring Strategy**:
- Log all operations to Application Insights
- Alert on 3+ consecutive failures for same school
- Track sync duration and record counts per school
- Monitor Key Vault access patterns

### Stage 3: Health Check Endpoints [Phase: Health & Observability]

**Goal**: Implement monitoring and health check infrastructure

**Scope**:

- Health check endpoints
- Application Insights integration
- Azure Monitor alerting
- Health status reporting
- Operational metrics and telemetry
- Graceful degradation handling

---

## Constitution Alignment

✅ Fully aligned with CleverSyncSOS Constitution v1.1.0

| Principle | Implementation |
| --- | --- |
| Security First | Managed identity + Key Vault; no secrets in config |
| Scalability | Token refresh and retry logic support multi-school deployments |
| Isolation | Auth service is modular and scoped per tenant |
| Observability | Structured logging + health check endpoint + Application Insights |
| Configurability | Retry intervals, timeouts, and endpoints externally configurable |
| Compatibility | Handles Clever API versioning and rate limits gracefully |

* * *

## Operational Goals (from Constitution)

- ✅ **[Stage 1]** Authenticate within 5 seconds of startup
- ✅ **[Stage 1]** Token refresh without request failures
- ✅ **[Stage 3]** Health check response \< 100ms
- ✅ **[Stage 1]** Zero credential leaks in logs/telemetry
- ✅ **[Stage 3]** Graceful degradation when Key Vault unavailable
- ✅ **[Stage 3]** 99.9% health check accuracy

* * *

## Technical Context

**Language/Version**: C# / .NET 9  
**Primary Dependencies**:

- Azure.Security.KeyVault.Secrets
- Azure.Identity
- Microsoft.Extensions.Http
- Microsoft.Extensions.Options
- Polly
- Microsoft.ApplicationInsights

**Storage**: Azure Key Vault (credentials), in-memory caching (tokens)  
**Testing**: xUnit with Moq; Azure SDK integration tests  
**Target Platform**: Azure App Service or Azure Functions  
**Project Type**: Library/service component within CleverSyncSOS solution

* * *

## Configuration Management

All operational parameters must be externally configurable per Constitution Principle 5 (Configurability). Configuration is managed via Azure Function App settings and Azure Key Vault with the following structure:

**Configuration Strategy**:
- **Function App Settings**: Non-sensitive operational parameters (schedules, timeouts, retry counts)
- **Azure Key Vault**: Sensitive values (connection strings, API credentials)
- **Access Pattern**: Standard .NET `IConfiguration` with Key Vault integration via managed identity

### Required Configuration Parameters

**Authentication & Security**:
```json
{
  "CleverSync:TokenRefreshThreshold": 0.75,
  "CleverSync:KeyVault:BaseUrl": "https://{vault-name}.vault.azure.net/",
  "CleverSync:KeyVault:RetryAttempts": 3,
  "CleverSync:KeyVault:RetryDelaySeconds": 2
}
```

**Synchronization**:
```json
{
  "CleverSync:Schedule:CronExpression": "0 0 2 * * *",
  "CleverSync:Concurrency:MaxSchoolsInParallel": 5,
  "CleverSync:Sync:DefaultMode": "Incremental",
  "CleverSync:Sync:FullSyncDay": "Sunday",
  "CleverSync:Sync:TimeoutMinutes": 30,
  "CleverSync:Sync:PageSize": 100
}
```

**Retry Policies**:
```json
{
  "CleverSync:Retry:CleverApi:MaxAttempts": 5,
  "CleverSync:Retry:CleverApi:BackoffBase": 2,
  "CleverSync:Retry:Database:MaxAttempts": 3,
  "CleverSync:Retry:Database:BackoffBase": 1,
  "CleverSync:Retry:RateLimitRespectRetryAfter": true
}
```

**Health & Monitoring**:
```json
{
  "CleverSync:Health:CacheDurationSeconds": 30,
  "CleverSync:Health:TimeoutMilliseconds": 100,
  "CleverSync:Health:IncludeDependencyChecks": true,
  "CleverSync:Monitoring:AlertAfterConsecutiveFailures": 3,
  "CleverSync:Monitoring:MetricFlushIntervalSeconds": 60
}
```

**Data Retention**:
```json
{
  "CleverSync:History:RetentionDays": 90,
  "CleverSync:Logging:LogLevel": "Information",
  "CleverSync:Logging:SanitizeSecrets": true
}
```

### Configuration Access Pattern

```csharp
public class CleverSyncConfiguration
{
    public AuthenticationSettings Authentication { get; set; }
    public SyncSettings Sync { get; set; }
    public RetrySettings Retry { get; set; }
    public HealthSettings Health { get; set; }
    public MonitoringSettings Monitoring { get; set; }
}

// Registered in DI container
services.Configure<CleverSyncConfiguration>(
    configuration.GetSection("CleverSync"));
```

### Environment-Specific Overrides

| Environment | Override Examples |
|-------------|-------------------|
| **Development** | `MaxSchoolsInParallel: 2`, `LogLevel: Debug` |
| **Staging** | `CronExpression: "0 0 3 * * *"` (3 AM), `AlertAfterConsecutiveFailures: 1` |
| **Production** | `MaxSchoolsInParallel: 10`, `LogLevel: Information` |

**Configuration Validation**:
- All configuration validated at startup using `IValidateOptions<T>`
- Missing required values cause startup failure with descriptive error
- Invalid values (e.g., negative retry counts) logged and rejected
- Configuration changes logged to Application Insights

* * *

## Development Standards (from Constitution)

- ✅ .NET coding conventions and async patterns
- ✅ Dependency injection for all services
- ✅ Configuration via Azure App Configuration or secure settings
- ✅ **EF Core for all data access** (SessionDb orchestration + per-school databases)
- ✅ Structured logging via ILogger with Application Insights
- ✅ Automated tests for core logic and error handling
- ✅ Configurable retry behavior and sync intervals
- ✅ API versioning and rate limit handling
- ✅ Separate DbContext migrations for SessionDb vs School databases

* * *

## CI/CD Roadmap

- CI pipeline will validate build, test, and security scan
- CD pipeline will deploy to Azure Functions or App Service
- Health check endpoint will be monitored post-deployment
- Logs and telemetry will be reviewed weekly for anomalies

* * *

## Project Structure

### Documentation

```
specs/001-clever-api-auth/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md (to be generated)

```

### Source Code

```
src/
├── CleverSyncSOS.Core/
│   ├── Authentication/
│   ├── Configuration/
│   └── Health/
└── CleverSyncSOS.Infrastructure/
    └── Extensions/

tests/
├── CleverSyncSOS.Core.Tests/
├── CleverSyncSOS.Integration.Tests/

```

* * *

## Phase 0: Research & Technical Decisions

All technical unknowns resolved via constitution and spec. Documented in `research.md`.

* * *

## Phase 1: Data Model & Contracts

### Data Model (`data-model.md`)

**Authentication Models** (Stage 1):
- `CleverAuthConfiguration` - OAuth settings and endpoints
- `CleverAuthToken` - Token metadata and expiration tracking
- `CredentialReference` - Key Vault secret references

**Database Models** (Stage 2):
See `SpecKit/DataModel/data-model.md` for complete schema definitions:

**SessionDb (Orchestration Database)**:
- `District` - Multi-district metadata and Clever app credentials
- `School` - Per-school configuration and database references
- `SyncHistory` - Audit trail for all sync operations

**SchoolDb (Per-School Data)**:
- `Student` - Student demographic and enrollment data
- `Teacher` - Teacher profile and contact information
- `Section` - Class/course assignments (future)

**Key Schema Characteristics**:
- All entities use `CleverXxxId` fields for idempotency
- `LastModifiedInClever` timestamp enables incremental sync
- `IsActive` flag supports soft-delete workflow
- EF Core conventions: `CreatedAt`, `UpdatedAt`, identity PKs

For detailed column definitions, constraints, and indexes, see:
- `SpecKit/DataModel/data-model.md` (logical model)
- `src/CleverSyncSOS.Data/Migrations/` (EF Core migrations)

**Health Models** (Stage 3):
- `AuthenticationHealthStatus` - Health check response model
- `DependencyHealthResult` - Per-dependency status tracking

### Contracts (`contracts/`)

- **[Stage 3]** Health Check Endpoint: `GET /health/clever-auth`
- **[Stage 1]** Internal Interfaces: `ICleverAuthenticationService`, `ICredentialStore`

* * *

## Quickstart (`quickstart.md`)

1. **[Stage 1]** Set up Azure Key Vault and store credentials
2. **[Stage 1]** Configure managed identity for the app
3. **[Stage 1]** Register services in DI container
4. **[Stage 3]** Verify health check endpoint
5. **[Stage 1]** Test credential refresh and error scenarios

* * *

## Implementation Notes

### Key Technical Decisions

- **[Stage 1]** Polly for exponential backoff
- **[Stage 1]** IHttpClientFactory with typed clients
- **[Stage 1]** Background token refresh via IHostedService
- **[Stage 3]** Graceful startup without Key Vault
- **[Stage 1]** Structured logging with queryable properties
- **[Stage 3]** ASP.NET Core health check middleware

### Database Architecture Decisions

**EF Core Context Strategy**:
- **SessionDbContext**: Single context for orchestration database
  - Entities: `District`, `School`, `SyncHistory`
  - Connection: Static connection string from Key Vault or App Config
  - Migration path: `src/CleverSyncSOS.Data/Migrations/Session/`

- **SchoolDbContext**: Dynamic context instantiation per school
  - Entities: `Student`, `Teacher`, `Section`
  - Connection: Retrieved dynamically from Key Vault using `School.KeyVaultConnectionStringSecretName`
  - Migration path: `src/CleverSyncSOS.Data/Migrations/School/`
  - Factory pattern: `ISchoolDbContextFactory.CreateContextAsync(School school)`

**Migration Management**:
```bash
# SessionDb migrations (one-time setup)
dotnet ef migrations add InitialCreate --context SessionDbContext --output-dir Migrations/Session
dotnet ef database update --context SessionDbContext

# SchoolDb migrations (applied to each school database)
dotnet ef migrations add InitialCreate --context SchoolDbContext --output-dir Migrations/School
# Applied programmatically via: context.Database.MigrateAsync()
```

**Connection String Management**:
- SessionDb: `CleverSyncSOS--SessionDb--ConnectionString` (Key Vault)
- School DBs: `CleverSyncSOS--{SchoolPrefix}--ConnectionString` (Key Vault)
  - Secret names dynamically constructed using `School.SchoolPrefix` field
  - Example: `CleverSyncSOS--Lincoln-Elementary--ConnectionString`
- All secrets follow standardized naming: `CleverSyncSOS--{Component}--{Property}`
- Connection pooling managed by EF Core (default pool size: 100)
- Deprecated field: `School.KeyVaultConnectionStringSecretName` (replaced by `School.SchoolPrefix`)

**Transaction Boundaries**:
- Each school sync runs in separate transaction scope
- Batch size: 100 records (configurable: `CleverSync:Sync:BatchSize`)
- Rollback on error preserves partial sync state
- SyncHistory updated outside school transaction for audit integrity

### Security Considerations

- **[Stage 1]** No secrets in config or code
- **[Stage 1]** Key Vault access audit trail
- **[Stage 1]** In-memory token caching
- **[Stage 1]** TLS 1.2+ enforced
- **[Stage 1]** Log sanitization

### Testing Strategy

**Unit Tests** (xUnit + Moq):
- Authentication service with mocked Key Vault
- Token refresh logic with simulated expiration
- Sync orchestration with mocked Clever API
- Data mapping and upsert logic
- Configuration validation
- Error handling and retry policies

**Integration Tests** (Azure SDK TestServer):
- End-to-end authentication with test Key Vault
- EF Core migrations against LocalDB/SQL Server
- SessionDb and SchoolDb context creation
- Clever API client with recorded responses (VCR pattern)
- Health check endpoint response times

**Acceptance Tests** (SpecFlow - Optional):
- User story scenarios from spec.md
- Full sync workflow
- Incremental sync workflow
- Multi-school orchestration
- Error recovery scenarios

**Performance Tests** (BenchmarkDotNet):
- Token refresh throughput
- 10,000 student upsert benchmark
- Health check response time < 100ms
- Concurrent school sync scalability

**Test Data**:
- Mock Clever API responses in `tests/CleverSyncSOS.Tests/MockData/`
- Test databases created/destroyed per test run
- Faker library for generating test students/teachers

### Performance Optimizations

- **[Stage 1]** Cached tokens
- **[Stage 1]** Proactive refresh at 75% lifetime
- **[Stage 2]** HTTP connection pooling
- **[Stage 3]** Cached health status (configurable cache duration, default: 30s)

* * *
