---
speckit:
  type: data-model
  title: Clever API Authentication Data Model
  owner: Bill Martin
  version: 1.0.0
  linked_spec: ../../Specs/001-clever-api-auth/spec-1.md
  linked_plan: ../../Plans/001-clever-api-auth/plan.md
---

# Data Model: Clever API Authentication and Connection

## Overview

This document defines the key data entities used in the Clever API authentication feature. These models support secure credential handling, token lifecycle management, health monitoring, and configuration flexibility.

---

## 🧩 Entities

### 1. `CleverAuthConfiguration` [Phase: Core Implementation]

**Purpose**: Holds configuration values for authentication behavior and retry logic.

| Field | Type | Description |
| --- | --- | --- |
| `TokenEndpoint` | string | URL for Clever OAuth token exchange |
| `Scope` | string | OAuth scope string |
| `RetryPolicy` | object | Contains retry settings |
| `RetryPolicy.MaxRetries` | int | Maximum number of retries |
| `RetryPolicy.BaseDelaySeconds` | int | Initial delay for exponential backoff |
| `HealthCheckIntervalSeconds` | int | Interval for refreshing health status |

---

### 2. `CleverAuthToken` [Phase: Core Implementation]

**Purpose**: Represents an access token retrieved from Clever.

| Field | Type | Description |
| --- | --- | --- |
| `AccessToken` | string | OAuth token value |
| `ExpiresAt` | DateTime | UTC expiration timestamp |
| `Scope` | string | Scope granted by Clever |
| `TokenType` | string | Typically "Bearer" |
| `RetrievedAt` | DateTime | Timestamp when token was acquired |

---

### 3. `AuthenticationHealthStatus` [Phase: Health & Observability]

**Purpose**: Tracks the health of the authentication subsystem.

| Field | Type | Description |
| --- | --- | --- |
| `IsHealthy` | bool | Indicates if auth is functioning |
| `LastSuccessTimestamp` | DateTime | Last successful token retrieval |
| `LastErrorMessage` | string | Most recent error (if any) |
| `ErrorCount` | int | Number of consecutive failures |
| `LastCheckedAt` | DateTime | Timestamp of last health check |

---

### 4. `CredentialReference` [Phase: Core Implementation]

**Purpose**: Points to credential locations in Azure Key Vault.

| Field | Type | Description |
| --- | --- | --- |
| `ClientIdSecretName` | string | Key Vault secret name for Client ID |
| `ClientSecretName` | string | Key Vault secret name for Client Secret |
| `VaultUri` | string | URI of the Azure Key Vault instance |

---

## 🔄 Relationships

- `CleverAuthConfiguration` is injected into the authentication service via DI.
- `CleverAuthToken` is produced by the authentication service and cached in memory.
- `AuthenticationHealthStatus` is updated by the health check service and exposed via `/health/clever-auth`.
- `CredentialReference` is used by `KeyVaultCredentialStore` to locate secrets.

---

## Stage 2: Database Synchronization Entities

### Database Architecture Overview

CleverSyncSOS uses a **dual-database architecture** for data isolation and scalability:

1. **SessionDb** (Control/Orchestration Database)
   - Stores metadata about districts, schools, and sync operations
   - Single database shared across all districts/schools
   - Connection string: `CleverSyncSOS--SessionDb--ConnectionString`

2. **Per-School Databases** (Student/Teacher Data)
   - Each school has its own isolated database
   - Contains actual student and teacher records
   - Connection strings: `CleverSyncSOS--School-{SchoolName}-ConnectionString`

---

## 🗄️ SessionDb Entities (Orchestration)

### 5. `District` [Phase: Database Sync]

**Purpose**: Represents a school district with Clever API credentials.

