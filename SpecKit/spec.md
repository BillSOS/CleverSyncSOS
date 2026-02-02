---
speckit:
  type: specification
  title: Clever API Authentication and Connection
  owner: Bill Martin
  version: 1.0.0
  branch: 001-clever-api-auth
  date: 2025-11-18
  status: draft
---

# Feature Specification: Clever API Authentication and Connection

**Branch**: `001-clever-api-auth`
**Constitution**: Aligned with CleverSyncSOS Constitution v1.1.0
**Owner**: Bill Martin

---

## Overview

CleverSyncSOS requires secure, automated synchronization of student information system (SIS) data from Clever's API to individual school Azure SQL databases. This feature establishes the authentication foundation and core synchronization capabilities for multi-school deployments with per-school data isolation.

### Problem Statement

Schools using Clever for SIS data need their information synchronized to SOS Azure SQL databases at regular intervals. Manual synchronization is error-prone and doesn't scale across multiple schools and districts. The system must handle authentication, token management, multi-school data isolation, and robust error recovery without exposing credentials or compromising data integrity.

### Success Criteria

- Automated authentication with Clever API using OAuth 2.0
- Secure credential management with zero leaks to logs or telemetry
- Successful multi-school synchronization with complete data isolation
- Full and incremental sync modes with automatic recovery
- Health monitoring and operational visibility
- 99.9%+ sync success rate across all schools

---

## Clarifications

### Session 2025-12-02

- Q: Now that you've enabled Events API in Clever, should the spec prioritize Events API as the primary incremental sync mechanism (with data API as fallback), or keep data API as primary? → A: Events API primary; data API fallback when Events unavailable (performance optimized)
- Q: The spec doesn't clearly document how the baseline event ID gets established for a school. When should this baseline be created? → A: After each successful full sync (re-establish baseline after data refresh)
- Q: The spec mentions recording "records processed" in SyncHistory, but doesn't clarify whether "updated" count should include all records touched or only records with actual data changes. → A: Only records with actual data changes count as "updated" (accurate metrics)
- Q: The change detection logic compares field values between Clever and database. Should null values be treated as equivalent to empty strings (and whitespace) when detecting changes? → A: Null = empty = whitespace (normalized comparison, fewer false positives)
- Q: During full sync, all records are marked inactive before fetching from Clever. Should Entity Framework's change tracker be cleared after this step to prevent cached inactive entities from affecting upsert logic? → A: Clear change tracker after marking inactive (prevents stale cache issues)

---

## Functional Requirements

### FR-001: OAuth 2.0 Authentication
**Priority**: P0 (Blocking)
**Description**: System must authenticate with Clever API using OAuth 2.0 client credentials flow.

**Acceptance Criteria**:
- ✅ Authenticate using client ID and secret from Azure Key Vault
- ✅ Acquire valid bearer token for API requests
- ✅ Authentication completes within 5 seconds of startup
- ✅ Support per-district credential isolation (multiple Clever apps)

---

### FR-002: Secure Credential Storage
**Priority**: P0 (Blocking)
**Description**: All Clever API credentials must be stored in Azure Key Vault and retrieved using managed identity.

**Acceptance Criteria**:
- ✅ Client ID and secret stored in Key Vault with naming pattern: `CleverSyncSOS--District-{Name}--ClientId`
- ✅ Managed identity used for Key Vault access (no connection strings)
- ✅ Zero credentials in application configuration, code, or logs
- ✅ Key Vault access audited via Azure Monitor

---

### FR-003: Token Lifecycle Management
**Priority**: P0 (Blocking)
**Description**: System must automatically refresh OAuth tokens before expiration to prevent request failures.

**Acceptance Criteria**:
- ✅ Tokens cached in-memory after acquisition
- ✅ Proactive refresh triggered at 75% of token lifetime
- ✅ Background token refresh runs without blocking requests
- ✅ Zero request failures due to expired tokens
- ✅ Fallback to on-demand refresh if background refresh fails

---

### FR-004: Multi-School Data Isolation
**Priority**: P0 (Blocking)
**Description**: Each school's data must be stored in a separate Azure SQL database with complete isolation.

**Acceptance Criteria**:
- ✅ SessionDb tracks districts, schools, and sync metadata
- ✅ Each school has dedicated database (e.g., `School_Lincoln_Db`)
- ✅ Connection strings stored per-school in Key Vault
- ✅ No cross-school data contamination
- ✅ Dynamic connection resolution based on school ID

---

### FR-005: Clever Data Synchronization
**Priority**: P0 (Blocking)
**Description**: System must retrieve and synchronize student, teacher, and section data from Clever API to school databases.

