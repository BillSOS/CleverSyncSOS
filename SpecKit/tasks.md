---
speckit:
  type: tasklist
  title: Clever API Authentication and Connection - Implementation Tasks
  version: 2.0.0
  branch: 001-clever-api-auth
  linked_spec: SpecKit/spec.md
  linked_plan: SpecKit/plan.md
---

# Implementation Task List

**Total Tasks**: 44
**Estimated Duration**: 12-15 days
**Dependencies**: Constitution v1.1.0, .NET 9 SDK, Azure subscription

---

## Legend

- `[P]` = Can run in parallel with other [P] tasks in same phase
- `→ TaskID` = Depends on TaskID completing first
- `FR-XXX` = Maps to Functional Requirement in spec.md
- `NFR-XXX` = Maps to Non-Functional Requirement in spec.md

---

## Phase 0: Project Setup (Day 1)

### T001: Create Azure Function Project [P]
**Requirements**: FR-001, NFR-001
**Files Created**:
- `src/CleverSyncSOS.Functions/CleverSyncSOS.Functions.csproj`
- `src/CleverSyncSOS.Functions/Program.cs`
- `src/CleverSyncSOS.Functions/host.json`
- `src/CleverSyncSOS.Functions/local.settings.json`

**Acceptance Criteria**:
- [ ] .NET 9 isolated process Azure Function project created
- [ ] `dotnet build` succeeds with zero warnings
- [ ] Function app runs locally with `func start`
- [ ] Health endpoint returns 200 OK at `http://localhost:7071/api/health`

**Commands**:
```bash
dotnet new func -n CleverSyncSOS.Functions --worker-runtime dotnet-isolated
cd src/CleverSyncSOS.Functions
dotnet add package Microsoft.Azure.Functions.Worker.Extensions.Http
```

---

### T002: Create Core Library Project [P]
**Requirements**: FR-001, FR-002
**Files Created**:
- `src/CleverSyncSOS.Core/CleverSyncSOS.Core.csproj`
- `src/CleverSyncSOS.Core/Authentication/ICleverAuthenticationService.cs`
- `src/CleverSyncSOS.Core/Configuration/CleverSyncConfiguration.cs`

**Acceptance Criteria**:
- [ ] Class library project targeting .NET 9
- [ ] Reference added from Functions project to Core project
- [ ] NuGet packages installed: `Azure.Security.KeyVault.Secrets`, `Azure.Identity`
- [ ] Solution builds successfully

---

### T003: Create Test Projects [P]
**Requirements**: NFR-001 (Testing)
**Files Created**:
- `tests/CleverSyncSOS.Core.Tests/CleverSyncSOS.Core.Tests.csproj`
- `tests/CleverSyncSOS.Integration.Tests/CleverSyncSOS.Integration.Tests.csproj`
- `tests/CleverSyncSOS.Core.Tests/Usings.cs`

**Acceptance Criteria**:
- [ ] xUnit test projects created
- [ ] Packages: `xunit`, `Moq`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`
- [ ] `dotnet test` runs successfully (0 tests initially)
- [ ] Test projects reference Core and Functions projects

---

### T004: Configure Azure Resources
**Requirements**: FR-002, NFR-002
**Azure Resources**:
- Azure Key Vault: `kv-cleversync-dev`
- Managed Identity: Enabled on Function App
- Application Insights: `ai-cleversync-dev`

**Acceptance Criteria**:
- [ ] Key Vault created with RBAC enabled
- [ ] Managed identity granted "Key Vault Secrets User" role
- [ ] Application Insights instrumentation key stored
- [ ] Test secret created and retrievable: `CleverSyncSOS--Test--Value`

**Commands**:
```bash
az keyvault create --name kv-cleversync-dev --resource-group rg-cleversync
az keyvault set-policy --name kv-cleversync-dev --object-id {managed-identity-id} --secret-permissions get list
```

---

## Phase 1: Authentication Infrastructure (Days 2-3)

### T005: Implement Configuration Models → T002
**Requirements**: FR-001, NFR-006
**Files Modified**:
- `src/CleverSyncSOS.Core/Configuration/CleverSyncConfiguration.cs`
- `src/CleverSyncSOS.Core/Configuration/AuthenticationSettings.cs`
- `src/CleverSyncSOS.Core/Configuration/RetrySettings.cs`

**Acceptance Criteria**:
- [ ] Configuration classes match plan.md Configuration Management section
- [ ] Data annotations for validation: `[Required]`, `[Range(1, 10)]`
- [ ] `IValidateOptions<CleverSyncConfiguration>` implemented
- [ ] Unit test: Invalid config throws `OptionsValidationException`

**Code Reference**:
```csharp
public class AuthenticationSettings
{
    [Range(0.5, 0.95)]
    public double TokenRefreshThreshold { get; set; } = 0.75;