| Field | Type | Description |
| --- | --- | --- |
| `DistrictId` | int (PK, identity) | Unique identifier |
| `CleverDistrictId` | string (unique) | Clever's district identifier |
| `Name` | string | District display name |
| `KeyVaultSecretPrefix` | string | Prefix for Key Vault secrets (e.g., "District-ABC") |
| `CreatedAt` | DateTime | Timestamp of record creation |
| `UpdatedAt` | DateTime | Timestamp of last update |

**EF Core Configuration**:
```csharp
entity.HasKey(e => e.DistrictId);
entity.HasIndex(e => e.CleverDistrictId).IsUnique();
entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
entity.Property(e => e.KeyVaultSecretPrefix).HasMaxLength(100);
```

---

### 6. `School` [Phase: Database Sync]

**Purpose**: Represents a school within a district, with reference to its dedicated database.

| Field | Type | Description |
| --- | --- | --- |
| `SchoolId` | int (PK, identity) | Unique identifier |
| `DistrictId` | nvarchar(50) (FK) | Clever district identifier (references Districts.CleverDistrictId) |
| `CleverSchoolId` | string (unique) | Clever's school identifier |
| `Name` | string | School display name |
| `DatabaseName` | string | Name of school's dedicated database |
| `KeyVaultConnectionStringSecretName` | string | Key Vault secret name for connection string |
| `IsActive` | bool | Whether school is actively syncing |
| `RequiresFullSync` | bool | Flag to force full sync on next run (e.g., beginning of school year) |
| `CreatedAt` | DateTime | Timestamp of record creation |
| `UpdatedAt` | DateTime | Timestamp of last update |

**EF Core Configuration**:
```csharp
entity.HasKey(e => e.SchoolId);
entity.HasIndex(e => e.CleverSchoolId).IsUnique();
entity.Property(e => e.DistrictId).IsRequired().HasMaxLength(50);
entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
entity.Property(e => e.DatabaseName).HasMaxLength(100);
entity.Property(e => e.KeyVaultConnectionStringSecretName).HasMaxLength(200);
entity.Property(e => e.IsActive).HasDefaultValue(true);
entity.Property(e => e.RequiresFullSync).HasDefaultValue(false);

entity.HasOne<District>()
    .WithMany()
    .HasForeignKey(e => e.DistrictId)
    .HasPrincipalKey(d => d.CleverDistrictId)
    .OnDelete(DeleteBehavior.Restrict);
```

---

### 7. `SyncHistory` [Phase: Database Sync]

**Purpose**: Tracks sync operations for auditing and incremental sync logic.