**Acceptance Criteria**:
- ✅ Fetch students, teachers, and sections via Clever API v3.0
- ✅ Map Clever data to Azure SQL schema
- ✅ Upsert records (insert new, update existing)
- ✅ Change detection: Normalize strings (null = empty = whitespace) to prevent false positives
- ✅ Handle pagination (100 records per page)
- ✅ Preserve Clever IDs for idempotency
- ✅ Track last modified timestamps from Clever

---

### FR-006: Full Sync Mode
**Priority**: P0 (Blocking)
**Description**: System must support full synchronization to refresh all school data (e.g., beginning of school year).

**Acceptance Criteria**:
- ✅ Triggered manually or when `School.RequiresFullSync = true`
- ✅ Retrieves all records from Clever (no date filters)
- ✅ Marks existing records inactive before sync
- ✅ Clears Entity Framework change tracker after marking inactive to prevent stale cache
- ✅ Reactivates records still present in Clever
- ✅ Deletes records no longer in Clever (graduated/transferred students)
- ✅ Establishes baseline event ID from Clever Events API for future incremental syncs
- ✅ Resets `RequiresFullSync` flag after completion

---

### FR-007: Incremental Sync Mode
**Priority**: P0 (Blocking)
**Description**: System must support incremental synchronization using Clever's Events API as primary mechanism, with data API fallback for districts without Events enabled.

**Acceptance Criteria**:
- ✅ **Primary**: Uses Clever Events API when baseline event ID established
- ✅ **Fallback**: Uses data API with `last_modified` filter when Events API unavailable
- ✅ Retrieves only changed records since last sync
- ✅ Inserts new records, updates existing records
- ✅ Does not delete records (handled by full sync)
- ✅ Automatic change detection prevents unnecessary updates when using data API fallback

---

### FR-008: Multi-District Support
**Priority**: P1 (High)
**Description**: System must support multiple districts with separate Clever credentials and school lists.

**Acceptance Criteria**:
- ✅ SessionDb stores district metadata
- ✅ Each district has separate Clever API credentials in Key Vault
- ✅ Schools associated with parent district
- ✅ Sync can target all districts, single district, or single school
- ✅ District-level configuration isolation

---

### FR-009: Sync Orchestration
**Priority**: P0 (Blocking)
**Description**: System must orchestrate synchronization across multiple schools with concurrency control.

**Acceptance Criteria**:
- ✅ Scheduled sync runs daily at configurable time (default: 2 AM UTC)
- ✅ Manual sync endpoint with district/school filtering
- ✅ Maximum 5 concurrent school syncs (configurable)
- ✅ Per-school sync failures don't block other schools
- ✅ Sync history recorded in SessionDb per school per entity type

---

### FR-010: Retry Logic and Error Handling
**Priority**: P0 (Blocking)
**Description**: System must implement exponential backoff retry for transient failures.

**Acceptance Criteria**:
- ✅ Clever API calls: 5 retries, exponential backoff (2s, 4s, 8s, 16s, 32s)
- ✅ Database operations: 3 retries, exponential backoff (1s, 2s, 4s)
- ✅ HTTP 429 (rate limit) handled with Retry-After header
- ✅ Permanent failures logged and recorded in SyncHistory
- ✅ Configurable retry policy via Azure App Configuration

---

### FR-011: Health Check Endpoint
**Priority**: P1 (High)
**Description**: System must expose health check endpoint for operational monitoring.

**Acceptance Criteria**:
- ✅ Endpoint: `GET /health/clever-auth`
- ✅ Response time < 100ms
- ✅ Returns status: Healthy, Degraded, Unhealthy
- ✅ Includes Key Vault connectivity check
- ✅ Includes token validity check
- ✅ Cached health status (updated every 30 seconds)

---

### FR-012: Graceful Degradation
**Priority**: P1 (High)
**Description**: System must handle dependency failures gracefully without crashing.

**Acceptance Criteria**:
- ✅ Starts successfully when Key Vault is unavailable
- ✅ Health check reports "Degraded" when dependencies down
- ✅ Retries Key Vault connection on next sync attempt
- ✅ Logs degraded state to Application Insights
- ✅ No cascading failures across schools

---

### FR-013: Structured Logging
**Priority**: P0 (Blocking)
**Description**: All operations must emit structured, queryable logs to Application Insights.

**Acceptance Criteria**:
- ✅ Logs include: SchoolId, DistrictId, EntityType, SyncId, Status
- ✅ Secrets sanitized from logs (client secret, tokens, connection strings)
- ✅ Error logs include exception details and correlation IDs
- ✅ Info logs for sync start, completion, and record counts
- ✅ Logs queryable in Application Insights with custom dimensions

---

### FR-014: Sync History Tracking
**Priority**: P0 (Blocking)
**Description**: System must record detailed sync history for auditing and troubleshooting.