    [Required, Url]
    public string KeyVaultBaseUrl { get; set; }
}
```

---

### T006: Implement Credential Store (Key Vault) → T005
**Requirements**: FR-002, NFR-002
**Files Created**:
- `src/CleverSyncSOS.Core/Authentication/ICredentialStore.cs`
- `src/CleverSyncSOS.Core/Authentication/AzureKeyVaultCredentialStore.cs`

**Acceptance Criteria**:
- [ ] Interface: `Task<string> GetSecretAsync(string secretName)`
- [ ] Implementation uses `SecretClient` with `DefaultAzureCredential`
- [ ] Secrets cached in-memory for 5 minutes (`MemoryCache`)
- [ ] Unit test: Mock `SecretClient` returns expected values
- [ ] Integration test: Retrieve real secret from test Key Vault

**Code Reference**:
```csharp
public interface ICredentialStore
{
    Task<string> GetSecretAsync(string secretName, CancellationToken ct = default);
    Task<T> GetSecretAsync<T>(string secretName, CancellationToken ct = default) where T : class;
}
```

---

### T007: Implement Clever Authentication Service → T006
**Requirements**: FR-001, FR-003, NFR-001
**Files Created**:
- `src/CleverSyncSOS.Core/Authentication/CleverAuthenticationService.cs`
- `src/CleverSyncSOS.Core/Models/CleverAuthToken.cs`

**Acceptance Criteria**:
- [ ] Method: `Task<CleverAuthToken> AuthenticateAsync(string districtId)`
- [ ] OAuth 2.0 client credentials flow implementation
- [ ] POST to `https://clever.com/oauth/tokens` with Basic Auth
- [ ] Returns bearer token with expiration timestamp
- [ ] Unit test: Mock HTTP response validates token parsing
- [ ] Completes in < 5 seconds (NFR-001)

---

### T008: Implement Token Lifecycle Manager → T007
**Requirements**: FR-003, NFR-001
**Files Created**:
- `src/CleverSyncSOS.Core/Authentication/TokenLifecycleManager.cs`
- `src/CleverSyncSOS.Core/Authentication/ITokenCache.cs`

**Acceptance Criteria**:
- [ ] In-memory token cache (ConcurrentDictionary per district)
- [ ] Background refresh at 75% of token lifetime (configurable)
- [ ] Method: `Task<string> GetValidTokenAsync(string districtId)`
- [ ] Auto-refresh if token expired or approaching expiration
- [ ] Unit test: Token refreshed at 75% lifetime (mock timer)
- [ ] Unit test: Zero request failures due to expired tokens

---

### T009: Implement Retry Policy with Polly → T007 [P]
**Requirements**: FR-010, NFR-004
**Files Created**:
- `src/CleverSyncSOS.Core/Resilience/RetryPolicyFactory.cs`

**Acceptance Criteria**:
- [ ] Polly NuGet package installed
- [ ] Clever API policy: 5 retries, exponential backoff (2s, 4s, 8s, 16s, 32s)
- [ ] Database policy: 3 retries, exponential backoff (1s, 2s, 4s)
- [ ] HTTP 429 respects `Retry-After` header
- [ ] Unit test: Retry count and delays validated
- [ ] Configuration-driven retry settings

---

### T010: Implement Structured Logging Sanitizer → T002 [P]
**Requirements**: FR-013, NFR-002
**Files Created**:
- `src/CleverSyncSOS.Core/Logging/SecretSanitizer.cs`
- `src/CleverSyncSOS.Core/Logging/SanitizingLoggerProvider.cs`