| Field | Type | Description |
| --- | --- | --- |
| `SyncId` | int (PK, identity) | Unique identifier |
| `SchoolId` | int (FK) | Foreign key to Schools table |
| `EntityType` | string | Type of entity synced ("Student", "Teacher", "Section") |
| `SyncType` | SyncType (enum) | Type of sync operation (Full, Incremental, Reconciliation) |
| `SyncStartTime` | DateTime | Sync start timestamp |
| `SyncEndTime` | DateTime? | Sync completion timestamp |
| `Status` | string | Sync status ("Success", "Failed", "Partial", "InProgress") |
| `RecordsProcessed` | int | Number of records successfully synced |
| `RecordsFailed` | int | Number of records that failed |
| `ErrorMessage` | string? | Error details if sync failed |
| `LastSyncTimestamp` | DateTime? | Timestamp for incremental sync (Clever's last_modified) |

**SyncType Enum**:
```csharp
public enum SyncType
{
    Full = 1,           // Complete refresh (new school, beginning of year)
    Incremental = 2,    // Changes only (routine sync during year)
    Reconciliation = 3  // Data integrity validation
}
```

**EF Core Configuration**:
```csharp
entity.HasKey(e => e.SyncId);
entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
entity.Property(e => e.SyncType).IsRequired().HasDefaultValue(SyncType.Incremental);
entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
entity.Property(e => e.RecordsProcessed).HasDefaultValue(0);
entity.Property(e => e.RecordsFailed).HasDefaultValue(0);

entity.HasOne<School>()
    .WithMany()
    .HasForeignKey(e => e.SchoolId)
    .OnDelete(DeleteBehavior.Restrict);

entity.HasIndex(e => new { e.SchoolId, e.EntityType, e.SyncEndTime });
```

---

## 🎓 Per-School Database Entities (Student/Teacher Data)

### 8. `Student` [Phase: Database Sync]

**Purpose**: Represents a student record synced from Clever API.

| Field | Type | Description |
| --- | --- | --- |
| `StudentId` | int (PK, identity) | Unique identifier |
| `CleverStudentId` | string (unique) | Clever's student identifier |
| `FirstName` | string | Student's first name |
| `LastName` | string | Student's last name |
| `Email` | string? | Student's email address |
| `Grade` | string? | Student's grade level |
| `StudentNumber` | string? | School's local student number |
| `LastModifiedInClever` | DateTime? | Clever's last_modified timestamp |
| `IsActive` | bool | Temporary flag used during full sync to identify records for deletion |
| `DeactivatedAt` | DateTime? | Temporary timestamp during full sync before record deletion |
| `CreatedAt` | DateTime | Timestamp of record creation |
| `UpdatedAt` | DateTime | Timestamp of last update |

**Note on IsActive/DeactivatedAt**: These fields are used during beginning-of-year full sync:
- Step 1: All students marked `IsActive = false`, `DeactivatedAt = NOW()`
- Step 2: Students in Clever reactivated `IsActive = true`, `DeactivatedAt = null`
- Step 3: Students that remain `IsActive = false` are **permanently deleted** from database
- Result: Clean database with only current students, no historical records retained

**Clever API Mapping**:
```json
{
  "id": "58da5c9a85ba240100c8d16f",
  "name": {
    "first": "John",
    "last": "Doe"
  },
  "email": "john.doe@example.com",
  "grade": "9",
  "student_number": "123456",
  "last_modified": "2023-10-15T14:30:00.000Z"
}
```

**EF Core Configuration**:
```csharp
entity.HasKey(e => e.StudentId);
entity.HasIndex(e => e.CleverStudentId).IsUnique();
entity.Property(e => e.CleverStudentId).IsRequired().HasMaxLength(50);
entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
entity.Property(e => e.Email).HasMaxLength(200);
entity.Property(e => e.Grade).HasMaxLength(20);
entity.Property(e => e.StudentNumber).HasMaxLength(50);
entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
entity.HasIndex(e => e.IsActive);  // For efficient queries of active students
```

---

### 9. `Teacher` [Phase: Database Sync]

**Purpose**: Represents a teacher record synced from Clever API.

| Field | Type | Description |
| --- | --- | --- |
| `TeacherId` | int (PK, identity) | Unique identifier |
| `CleverTeacherId` | string (unique) | Clever's teacher identifier |
| `FirstName` | string | Teacher's first name |
| `LastName` | string | Teacher's last name |
| `Email` | string | Teacher's email address |
| `Title` | string? | Teacher's job title |
| `LastModifiedInClever` | DateTime? | Clever's last_modified timestamp |
| `IsActive` | bool | Temporary flag used during full sync to identify records for deletion |
| `DeactivatedAt` | DateTime? | Temporary timestamp during full sync before record deletion |
| `CreatedAt` | DateTime | Timestamp of record creation |
| `UpdatedAt` | DateTime | Timestamp of last update |

**Note on IsActive/DeactivatedAt**: These fields are used during beginning-of-year full sync:
- Step 1: All teachers marked `IsActive = false`, `DeactivatedAt = NOW()`
- Step 2: Teachers in Clever reactivated `IsActive = true`, `DeactivatedAt = null`
- Step 3: Teachers that remain `IsActive = false` are **permanently deleted** from database
- Result: Clean database with only current teachers, no historical records retained

**Clever API Mapping**:
```json
{
  "id": "58da5d8b85ba240100c8d170",
  "name": {
    "first": "Jane",
    "last": "Smith"
  },
  "email": "jane.smith@example.com",
  "title": "Math Teacher",
  "last_modified": "2023-10-15T14:30:00.000Z"
}
```

**EF Core Configuration**:
```csharp
entity.HasKey(e => e.TeacherId);
entity.HasIndex(e => e.CleverTeacherId).IsUnique();
entity.Property(e => e.CleverTeacherId).IsRequired().HasMaxLength(50);
entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
entity.Property(e => e.Title).HasMaxLength(100);
entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
entity.HasIndex(e => e.IsActive);  // For efficient queries of active teachers
```

---

## 🔄 Data Flow Relationships

### Sync Orchestration Flow

```
1. Timer Trigger / Manual Trigger
   ↓
2. Query SessionDb.Districts
   ↓
3. For each District → Query SessionDb.Schools
   ↓
4. For each School (parallel, max 5):
   a. Read school.KeyVaultConnectionStringSecretName from SessionDb
   b. Retrieve connection string from Key Vault
   c. Connect to school's dedicated database
   d. Query SessionDb.SyncHistory for last sync timestamp
   e. Call Clever API with lastModified parameter
   f. Upsert Students/Teachers to school database
   g. Record sync results in SessionDb.SyncHistory
```

### Key Relationships

- `District` 1:N `School` (One district has many schools, FK via CleverDistrictId)
- `School` 1:N `SyncHistory` (One school has many sync operations)
- `School` 1:1 Dedicated Database (Each school maps to its own database)
- `Student` and `Teacher` entities live in per-school databases (no FK to SessionDb)

### Incremental Sync Logic

```csharp
// Query SessionDb for last successful sync
var lastSync = await sessionDb.SyncHistory
    .Where(h => h.SchoolId == schoolId
             && h.EntityType == "Student"
             && h.Status == "Success")
    .OrderByDescending(h => h.SyncEndTime)
    .FirstOrDefaultAsync();

// Use timestamp for Clever API request
var lastModified = lastSync?.LastSyncTimestamp;
var students = await cleverClient.GetStudentsAsync(schoolId, lastModified);
```

---

## 📊 Clever API Response Models

### 10. `CleverStudent` [Phase: Database Sync]

**Purpose**: DTO for Clever API student response.

| Field | Type | Description |
| --- | --- | --- |
| `Id` | string | Clever student ID |
| `Name` | CleverName | Name object (First, Last) |
| `Email` | string? | Student email |
| `Grade` | string? | Grade level |
| `StudentNumber` | string? | Local student number |
| `LastModified` | DateTime? | Clever's modification timestamp |

---

### 11. `CleverTeacher` [Phase: Database Sync]

**Purpose**: DTO for Clever API teacher response.

| Field | Type | Description |
| --- | --- | --- |
| `Id` | string | Clever teacher ID |
| `Name` | CleverName | Name object (First, Last) |
| `Email` | string | Teacher email |
| `Title` | string? | Job title |
| `LastModified` | DateTime? | Clever's modification timestamp |

---

### 12. `CleverApiResponse<T>` [Phase: Database Sync]

**Purpose**: Generic wrapper for Clever API paged responses.

| Field | Type | Description |
| --- | --- | --- |
| `Data` | T[] | Array of data items |
| `Paging` | CleverPaging | Pagination metadata |

---

## 🗂️ DbContext Classes

### `SessionDbContext` (Orchestration Database)

```csharp
public class SessionDbContext : DbContext
{
    public DbSet<District> Districts { get; set; }
    public DbSet<School> Schools { get; set; }
    public DbSet<SyncHistory> SyncHistory { get; set; }
}
```

### `SchoolDbContext` (Per-School Database)

```csharp
public class SchoolDbContext : DbContext
{
    public DbSet<Student> Students { get; set; }
    public DbSet<Teacher> Teachers { get; set; }
    // Future: public DbSet<Section> Sections { get; set; }
}
```
