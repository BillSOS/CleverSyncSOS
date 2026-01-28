# Research: Database-Driven Session Settings Management

## Technical Context Analysis

### Current Implementation Summary

The CleverSyncSOS application has a mature session management infrastructure with settings resolution at multiple levels. However, settings are currently configured via code defaults and `appsettings.json`, with limited database-driven overrides at the school level only.

### Existing Settings Hierarchy

| Level | Current State | Database Support |
|-------|---------------|------------------|
| System | SessionSecurityOptions class defaults | None (config file only) |
| District | Not implemented | District entity exists, no session fields |
| School | Partial implementation | School entity has 5 session properties |
| User | Per-user override | User.MaxConcurrentSessions (nullable int) |

---

## Decision Log

### Decision 1: Extend Existing Entities vs. New SessionSettings Table

**Decision:** Extend existing District and School entities rather than creating a new SessionSettings table

**Rationale:**
- School entity already has session properties (`IsSharedDeviceMode`, `MaxConcurrentSessions`, etc.)
- Adding properties to District entity follows the same pattern
- Simpler queries (no joins needed for settings resolution)
- Existing `SessionSecurityService.GetTimeoutSettingsAsync()` already queries School entity
- Avoids data migration complexity

**Alternatives Considered:**
- New `SessionSettings` table with Scope enum (System/District/School) - rejected due to complexity and existing patterns
- Separate `DistrictSessionSettings` and `SchoolSessionSettings` tables - rejected as over-engineering

### Decision 2: System-Level Settings Storage

**Decision:** Keep system-level defaults in `appsettings.json`, store overrides in a new `SystemSettings` table

**Rationale:**
- Configuration file provides deployment-time defaults
- Database allows runtime changes without redeployment
- Fallback chain: Database → Config File → Code Defaults
- SuperAdmin can override system defaults via Admin Portal