**Acceptance Criteria**:
- [ ] Regex patterns redact: client secrets, tokens, connection strings, passwords
- [ ] Redacted format: `[REDACTED:ClientSecret]`
- [ ] Unit test: Sample log with secret returns sanitized output
- [ ] Integration test: Verify Application Insights logs don't contain secrets
- [ ] Performance: < 1ms overhead per log entry

---

### T011: Register Services in DI Container → T007, T008, T010
**Requirements**: FR-001, NFR-001
**Files Modified**:
- `src/CleverSyncSOS.Functions/Program.cs`

**Acceptance Criteria**:
- [ ] All services registered with appropriate lifetimes (Singleton, Scoped)
- [ ] Configuration bound: `services.Configure<CleverSyncConfiguration>(config.GetSection("CleverSync"))`
- [ ] HTTP client factory configured with retry policies
- [ ] Application Insights telemetry processor registered
- [ ] Function starts without DI resolution errors

---

### T012: Write Authentication Integration Tests → T011
**Requirements**: NFR-001, NFR-004
**Files Created**:
- `tests/CleverSyncSOS.Integration.Tests/Authentication/CleverAuthenticationTests.cs`

**Acceptance Criteria**:
- [ ] Test: Authenticate with real Clever API (test credentials)
- [ ] Test: Token refresh succeeds before expiration
- [ ] Test: Key Vault retrieval with managed identity (or test credential)
- [ ] Test: Authentication completes in < 5 seconds (NFR-001)
- [ ] Test: Retry succeeds on transient failure (mock flaky endpoint)
- [ ] All tests pass in CI/CD pipeline

---

## Phase 2: Database Infrastructure (Days 4-5)

### T013: Create Data Model Project [P]
**Requirements**: FR-004, FR-005
**Files Created**:
- `src/CleverSyncSOS.Data/CleverSyncSOS.Data.csproj`
- `src/CleverSyncSOS.Data/Entities/District.cs`
- `src/CleverSyncSOS.Data/Entities/School.cs`
- `src/CleverSyncSOS.Data/Entities/SyncHistory.cs`
- `src/CleverSyncSOS.Data/Entities/Student.cs`
- `src/CleverSyncSOS.Data/Entities/Teacher.cs`

**Acceptance Criteria**:
- [ ] EF Core packages: `Microsoft.EntityFrameworkCore.SqlServer`, `.Design`
- [ ] Entity classes match schemas in data-model.md
- [ ] Data annotations: `[Required]`, `[MaxLength]`, `[Index]`
- [ ] All entities have `CreatedAt`, `UpdatedAt` timestamps
- [ ] Clever IDs are unique indexed columns

---

### T014: Create SessionDbContext → T013
**Requirements**: FR-004, FR-008
**Files Created**:
- `src/CleverSyncSOS.Data/SessionDbContext.cs`

**Acceptance Criteria**:
- [ ] DbSets: `Districts`, `Schools`, `SyncHistory`
- [ ] Fluent API configuration in `OnModelCreating`
- [ ] Unique constraints on Clever IDs
- [ ] Foreign key relationships configured
- [ ] Connection string from configuration or Key Vault

---

### T015: Create SchoolDbContext → T013
**Requirements**: FR-004, FR-005
**Files Created**:
- `src/CleverSyncSOS.Data/SchoolDbContext.cs`

**Acceptance Criteria**:
- [ ] DbSets: `Students`, `Teachers`, `Sections` (future)
- [ ] Unique constraints on Clever IDs
- [ ] Soft-delete query filter: `modelBuilder.Entity<Student>().HasQueryFilter(s => s.IsActive)`
- [ ] Timestamps auto-updated via `SaveChangesAsync` override

---

### T016: Create EF Core Migrations → T014, T015
**Requirements**: FR-004
**Files Created**:
- `src/CleverSyncSOS.Data/Migrations/Session/20251118_InitialCreate.cs`
- `src/CleverSyncSOS.Data/Migrations/School/20251118_InitialCreate.cs`

