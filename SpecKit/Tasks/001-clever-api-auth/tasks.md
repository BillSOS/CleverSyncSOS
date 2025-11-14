---
speckit:
  type: tasks
  title: Clever API Authentication Tasks
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../../Specs/001-clever-api-auth/spec-1.md
  linked_plan: ../../plan.md
---

```

# Tasks: Clever API Authentication and Connection

## Phase 1: Core Implementation

### Authentication Service

- \[ \] Implement `ICleverAuthenticationService` interface
- \[ \] Create `CleverAuthenticationService` with OAuth 2.0 client credentials flow
- \[ \] Add proactive token refresh logic (75% lifetime)
- \[ \] Integrate Polly retry policy for transient failures
- \[ \] Configure IHttpClientFactory with typed Clever client

### Credential Management

- \[ \] Implement `ICredentialStore` interface
- \[ \] Create `KeyVaultCredentialStore` using Azure.Identity and Azure.Security.KeyVault.Secrets
- \[ \] Add background retry loop for Key Vault unavailability
- \[ \] Validate credentials on startup and log audit trail

### Configuration

- \[ \] Define `CleverAuthConfiguration` model
- \[ \] Load configuration from Azure App Configuration or secure settings
- \[ \] Make retry intervals, timeouts, and endpoints externally configurable

## Phase 2: Health & Observability

### Health Check

- \[ \] Implement `CleverAuthenticationHealthCheck` class
- \[ \] Register health check in ASP.NET Core middleware
- \[ \] Expose `GET /health/clever-auth` endpoint
- \[ \] Cache health status and update every 30 seconds

### Logging

- \[ \] Add structured logging via ILogger
- \[ \] Integrate with Azure Application Insights
- \[ \] Sanitize logs to prevent credential leakage

## Phase 3: Testing

### Unit Tests

- \[ \] Write tests for `CleverAuthenticationService`
- \[ \] Write tests for `CleverApiRetryPolicy`
- \[ \] Write tests for `KeyVaultCredentialStore`
- \[ \] Write tests for `CredentialValidation`
- \[ \] Write tests for `CleverAuthenticationHealthCheck`

### Integration Tests

- \[ \] Test Clever token acquisition with mock credentials
- \[ \] Test Key Vault access using Azure SDK test infrastructure
- \[ \] Validate health check endpoint under failure conditions

## Phase 4: Deployment & Validation

### CI/CD

- \[ \] Add build and test steps to CI pipeline
- \[ \] Add deployment steps to CD pipeline for Azure Functions/App Service
- \[ \] Validate health check endpoint post-deployment
- \[ \] Review logs and telemetry for credential safety

* * *

---

# Stage 2: Database Synchronization Tasks

## Phase 1: Database Setup

### SessionDb Schema

- [ ] Add Microsoft.EntityFrameworkCore.SqlServer package
- [ ] Add Microsoft.EntityFrameworkCore.Design package
- [ ] Create `District` entity class
- [ ] Create `School` entity class
- [ ] Create `SyncHistory` entity class
- [ ] Create `SessionDbContext` with entity configurations
- [ ] Add SessionDb connection string to Key Vault
- [ ] Create EF Core migration for SessionDb
- [ ] Apply migration to SessionDb in Azure

### Per-School Database Schema

- [ ] Create `Student` entity class
- [ ] Create `Teacher` entity class
- [ ] Create `SchoolDbContext` with entity configurations
- [ ] Create EF Core migration template for school databases
- [ ] Document school database setup process

### Connection Management

- [ ] Create `IDbContextFactory<SessionDbContext>` implementation
- [ ] Create `SchoolDatabaseConnectionFactory` class
- [ ] Implement dynamic connection string resolution from Key Vault
- [ ] Add connection pooling configuration
- [ ] Implement connection validation on startup

## Phase 2: Clever API Client

### API Client Implementation

- [ ] Create `ICleverApiClient` interface
- [ ] Define `CleverStudent` DTO model
- [ ] Define `CleverTeacher` DTO model
- [ ] Define `CleverSchool` DTO model
- [ ] Define `CleverApiResponse<T>` generic wrapper
- [ ] Define `CleverPaging` model
- [ ] Implement `CleverApiClient` with IHttpClientFactory
- [ ] Implement pagination handling (100 records per page)
- [ ] Add rate limit detection and handling (HTTP 429)
- [ ] Implement exponential backoff with Polly

### API Endpoints

- [ ] Implement `GetSchoolsAsync(string districtId)` method
- [ ] Implement `GetStudentsAsync(string schoolId, DateTime? lastModified)` method
- [ ] Implement `GetTeachersAsync(string schoolId, DateTime? lastModified)` method
- [ ] Implement `GetPagedDataAsync<T>(string endpoint, int page, int pageSize)` helper
- [ ] Add Clever API base URL configuration

## Phase 3: Data Synchronization Service

### Sync Service Core

- [ ] Create `ISyncService` interface
- [ ] Create `SyncResult` model
- [ ] Implement `SyncService` class
- [ ] Implement `SyncAllDistrictsAsync()` method
- [ ] Implement `SyncDistrictAsync(int districtId)` method
- [ ] Implement `SyncSchoolAsync(int schoolId, bool fullSync)` method
- [ ] Implement parallel school sync with SemaphoreSlim (max 5 concurrent)

### Student Synchronization

- [ ] Implement `SyncStudentsAsync(int schoolId, DateTime? lastModified)` method
- [ ] Create student data mapping logic (Clever → Entity)
- [ ] Implement student upsert logic (INSERT or UPDATE based on CleverStudentId)
- [ ] Add transaction support for student sync
- [ ] Record sync start in SessionDb.SyncHistory
- [ ] Record sync results in SessionDb.SyncHistory

### Teacher Synchronization

- [ ] Implement `SyncTeachersAsync(int schoolId, DateTime? lastModified)` method
- [ ] Create teacher data mapping logic (Clever → Entity)
- [ ] Implement teacher upsert logic (INSERT or UPDATE based on CleverTeacherId)
- [ ] Add transaction support for teacher sync
- [ ] Record sync start in SessionDb.SyncHistory
- [ ] Record sync results in SessionDb.SyncHistory

### Incremental Sync

- [ ] Query SessionDb.SyncHistory for last successful sync timestamp
- [ ] Pass lastModified parameter to Clever API
- [ ] Handle first-time full sync vs incremental sync
- [ ] Store Clever's last_modified timestamp in entity records
- [ ] Update LastSyncTimestamp in SyncHistory after successful sync

## Phase 4: Azure Function Implementation

### Timer Trigger Function

- [ ] Create `SyncTimerFunction` class
- [ ] Implement timer trigger (cron: "0 0 2 * * *" for daily at 2 AM UTC)
- [ ] Inject ISyncService via DI
- [ ] Call SyncAllDistrictsAsync() on trigger
- [ ] Add structured logging for scheduled sync

### Manual Trigger Function

- [ ] Create `ManualSyncFunction` HTTP trigger
- [ ] Parse query parameters (schoolId, districtId)
- [ ] Support school-level sync via ?schoolId={id}
- [ ] Support district-level sync via ?districtId={id}
- [ ] Support full sync with no parameters
- [ ] Add function-level authorization
- [ ] Return sync status in HTTP response

### Dependency Injection Setup

- [ ] Register SessionDbContext in DI container
- [ ] Register SchoolDbContext factory in DI container
- [ ] Register ICleverApiClient in DI container
- [ ] Register ISyncService in DI container
- [ ] Register SchoolDatabaseConnectionFactory in DI container
- [ ] Configure EF Core with SQL Server provider

## Phase 5: Error Handling & Resilience

### Retry Policies

- [ ] Configure Polly retry policy for database operations (3 retries, exponential backoff)
- [ ] Configure Polly retry policy for Clever API calls (5 retries, exponential backoff)
- [ ] Handle SqlException with transient error detection
- [ ] Handle HttpRequestException for network failures
- [ ] Handle Clever API specific errors (rate limits, invalid tokens)

### Error Isolation

- [ ] Ensure failure in one school doesn't stop other schools
- [ ] Catch and log exceptions per school
- [ ] Continue district sync even if one school fails
- [ ] Record partial sync status in SyncHistory

### Logging & Monitoring

- [ ] Log sync start/end for each district
- [ ] Log sync start/end for each school
- [ ] Log record counts (processed, failed)
- [ ] Log Clever API rate limit events
- [ ] Log database connection failures
- [ ] Send alerts on 3+ consecutive failures for same school

## Phase 6: Configuration & Key Vault

### Key Vault Secrets

- [ ] Add SessionDb connection string to Key Vault
- [ ] Document naming convention for school connection strings
- [ ] Document naming convention for district Clever credentials
- [ ] Create example Key Vault secret structure in documentation

### Configuration Models

- [ ] Create `SyncConfiguration` class
- [ ] Create `DistrictConfiguration` class
- [ ] Add configuration for MaxConcurrentSchools (default: 5)
- [ ] Add configuration for PageSize (default: 100)
- [ ] Add configuration for CleverApiBaseUrl
- [ ] Bind configuration from Azure App Configuration / appsettings

## Phase 7: Testing

### Unit Tests

- [ ] Write tests for CleverApiClient pagination
- [ ] Write tests for CleverApiClient rate limit handling
- [ ] Write tests for SyncService orchestration logic
- [ ] Write tests for student data mapping
- [ ] Write tests for teacher data mapping
- [ ] Write tests for upsert logic (insert vs update)
- [ ] Write tests for incremental sync timestamp logic

### Integration Tests

- [ ] Test SessionDb connection and queries
- [ ] Test school database connection factory
- [ ] Test full sync for single school
- [ ] Test incremental sync with lastModified parameter
- [ ] Test parallel sync for multiple schools
- [ ] Test error handling with simulated failures
- [ ] Test SyncHistory tracking accuracy

## Phase 8: Documentation & Deployment

### Documentation

- [ ] Create setup guide for SessionDb
- [ ] Create setup guide for school databases
- [ ] Document Key Vault secret structure
- [ ] Document manual trigger endpoint usage
- [ ] Create troubleshooting guide

### Deployment

- [ ] Deploy SessionDb schema to Azure
- [ ] Deploy sample school database schema
- [ ] Configure Key Vault secrets
- [ ] Deploy Azure Function to Azure
- [ ] Validate timer trigger schedule
- [ ] Test manual trigger endpoint
- [ ] Verify sync operations in SessionDb.SyncHistory

* * *