**Acceptance Criteria**:
- ✅ SessionDb.SyncHistory tracks per-school, per-entity syncs
- ✅ Records: start time, end time, status, records processed, records updated, errors
- ✅ **Processed**: Count of all records examined/fetched from Clever
- ✅ **Updated**: Count of records with actual data changes persisted to database
- ✅ Status values: InProgress, Success, Failed, Partial
- ✅ Stores last sync timestamp and event ID for incremental syncs
- ✅ Retention policy: 90 days (configurable)

---

## Non-Functional Requirements

### NFR-001: Performance
- **Authentication Latency**: Authenticate within 5 seconds of startup
- **Health Check Response**: < 100ms response time
- **Token Refresh**: Background refresh completes before expiration (zero downtime)
- **Sync Throughput**: Process 10,000 student records in < 5 minutes per school

---

### NFR-002: Security
- **Credential Storage**: Zero secrets in config, code, or logs
- **Transport Security**: TLS 1.2+ for all external communications
- **Key Vault Access**: Managed identity only (no service principals)
- **Audit Trail**: All Key Vault accesses logged to Azure Monitor
- **Log Sanitization**: Automated redaction of secrets from telemetry

---

### NFR-003: Scalability
- **Multi-School**: Support 100+ schools per district
- **Multi-District**: Support 10+ districts
- **Concurrent Syncs**: 5 parallel school syncs (configurable up to 20)
- **Database Isolation**: Each school uses dedicated database

---

### NFR-004: Reliability
- **Sync Success Rate**: 99.9% across all schools
- **Health Check Accuracy**: 99.9% (no false positives/negatives)
- **Retry Success**: 95% of transient failures resolved by retry logic
- **Zero Data Loss**: All Clever data successfully persisted or failure logged

---

### NFR-005: Observability
- **Centralized Logging**: All logs in Application Insights
- **Queryable Metrics**: Custom dimensions for SchoolId, DistrictId, EntityType
- **Alerting**: Automated alerts for 3+ consecutive failures
- **Dashboards**: Real-time sync status per school

---

### NFR-006: Configurability
All operational parameters must be externally configurable via Azure Function App settings and Azure Key Vault:
- Sync schedule (cron expression)
- Retry counts and backoff intervals
- Concurrent sync limit
- Health check cache duration
- Token refresh threshold (default: 75%)
- Sync history retention (default: 90 days)

**Configuration Sources**:
- **Function App Settings**: Non-sensitive configuration (schedules, timeouts, retry counts)
- **Azure Key Vault**: Sensitive configuration (connection strings, API credentials)

---

## User Stories

### US-001: Automated Daily Sync
**As a** school administrator
**I want** student and teacher data automatically synchronized from Clever to my school's database daily
**So that** my SOS applications always have up-to-date information without manual intervention

**Acceptance Criteria**:
- Sync runs daily at 2 AM UTC (configurable)
- Students, teachers, and sections synced
- Email notification on sync completion (success or failure)
- Sync history visible in admin dashboard

---

### US-002: Beginning-of-Year Data Refresh
**As a** school administrator
**I want** to trigger a full sync at the beginning of the school year
**So that** graduated students are removed and new students are added

**Acceptance Criteria**:
- Manual trigger via admin UI or API endpoint
- All existing students marked inactive
- Students still in Clever reactivated
- Students no longer in Clever permanently deleted
- Sync report shows added, updated, and deleted counts

---

### US-003: Multi-District Management
**As a** SOS technical administrator
**I want** to manage synchronization for multiple districts with separate Clever credentials
**So that** each district's data remains isolated and secure

**Acceptance Criteria**:
- Add new district via admin UI with Clever credentials
- Credentials stored securely in Key Vault
- District-level sync status visible in dashboard
- Per-district error tracking and alerting

---

### US-004: Operational Health Monitoring
**As a** DevOps engineer
**I want** health check endpoints for all critical dependencies
**So that** I can monitor system health and receive alerts for failures

**Acceptance Criteria**:
- `/health/clever-auth` endpoint returns status in < 100ms
- Integration with Azure Monitor health checks
- Alerts triggered for Unhealthy status
- Health status includes Key Vault, Clever API, and Database connectivity

---

### US-005: Troubleshooting Failed Syncs
**As a** support engineer
**I want** detailed sync history with error messages and correlation IDs
**So that** I can diagnose and resolve sync failures quickly

**Acceptance Criteria**:
- Sync history queryable by school, date range, entity type
- Error messages include exception details
- Correlation IDs link logs across Application Insights
- Retry history visible for transient failures

---

## Data Scope

### Entities Synchronized from Clever

**Students**:
- Clever Student ID (required, unique)
- First Name, Last Name
- Email (optional)
- Grade (optional)
- Student Number (optional)
- Last Modified timestamp