**Acceptance Criteria**:
- [ ] SessionDb migration creates Districts, Schools, SyncHistory tables
- [ ] SchoolDb migration creates Students, Teachers tables
- [ ] Migrations include indexes, constraints, default values
- [ ] `dotnet ef migrations add InitialCreate --context SessionDbContext` succeeds
- [ ] Apply migration to test database: tables created successfully

**Commands**:
```bash
dotnet ef migrations add InitialCreate --context SessionDbContext --output-dir Migrations/Session
dotnet ef migrations add InitialCreate --context SchoolDbContext --output-dir Migrations/School
dotnet ef database update --context SessionDbContext
```

---

### T017: Implement School Database Factory → T015
**Requirements**: FR-004, NFR-003
**Files Created**:
- `src/CleverSyncSOS.Data/SchoolDbContextFactory.cs`

**Acceptance Criteria**:
- [ ] Interface: `ISchoolDbContextFactory`
- [ ] Method: `Task<SchoolDbContext> CreateContextAsync(School school)`
- [ ] Retrieves connection string from Key Vault using `school.KeyVaultConnectionStringSecretName`
- [ ] Connection string cached for 10 minutes
- [ ] Unit test: Mock Key Vault returns connection, context created
- [ ] Integration test: Create context for real school database

---

## Phase 3: Clever API Client (Day 6)

### T018: Define Clever API Response Models [P]
**Requirements**: FR-005
**Files Created**:
- `src/CleverSyncSOS.Core/Models/CleverStudent.cs`
- `src/CleverSyncSOS.Core/Models/CleverTeacher.cs`
- `src/CleverSyncSOS.Core/Models/CleverApiResponse.cs`

**Acceptance Criteria**:
- [ ] Models match Clever API v3.0 JSON structure
- [ ] JSON property names: `[JsonPropertyName("data")]`
- [ ] Nested name object: `FirstName`, `LastName` → `Name.First`, `Name.Last`
- [ ] Pagination metadata: `Links`, `Paging` properties
- [ ] Unit test: Deserialize sample Clever JSON response

---

### T019: Implement Clever API Client → T018, T009
**Requirements**: FR-005, FR-010
**Files Created**:
- `src/CleverSyncSOS.Core/Clever/ICleverApiClient.cs`
- `src/CleverSyncSOS.Core/Clever/CleverApiClient.cs`

**Acceptance Criteria**:
- [ ] Methods: `GetStudentsAsync`, `GetTeachersAsync`, `GetSchoolsAsync`
- [ ] Base URL: `https://api.clever.com/v3.0`
- [ ] Bearer token from `ITokenLifecycleManager`
- [ ] Pagination: Iterate `next` link until null
- [ ] Rate limiting: Respect HTTP 429 with Polly policy
- [ ] Unit test: Mock HTTP responses with pagination
- [ ] Integration test: Fetch real data from Clever test environment

---

### T020: Implement Pagination Handler → T019
**Requirements**: FR-005
**Files Modified**:
- `src/CleverSyncSOS.Core/Clever/CleverApiClient.cs`

**Acceptance Criteria**:
- [ ] Method: `Task<T[]> GetPagedDataAsync<T>(string url, string token)`
- [ ] Follows `links.next` until null
- [ ] Page size: 100 (configurable via `CleverSync:Sync:PageSize`)
- [ ] Accumulates all records across pages
- [ ] Unit test: Mock 3 pages (300 records) returns all records
- [ ] Performance: < 10 seconds for 1000 records

---

## Phase 4: Sync Service Implementation (Days 7-9)

### T021: Implement Data Mapper → T018
**Requirements**: FR-005
**Files Created**:
- `src/CleverSyncSOS.Core/Sync/CleverDataMapper.cs`

**Acceptance Criteria**:
- [ ] Method: `Student MapToStudent(CleverStudent cleverStudent)`
- [ ] Method: `Teacher MapToTeacher(CleverTeacher cleverTeacher)`
- [ ] Handles null/missing fields gracefully (optional email, grade)
- [ ] Preserves `LastModifiedInClever` timestamp
- [ ] Unit test: Map sample Clever response to entity
- [ ] Unit test: Null email doesn't throw exception

---

### T022: Implement Upsert Logic → T021, T015
**Requirements**: FR-005, FR-006, FR-007
**Files Created**:
- `src/CleverSyncSOS.Core/Sync/DataUpsertService.cs`