**Alternatives Considered:**
- Store all settings in database only - rejected (would lose deployment-time configuration)
- Keep system settings in config file only - rejected (doesn't meet self-service requirement)

### Decision 3: Caching Strategy

**Decision:** Implement in-memory cache with 5-minute TTL and explicit invalidation on change

**Rationale:**
- Current implementation has no caching (queries DB every request)
- Settings rarely change (minutes/hours, not seconds)
- 5-minute TTL provides good balance of freshness vs. performance
- Explicit invalidation ensures immediate effect when admin changes settings
- IMemoryCache is already available in ASP.NET Core

**Alternatives Considered:**
- Distributed cache (Redis) - rejected (over-engineering for single-instance deployment)
- No caching - rejected (fails NFR-SS-001 performance requirement)
- Longer TTL (15+ min) - rejected (user expectation is "immediate effect")

### Decision 4: UI Architecture

**Decision:** Create a single "Session Settings" page with role-based content filtering

**Rationale:**
- SuperAdmin sees tabs: System | Districts | Schools
- DistrictAdmin sees their district and its schools
- SchoolAdmin sees only their school
- Single page reduces navigation complexity
- Consistent with ManageSchools.razor pattern

**Alternatives Considered:**
- Separate pages per scope - rejected (fragmenting user experience)
- Inline editing in existing School/District pages - rejected (clutters those pages)

---

## Technical Findings

### SessionSecurityService Analysis

**File:** `src/CleverSyncSOS.Core/Services/SessionSecurityService.cs`

**Key Methods to Modify:**

1. `GetTimeoutSettingsAsync(int? schoolId)` - Add district lookup in hierarchy
2. `GetMaxConcurrentSessionsAsync(int? schoolId)` - Add district lookup
3. `IsSharedDeviceModeAsync(int? schoolId)` - Add district lookup
4. `ShouldInvalidateAllSessionsOnLoginAsync()` - Add district lookup
5. `GetEffectiveSessionLimitAsync()` - Add district-level resolution

**Current Resolution Logic (lines 114-163):**
```
1. If schoolId provided → check School entity
2. Else → use SessionSecurityConfiguration defaults
```

**New Resolution Logic:**
```
1. If schoolId provided → check School entity
2. If school setting is null AND school has district → check District entity
3. If district setting is null → check SystemSettings table
4. If system setting is null → use SessionSecurityConfiguration defaults
```

### District Entity Extension

**File:** `src/CleverSyncSOS.Core/Database/SessionDb/Entities/District.cs`

**Properties to Add:**
```csharp
// Session Management Settings
public bool? IsSharedDeviceMode { get; set; }
public int? SessionIdleTimeoutMinutes { get; set; }
public int? SessionAbsoluteTimeoutMinutes { get; set; }
public int? MaxConcurrentSessions { get; set; }
public bool? InvalidateAllSessionsOnLogin { get; set; }
public int? SessionWarningMinutes { get; set; }
```

**Why Nullable:** Null means "inherit from system level"

### SystemSettings Entity (New)

**Purpose:** Store system-level overrides that can be changed at runtime without editing config files

**Table:** `[dbo].[SystemSettings]`

```csharp
public class SystemSettings
{
    public int Id { get; set; }  // Always 1 (singleton row)
    public int? DefaultIdleTimeoutMinutes { get; set; }
    public int? DefaultAbsoluteTimeoutMinutes { get; set; }
    public int? DefaultMaxConcurrentSessions { get; set; }
    public int? SessionWarningMinutes { get; set; }
    public int? SharedDeviceIdleTimeoutMinutes { get; set; }
    public int? SharedDeviceAbsoluteTimeoutMinutes { get; set; }
    public int? SharedDeviceMaxConcurrentSessions { get; set; }
    public bool? SharedDeviceAlwaysInvalidateAllSessions { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }  // FK to User
}
```

### Audit Logging Pattern

**Existing Pattern (from AuditLogService):**
```csharp
await _auditLogService.LogEventAsync(
    action: "UpdateSessionSettings",
    success: true,
    userId: currentUser.Id,
    userIdentifier: currentUser.Email,
    resource: $"School:{schoolId}",
    details: $"IdleTimeout: {oldValue} → {newValue}",
    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()
);
```

**Audit Actions to Implement:**
- `UpdateSystemSessionSettings`
- `UpdateDistrictSessionSettings`
- `UpdateSchoolSessionSettings`
- `ResetSessionSettingsToDefault`

### Admin Portal Page Structure

**Existing Pattern (ManageSchools.razor):**
- Table listing with inline badges
- Modal for editing
- Form validation with FluentValidation pattern
- Save/Cancel buttons with loading state

**New Page:** `Pages/Admin/SessionSettings.razor`

**Route:** `/admin/session-settings`

**Authorization:**
- Page visible to SchoolAdmin, DistrictAdmin, SuperAdmin
- Content filtered by role

---

## Database Schema Changes

### T-SQL Script Requirements (per Constitution)

- Idempotent (safe to run multiple times)
- Use IF NOT EXISTS checks
- No EF Core migrations
- Additive changes preferred

### Script 1: Add District Session Properties

```sql
-- Add session management columns to Districts table
IF COL_LENGTH('dbo.Districts', 'IsSharedDeviceMode') IS NULL
BEGIN
    ALTER TABLE dbo.Districts ADD IsSharedDeviceMode BIT NULL;
END

IF COL_LENGTH('dbo.Districts', 'SessionIdleTimeoutMinutes') IS NULL
BEGIN
    ALTER TABLE dbo.Districts ADD SessionIdleTimeoutMinutes INT NULL;
END
-- ... etc for all properties
```

### Script 2: Create SystemSettings Table

```sql
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.SystemSettings') AND type = 'U')
BEGIN
    CREATE TABLE dbo.SystemSettings (
        Id INT NOT NULL PRIMARY KEY DEFAULT 1,
        DefaultIdleTimeoutMinutes INT NULL,
        -- ... other columns
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedBy INT NULL,
        CONSTRAINT CK_SystemSettings_SingleRow CHECK (Id = 1)
    );

    -- Insert singleton row
    INSERT INTO dbo.SystemSettings (Id) VALUES (1);
END
```

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Cache inconsistency across instances | Medium | Low | 5-min TTL ensures eventual consistency |
| Breaking existing school settings | Low | High | Additive changes only, no data migration |
| Performance regression | Low | Medium | Caching + efficient queries |
| Authorization bypass | Low | High | Reuse existing role checks + new tests |

---

## Implementation Order

1. **Database Schema** - Add columns to District, create SystemSettings table
2. **Entity Updates** - Update District.cs, create SystemSettings.cs
3. **DbContext Configuration** - Configure new properties
4. **Service Layer** - Update SessionSecurityService with new hierarchy
5. **Caching Layer** - Add IMemoryCache for settings
6. **Admin UI** - Create SessionSettings.razor page
7. **Audit Integration** - Add logging for all settings changes
8. **Testing** - Unit tests for resolution hierarchy, integration tests for UI