**Teachers**:
- Clever Teacher ID (required, unique)
- First Name, Last Name
- Email (required)
- Title (optional)
- Last Modified timestamp

**Sections** (Future):
- Clever Section ID
- Course name, period
- Associated teacher and students

---

### Metadata Entities (SessionDb)

**Districts**:
- District ID (internal)
- Clever District ID
- Name
- Key Vault secret prefix

**Schools**:
- School ID (internal)
- District ID (foreign key)
- Clever School ID
- Name
- Database name
- Connection string secret name
- Active status
- Requires full sync flag

**Sync History**:
- Sync ID
- School ID
- Entity type (Student, Teacher, Section)
- Sync type (Full, Incremental)
- Start/end timestamps
- Status (InProgress, Success, Failed, Partial)
- Records processed, failed, deleted
- Error messages
- Last sync timestamp

---

## Edge Cases and Error Scenarios

### EC-001: Clever API Rate Limiting
**Scenario**: Clever API returns HTTP 429 during high-volume sync
**Expected Behavior**:
- Respect `Retry-After` header
- Exponential backoff with jitter
- Log rate limit event to Application Insights
- Resume sync after backoff period

---

### EC-002: Key Vault Unavailable at Startup
**Scenario**: Managed identity can't access Key Vault during function startup
**Expected Behavior**:
- Function starts successfully (graceful degradation)
- Health check returns "Degraded"
- Retry Key Vault access on next scheduled sync
- Alert sent to operations team

---

### EC-003: Partial Sync Failure
**Scenario**: Database connection lost mid-sync after 500 of 1000 students processed
**Expected Behavior**:
- Transaction rollback for current batch
- Sync marked as "Failed" in SyncHistory
- Error logged with records processed count
- Next sync retries from beginning (incremental mode uses last successful timestamp)

---

### EC-004: School Database Doesn't Exist
**Scenario**: School record in SessionDb references non-existent database
**Expected Behavior**:
- Sync marked as "Failed" for that school
- Error logged: "Database not found"
- Other schools continue syncing (failure isolation)
- Alert sent to technical administrator

---

### EC-005: Student Deleted in Clever (Mid-Year)
**Scenario**: Student withdrawn mid-year, removed from Clever
**Expected Behavior**:
- **Incremental Sync**: No action (student remains in database)
- **Full Sync**: Student marked inactive, then deleted
- Rationale: Preserve historical data during school year

---

### EC-006: Duplicate Clever ID in Different Schools
**Scenario**: Same Clever Student ID appears in multiple schools (transfer)
**Expected Behavior**:
- Separate databases prevent conflict
- Student appears in both school databases if present in both Clever schools
- Each school's data isolated

---

### EC-007: Token Expires Mid-Sync
**Scenario**: OAuth token expires during multi-school sync operation
**Expected Behavior**:
- Automatic token refresh before next API call
- Current request retried with new token
- Sync continues without interruption
- Event logged for monitoring

---

### EC-008: Clever API Schema Change
**Scenario**: Clever adds new required field to Student API response
**Expected Behavior**:
- Sync logs warning for unmapped fields
- Existing mapped fields continue syncing
- Health check remains "Healthy"
- Technical team alerted to review schema changes

---

## Out of Scope (for this feature)

- ❌ Bi-directional sync (SOS → Clever)
- ❌ Real-time sync (webhooks)
- ❌ Attendance data synchronization
- ❌ Grade/assessment synchronization
- ❌ Custom field mapping UI
- ❌ Multi-tenancy at Azure Function level (separate function apps per district)
- ❌ Data transformation rules engine
- ❌ Conflict resolution for concurrent edits

---

## Dependencies

**External Services**:
- Clever API v3.0
- Azure Key Vault
- Azure SQL Database
- Azure Application Insights
- Azure Functions (hosting platform)

**Internal Dependencies**:
- CleverSyncSOS Constitution v1.1.0
- SessionDb database (must exist)
- Per-school databases (created via separate process)

---

## References

- **Clever API Documentation**: https://dev.clever.com/docs/api-overview
- **OAuth 2.0 Client Credentials**: https://oauth.net/2/grant-types/client-credentials/
- **Azure Key Vault Best Practices**: https://learn.microsoft.com/azure/key-vault/general/best-practices
- **SpecKit Constitution**: `SpecKit/Constitution/constitution.md`
- **Implementation Plan**: `SpecKit/plan.md`
- **Tasks**: `SpecKit/tasks.md`

---

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0.0 | 2025-11-18 | Bill Martin | Initial specification created from plan analysis |

---

## Approval

**Status**: Draft - Pending Review
**Approver**: Bill Martin
**Approval Date**: TBD