**Acceptance Criteria**:
- [ ] Method: `Task UpsertStudentAsync(SchoolDbContext db, CleverStudent cleverStudent, bool isActive)`
- [ ] Checks if student exists by `CleverStudentId`
- [ ] Updates existing record: all fields + `UpdatedAt`
- [ ] Inserts new record if not found
- [ ] Batch size: 100 (configurable), commit per batch
- [ ] Unit test: Upsert existing student updates fields
- [ ] Unit test: Upsert new student inserts record

---

### T023: Implement Full Sync Logic → T022
**Requirements**: FR-006
**Files Created**:
- `src/CleverSyncSOS.Core/Sync/FullSyncService.cs`

**Acceptance Criteria**:
- [ ] Step 1: Mark all existing students `IsActive = false`, `DeactivatedAt = NOW()`
- [ ] Step 2: Fetch all students from Clever (no `last_modified` filter)
- [ ] Step 3: Upsert students (reactivates if still in Clever)
- [ ] Step 4: Hard-delete students still `IsActive = false`
- [ ] Log: Record counts (deactivated, reactivated, deleted)
- [ ] Unit test: Full sync with 10 existing, 8 in Clever → 2 deleted
- [ ] Integration test: End-to-end full sync with test database

---

### T024: Implement Incremental Sync Logic → T022
**Requirements**: FR-007
**Files Created**:
- `src/CleverSyncSOS.Core/Sync/IncrementalSyncService.cs`

**Acceptance Criteria**:
- [ ] Retrieves `LastSyncTimestamp` from most recent successful SyncHistory
- [ ] Fetches students with `last_modified >= LastSyncTimestamp`
- [ ] Upserts only changed records (no deletions)
- [ ] Logs: Record count and timestamp range
- [ ] Unit test: Incremental sync with last timestamp fetches correct records
- [ ] Integration test: Two incremental syncs with time gap

---

### T025: Implement Sync Orchestration Service → T023, T024
**Requirements**: FR-009, NFR-003
**Files Created**:
- `src/CleverSyncSOS.Core/Sync/SyncOrchestrationService.cs`

**Acceptance Criteria**:
- [ ] Method: `Task SyncAllDistrictsAsync()`
- [ ] Method: `Task SyncDistrictAsync(int districtId)`
- [ ] Method: `Task SyncSchoolAsync(int schoolId, bool fullSync = false)`
- [ ] Concurrency: `SemaphoreSlim(MaxSchoolsInParallel)` from config
- [ ] Per-school failures don't block other schools
- [ ] Sync history recorded before/after each school
- [ ] Unit test: Mock 10 schools, verify 5 concurrent max
- [ ] Integration test: Sync 3 schools, verify all complete

---

### T026: Implement Sync History Tracking → T025
**Requirements**: FR-014
**Files Modified**:
- `src/CleverSyncSOS.Core/Sync/SyncOrchestrationService.cs`

**Acceptance Criteria**:
- [ ] Record `SyncHistory` before starting sync (Status: InProgress)
- [ ] Update on success: `Status = Success`, `RecordsProcessed`, `LastSyncTimestamp`
- [ ] Update on failure: `Status = Failed`, `ErrorMessage`
- [ ] Sync history survives school DB transaction rollback
- [ ] Unit test: Failed sync records error message
- [ ] Query: `SELECT * FROM SyncHistory WHERE SchoolId = 1 ORDER BY SyncStartTime DESC`

---

### T027: Write Sync Service Unit Tests → T025, T026
**Requirements**: NFR-004
**Files Created**:
- `tests/CleverSyncSOS.Core.Tests/Sync/SyncOrchestrationServiceTests.cs`
- `tests/CleverSyncSOS.Core.Tests/Sync/FullSyncServiceTests.cs`
- `tests/CleverSyncSOS.Core.Tests/Sync/IncrementalSyncServiceTests.cs`

**Acceptance Criteria**:
- [ ] Test: Full sync marks inactive, reactivates, deletes
- [ ] Test: Incremental sync uses last timestamp
- [ ] Test: Orchestration respects concurrency limit
- [ ] Test: School failure doesn't affect other schools
- [ ] Test: Sync history recorded correctly
- [ ] All tests pass with > 80% code coverage

