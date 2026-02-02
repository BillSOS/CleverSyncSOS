# Data Model: Session Settings Management

## Entity Changes

### 1. District Entity (Extended)

**File:** `src/CleverSyncSOS.Core/Database/SessionDb/Entities/District.cs`

**New Properties:**

| Property | Type | Nullable | Description |
|----------|------|----------|-------------|
| IsSharedDeviceMode | bool | Yes | District-wide shared device policy |
| SessionIdleTimeoutMinutes | int | Yes | Idle timeout override (5-120) |
| SessionAbsoluteTimeoutMinutes | int | Yes | Absolute timeout override (30-1440) |
| MaxConcurrentSessions | int | Yes | Max sessions per user (1-10) |
| InvalidateAllSessionsOnLogin | bool | Yes | Revoke all sessions on new login |
| SessionWarningMinutes | int | Yes | Warning period before expiration (1-10) |

**Inheritance Rule:** Null values inherit from SystemSettings

### 2. SystemSettings Entity (New)

**File:** `src/CleverSyncSOS.Core/Database/SessionDb/Entities/SystemSettings.cs`

**Purpose:** Runtime-configurable system defaults (singleton table with Id=1)

| Property | Type | Nullable | Description |
|----------|------|----------|-------------|
| Id | int | No | Always 1 (enforced by constraint) |
| DefaultIdleTimeoutMinutes | int | Yes | System default idle timeout |
| DefaultAbsoluteTimeoutMinutes | int | Yes | System default absolute timeout |
| DefaultMaxConcurrentSessions | int | Yes | System default concurrent sessions |
| SessionWarningMinutes | int | Yes | System default warning period |
| SharedDeviceIdleTimeoutMinutes | int | Yes | Shared device idle timeout |
| SharedDeviceAbsoluteTimeoutMinutes | int | Yes | Shared device absolute timeout |
| SharedDeviceMaxConcurrentSessions | int | Yes | Shared device max sessions (usually 1) |
| SharedDeviceAlwaysInvalidateAllSessions | bool | Yes | Force session invalidation in shared mode |
| UpdatedAt | DateTime | No | Last modification timestamp |
| UpdatedBy | int | Yes | FK to User who made change |

**Inheritance Rule:** Null values fall back to `SessionSecurityOptions` (appsettings.json)

### 3. School Entity (Existing - No Changes)

**File:** `src/CleverSyncSOS.Core/Database/SessionDb/Entities/School.cs`

Already has these properties:
- `IsSharedDeviceMode` (bool, not nullable)
- `SessionIdleTimeoutMinutes` (int?)
- `SessionAbsoluteTimeoutMinutes` (int?)
- `MaxConcurrentSessions` (int, default 5)
- `InvalidateAllSessionsOnLogin` (bool?)

**Inheritance Rule:** Null values inherit from District → SystemSettings → Config

---

## Settings Resolution Hierarchy

```
┌─────────────────────────────────────────────────────────────┐
│                    Settings Resolution                       │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│   School Settings (most specific)                            │
│        ↓ if null                                             │
│   District Settings                                          │
│        ↓ if null                                             │
│   SystemSettings Table                                       │
│        ↓ if null                                             │
│   SessionSecurityOptions (appsettings.json)                  │
│        ↓ if not configured                                   │
│   Hardcoded Defaults (code)                                  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## Database Schema

### Table: Districts (ALTER)

```sql
-- New columns for session management
IsSharedDeviceMode           BIT           NULL
SessionIdleTimeoutMinutes    INT           NULL
SessionAbsoluteTimeoutMinutes INT          NULL
MaxConcurrentSessions        INT           NULL
InvalidateAllSessionsOnLogin BIT           NULL
SessionWarningMinutes        INT           NULL
```

### Table: SystemSettings (CREATE)

```sql
CREATE TABLE dbo.SystemSettings (
    Id                                    INT           NOT NULL PRIMARY KEY,
    DefaultIdleTimeoutMinutes             INT           NULL,
    DefaultAbsoluteTimeoutMinutes         INT           NULL,
    DefaultMaxConcurrentSessions          INT           NULL,
    SessionWarningMinutes                 INT           NULL,
    SharedDeviceIdleTimeoutMinutes        INT           NULL,
    SharedDeviceAbsoluteTimeoutMinutes    INT           NULL,
    SharedDeviceMaxConcurrentSessions     INT           NULL,
    SharedDeviceAlwaysInvalidateAllSessions BIT         NULL,
    UpdatedAt                             DATETIME2     NOT NULL,
    UpdatedBy                             INT           NULL,

    CONSTRAINT CK_SystemSettings_SingleRow CHECK (Id = 1),
    CONSTRAINT FK_SystemSettings_UpdatedBy FOREIGN KEY (UpdatedBy)
        REFERENCES dbo.Users(UserId)
);
```

---

## Validation Rules

### Idle Timeout
- Range: 5-120 minutes
- Must be less than or equal to Absolute Timeout

### Absolute Timeout
- Range: 30-1440 minutes (30 min to 24 hours)
- Must be greater than or equal to Idle Timeout

### Max Concurrent Sessions
- Range: 1-10
- Shared device mode typically uses 1

### Session Warning Minutes
- Range: 1-10 minutes
- Must be less than Idle Timeout

---

## Entity Relationships

```
SystemSettings (1)
      │
      │ [fallback for null district values]
      ▼
Districts (*)
      │
      │ FK: Schools.DistrictId → Districts.CleverDistrictId
      │ [fallback for null school values]
      ▼
Schools (*)
      │
      │ FK: Users.SchoolId → Schools.SchoolId
      ▼
Users (*)
      │
      │ User.MaxConcurrentSessions (per-user override)
      │
      │ FK: UserSessions.UserId → Users.UserId
      ▼
UserSessions (*)
```

---

## Caching Model

### Cache Key Pattern
- `SessionSettings:System` - SystemSettings singleton
- `SessionSettings:District:{districtId}` - District settings
- `SessionSettings:School:{schoolId}` - School settings

### Cache Entry
```csharp
public record CachedSessionSettings
{
    public int? IdleTimeoutMinutes { get; init; }
    public int? AbsoluteTimeoutMinutes { get; init; }
    public int? MaxConcurrentSessions { get; init; }
    public bool? IsSharedDeviceMode { get; init; }
    public bool? InvalidateAllSessionsOnLogin { get; init; }
    public int? SessionWarningMinutes { get; init; }
    public string Source { get; init; }  // "School", "District", "System", "Config"
    public DateTime CachedAt { get; init; }
}
```

### Cache Policy
- **TTL:** 5 minutes (sliding expiration)
- **Invalidation:** Explicit removal on settings update
- **Scope:** Per-application instance (IMemoryCache)

---

## Audit Trail

### AuditLog Records for Settings Changes

| Action | Resource | Details Format |
|--------|----------|----------------|
| UpdateSystemSessionSettings | System | `{Property}: {OldValue} → {NewValue}` |
| UpdateDistrictSessionSettings | District:{id} | `{Property}: {OldValue} → {NewValue}` |
| UpdateSchoolSessionSettings | School:{id} | `{Property}: {OldValue} → {NewValue}` |
| ResetSessionSettingsToDefault | {Scope}:{id} | `Reset {Property} to inherit from {ParentScope}` |

---

## Migration Considerations

### Existing School Data
- No migration needed - existing properties remain
- New district properties default to NULL (inherit from system)

### Existing District Data
- New columns added with NULL default
- No data migration required

### SystemSettings Initialization
- Table created with single row (Id=1)
- All values NULL initially (use config file defaults)
- SuperAdmin can override via Admin Portal