---

## Phase 5: Azure Function Endpoints (Day 10)

### T028: Implement Timer Trigger Function → T025
**Requirements**: FR-009, US-001
**Files Created**:
- `src/CleverSyncSOS.Functions/SyncTimerFunction.cs`

**Acceptance Criteria**:
- [ ] Timer trigger: `[TimerTrigger("%CleverSync:Schedule:CronExpression%")]`
- [ ] Default schedule: Daily at 2 AM UTC (`0 0 2 * * *`)
- [ ] Calls `ISyncOrchestrationService.SyncAllDistrictsAsync()`
- [ ] Logs start/completion with execution duration
- [ ] Function runs successfully on local emulator

---

### T029: Implement Manual Sync HTTP Trigger → T025
**Requirements**: FR-009, US-002
**Files Created**:
- `src/CleverSyncSOS.Functions/ManualSyncFunction.cs`

**Acceptance Criteria**:
- [ ] HTTP POST endpoint: `/api/sync`
- [ ] Query params: `?districtId=1` or `?schoolId=5` or none (all)
- [ ] Function-level authorization: `[HttpTrigger(AuthorizationLevel.Function)]`
- [ ] Returns 200 OK with sync summary JSON
- [ ] Returns 400 Bad Request for invalid IDs
- [ ] Test: `curl -X POST http://localhost:7071/api/sync?schoolId=1`

---

## Phase 6: Health & Monitoring (Days 11-12)

### T030: Implement Health Check Service [P]
**Requirements**: FR-011, NFR-001
**Files Created**:
- `src/CleverSyncSOS.Core/Health/HealthCheckService.cs`
- `src/CleverSyncSOS.Core/Health/AuthenticationHealthStatus.cs`

**Acceptance Criteria**:
- [ ] Checks: Key Vault connectivity, token validity, SessionDb connectivity
- [ ] Status: Healthy, Degraded, Unhealthy
- [ ] Response time < 100ms (NFR-001)
- [ ] Cached for 30 seconds (configurable: `CleverSync:Health:CacheDurationSeconds`)
- [ ] Unit test: All checks healthy returns Healthy
- [ ] Unit test: Key Vault down returns Degraded

---

### T031: Implement Health Check HTTP Endpoint → T030
**Requirements**: FR-011, US-004
**Files Created**:
- `src/CleverSyncSOS.Functions/HealthCheckFunction.cs`

**Acceptance Criteria**:
- [ ] HTTP GET endpoint: `/api/health/clever-auth`
- [ ] Returns JSON: `{ "status": "Healthy", "checks": [...], "timestamp": "..." }`
- [ ] HTTP status codes: 200 (Healthy), 200 (Degraded), 503 (Unhealthy)
- [ ] Response time < 100ms (NFR-001)
- [ ] Test: `curl http://localhost:7071/api/health/clever-auth`

---

### T032: Configure Application Insights Telemetry → T010 [P]
**Requirements**: FR-013, NFR-005
**Files Modified**:
- `src/CleverSyncSOS.Functions/Program.cs`
- `src/CleverSyncSOS.Functions/host.json`

**Acceptance Criteria**:
- [ ] Application Insights connection string in Key Vault
- [ ] Custom dimensions: `SchoolId`, `DistrictId`, `EntityType`, `SyncId`
- [ ] Dependency tracking enabled for HTTP, SQL
- [ ] Sampling rate: 100% (configurable per environment)
- [ ] Test: Verify logs appear in Application Insights portal

---

### T033: Implement Alerting Rules → T032
**Requirements**: NFR-005
**Azure Resources**:
- Action Group: Email/SMS notification
- Alert Rule: 3+ consecutive sync failures per school

**Acceptance Criteria**:
- [ ] Alert query: Count `SyncHistory` where `Status = 'Failed'` grouped by `SchoolId`
- [ ] Threshold: 3 failures in 24 hours
- [ ] Action: Email to operations team
- [ ] Test: Trigger alert with 3 manual failures

---

## Phase 7: Testing & Validation (Days 13-14)

### T034: Write End-to-End Integration Tests → T029, T031
**Requirements**: All FR-XXX, NFR-004
**Files Created**:
- `tests/CleverSyncSOS.Integration.Tests/EndToEnd/FullSyncE2ETests.cs`
- `tests/CleverSyncSOS.Integration.Tests/EndToEnd/IncrementalSyncE2ETests.cs`

**Acceptance Criteria**:
- [ ] Test: Full sync with real Clever test data
- [ ] Test: Incremental sync after initial full sync
- [ ] Test: Multi-school concurrent sync (3 schools)
- [ ] Test: Health check returns Healthy after successful sync
- [ ] Test: Sync history recorded correctly
- [ ] All tests pass in CI/CD pipeline

---

### T035: Performance Testing → T034
**Requirements**: NFR-001, NFR-003
**Files Created**:
- `tests/CleverSyncSOS.Performance/SyncPerformanceTests.cs`

**Acceptance Criteria**:
- [ ] Benchmark: 10,000 student sync completes in < 5 minutes
- [ ] Benchmark: Health check response < 100ms
- [ ] Benchmark: Authentication < 5 seconds
- [ ] Load test: 10 concurrent school syncs
- [ ] Results documented in performance report

---

### T036: Security Testing → T010
**Requirements**: NFR-002
**Test Cases**:
- [ ] Verify no secrets in Application Insights logs
- [ ] Verify managed identity Key Vault access (no connection strings)
- [ ] Verify TLS 1.2+ for all HTTP requests
- [ ] Verify SQL injection prevention (parameterized queries)
- [ ] Verify CORS policies on HTTP endpoints

---

### T037: User Acceptance Testing → T029, T031
**Requirements**: US-001, US-002, US-003, US-004
**Test Cases**:
- [ ] US-001: Scheduled sync runs daily at 2 AM
- [ ] US-002: Manual full sync removes graduated students
- [ ] US-003: Multi-district sync with separate credentials
- [ ] US-004: Health check endpoint accessible in Azure Portal
- [ ] US-005: Sync history queryable for troubleshooting

---

## Phase 8: Deployment & Documentation (Day 15)

### T038: Create ARM Templates or Bicep → T004
**Requirements**: NFR-001
**Files Created**:
- `infra/main.bicep`
- `infra/parameters.json`

**Acceptance Criteria**:
- [ ] Resources: Function App, Key Vault, Application Insights, App Service Plan
- [ ] Managed identity enabled and configured
- [ ] RBAC roles assigned (Key Vault Secrets User)
- [ ] Deployment: `az deployment group create --template-file main.bicep`

---

### T039: Configure CI/CD Pipeline [P]
**Requirements**: Constitution CI/CD Roadmap
**Files Created**:
- `.github/workflows/deploy.yml`

**Acceptance Criteria**:
- [ ] Build: `dotnet build`, `dotnet test`
- [ ] Security scan: Dependency check, SAST (CodeQL)
- [ ] Deploy: Publish to Azure Function App
- [ ] Post-deploy: Health check validation
- [ ] Pipeline runs on PR merge to main

---

### T040: Create Quickstart Guide → T001-T039
**Requirements**: Plan Quickstart Section
**Files Created**:
- `SpecKit/QuickStart/quickstart.md`

**Acceptance Criteria**:
- [ ] Section 1: Azure resource setup (Key Vault, Function App)
- [ ] Section 2: Configure Clever credentials
- [ ] Section 3: Deploy function app
- [ ] Section 4: Verify health check
- [ ] Section 5: Trigger manual sync
- [ ] Section 6: Monitor Application Insights

---

### T041: Create Operational Runbook → T040
**Requirements**: NFR-005
**Files Created**:
- `docs/operations-runbook.md`

**Acceptance Criteria**:
- [ ] Troubleshooting: Common sync failures
- [ ] Procedures: Add new school, remove school
- [ ] Monitoring: Key metrics and alert thresholds
- [ ] Incident response: Sync failure, Key Vault outage
- [ ] Escalation contacts

---

### T042: Update README → T040
**Requirements**: US-005
**Files Modified**:
- `README.md`

**Acceptance Criteria**:
- [ ] Architecture diagram (SessionDb → Schools → Clever API)
- [ ] Features list with constitution alignment
- [ ] Setup instructions (link to quickstart)
- [ ] Configuration reference (link to plan.md)
- [ ] Contributing guidelines

---

## Phase 9: Final Validation & Handoff (Day 15)

### T043: Production Deployment Dry Run → T038, T039
**Requirements**: All NFR-XXX
**Tasks**:
- [ ] Deploy to staging environment
- [ ] Run full sync for one test school
- [ ] Verify Application Insights logs
- [ ] Verify health check responds
- [ ] Load test with 5 concurrent schools
- [ ] Security scan passes

---

### T044: Production Go-Live Checklist
**Requirements**: All FR-XXX, US-XXX
**Tasks**:
- [ ] All 44 tasks completed and tested
- [ ] Security review approved
- [ ] Performance benchmarks met (NFR-001)
- [ ] Monitoring dashboards configured
- [ ] Alert rules active
- [ ] Runbook reviewed with operations team
- [ ] Rollback plan documented
- [ ] Go/No-Go decision approved

---

## Summary Statistics

| Phase | Tasks | Estimated Days | Parallel Tasks |
|-------|-------|----------------|----------------|
| Phase 0: Setup | 4 | 1 | 3 |
| Phase 1: Authentication | 8 | 2 | 2 |
| Phase 2: Database | 5 | 2 | 1 |
| Phase 3: Clever API | 3 | 1 | 1 |
| Phase 4: Sync Service | 7 | 3 | 0 |
| Phase 5: Functions | 2 | 1 | 0 |
| Phase 6: Health & Monitoring | 4 | 2 | 2 |
| Phase 7: Testing | 4 | 2 | 0 |
| Phase 8: Deployment | 5 | 1 | 2 |
| Phase 9: Go-Live | 2 | 0.5 | 0 |
| **TOTAL** | **44** | **15.5** | **11** |

---

## Requirement Coverage Matrix

| Requirement | Covered by Tasks | Status |
|-------------|------------------|--------|
| FR-001: OAuth 2.0 Authentication | T001-T012 | ✅ Covered |
| FR-002: Secure Credential Storage | T004, T006 | ✅ Covered |
| FR-003: Token Lifecycle Management | T008 | ✅ Covered |
| FR-004: Multi-School Data Isolation | T013-T017 | ✅ Covered |
| FR-005: Clever Data Synchronization | T018-T024 | ✅ Covered |
| FR-006: Full Sync Mode | T023 | ✅ Covered |
| FR-007: Incremental Sync Mode | T024 | ✅ Covered |
| FR-008: Multi-District Support | T014 | ✅ Covered |
| FR-009: Sync Orchestration | T025, T028, T029 | ✅ Covered |
| FR-010: Retry Logic and Error Handling | T009 | ✅ Covered |
| FR-011: Health Check Endpoint | T030, T031 | ✅ Covered |
| FR-012: Graceful Degradation | T030 | ✅ Covered |
| FR-013: Structured Logging | T010, T032 | ✅ Covered |
| FR-014: Sync History Tracking | T026 | ✅ Covered |
| NFR-001: Performance | T012, T030, T035 | ✅ Covered |
| NFR-002: Security | T004, T006, T010, T036 | ✅ Covered |
| NFR-003: Scalability | T017, T025 | ✅ Covered |
| NFR-004: Reliability | T009, T027, T034 | ✅ Covered |
| NFR-005: Observability | T032, T033 | ✅ Covered |
| NFR-006: Configurability | T005 | ✅ Covered |
| US-001: Automated Daily Sync | T028 | ✅ Covered |
| US-002: Beginning-of-Year Data Refresh | T023, T029 | ✅ Covered |
| US-003: Multi-District Management | T014 | ✅ Covered |
| US-004: Operational Health Monitoring | T030, T031 | ✅ Covered |
| US-005: Troubleshooting Failed Syncs | T026, T041 | ✅ Covered |

---

## Next Steps

1. Review this task list with stakeholders
2. Begin implementation with Phase 0 (Project Setup)
3. Track progress by checking off acceptance criteria
4. Update task list as implementation reveals additional work
5. Run `/speckit.analyze` after completing phases to verify alignment
