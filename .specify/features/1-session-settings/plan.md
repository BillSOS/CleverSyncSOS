# Implementation Plan: Database-Driven Session Settings Management

## Feature Overview

Enable administrators to configure session security parameters through the Admin Portal with role-based access at System, District, and School levels.

---

## Constitution Check

### ✅ Data Integrity
- Clever IDs not affected (session settings are internal)
- No sync-related changes
- Additive schema changes only

### ✅ Database Safety
- NO EF Core migrations - T-SQL scripts only
- Idempotent scripts with IF NOT EXISTS checks
- No breaking changes to existing tables

### ✅ Security First
- Role-based authorization enforced
- All changes logged to audit trail
- No secrets involved in settings

### ✅ Change Transparency
- Audit log records old → new values
- Source tracking shows where settings originate
- Clear UI indicators for inheritance

---

## Technical Context

### Existing Infrastructure
- `SessionSecurityService` - Core settings resolution
- `SessionSecurityOptions` - Config file defaults
- `School` entity - Already has 5 session properties
- `AuditLogService` - Ready for settings audit
- `ManageSchools.razor` - Pattern for admin UI

### New Components
- `SystemSettings` entity and table
- `District` entity extensions (6 properties)
- `ISessionSettingsService` / `SessionSettingsService`
- `SessionSettings.razor` admin page
- In-memory caching layer

---

## Implementation Phases

### Phase 1: Database Schema (T-SQL)

**Deliverables:**
1. `scripts/add-district-session-columns.sql`
2. `scripts/create-system-settings-table.sql`

**Script 1: District Columns**
```sql
-- Idempotent script to add session properties to Districts table
IF COL_LENGTH('dbo.Districts', 'IsSharedDeviceMode') IS NULL
    ALTER TABLE dbo.Districts ADD IsSharedDeviceMode BIT NULL;

IF COL_LENGTH('dbo.Districts', 'SessionIdleTimeoutMinutes') IS NULL
    ALTER TABLE dbo.Districts ADD SessionIdleTimeoutMinutes INT NULL;

IF COL_LENGTH('dbo.Districts', 'SessionAbsoluteTimeoutMinutes') IS NULL
    ALTER TABLE dbo.Districts ADD SessionAbsoluteTimeoutMinutes INT NULL;

IF COL_LENGTH('dbo.Districts', 'MaxConcurrentSessions') IS NULL
    ALTER TABLE dbo.Districts ADD MaxConcurrentSessions INT NULL;

IF COL_LENGTH('dbo.Districts', 'InvalidateAllSessionsOnLogin') IS NULL
    ALTER TABLE dbo.Districts ADD InvalidateAllSessionsOnLogin BIT NULL;

IF COL_LENGTH('dbo.Districts', 'SessionWarningMinutes') IS NULL
    ALTER TABLE dbo.Districts ADD SessionWarningMinutes INT NULL;
```

**Script 2: SystemSettings Table**
```sql
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.SystemSettings'))
BEGIN
    CREATE TABLE dbo.SystemSettings (
        Id INT NOT NULL CONSTRAINT PK_SystemSettings PRIMARY KEY,
        DefaultIdleTimeoutMinutes INT NULL,
        DefaultAbsoluteTimeoutMinutes INT NULL,
        DefaultMaxConcurrentSessions INT NULL,
        SessionWarningMinutes INT NULL,
        SharedDeviceIdleTimeoutMinutes INT NULL,
        SharedDeviceAbsoluteTimeoutMinutes INT NULL,
        SharedDeviceMaxConcurrentSessions INT NULL,
        SharedDeviceAlwaysInvalidateAllSessions BIT NULL,
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt DEFAULT GETUTCDATE(),
        UpdatedBy INT NULL,
        CONSTRAINT CK_SystemSettings_SingleRow CHECK (Id = 1)
    );

    -- Initialize singleton row
    INSERT INTO dbo.SystemSettings (Id) VALUES (1);
END
```

---

### Phase 2: Entity Layer

**Files to Modify:**

1. **`District.cs`** - Add 6 properties with XML documentation
2. **`SystemSettings.cs`** - New entity file
3. **`SessionDbContext.cs`** - Configure new entity and properties

**District.cs Additions:**
```csharp
// === Session Management Settings ===

/// <summary>
/// District-wide shared device mode policy.
/// Null = inherit from system settings.
/// </summary>
public bool? IsSharedDeviceMode { get; set; }

/// <summary>
/// District-wide idle timeout override in minutes.
/// Null = inherit from system settings.
/// Valid range: 5-120 minutes.
/// </summary>
public int? SessionIdleTimeoutMinutes { get; set; }

// ... similar for other properties
```

**SessionDbContext.cs Configuration:**
```csharp
// SystemSettings configuration
modelBuilder.Entity<SystemSettings>(entity =>
{
    entity.ToTable("SystemSettings");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).ValueGeneratedNever();
    entity.HasOne<User>()
        .WithMany()
        .HasForeignKey(e => e.UpdatedBy)
        .OnDelete(DeleteBehavior.SetNull);
});

// District additional properties
modelBuilder.Entity<District>(entity =>
{
    entity.Property(e => e.IsSharedDeviceMode);
    entity.Property(e => e.SessionIdleTimeoutMinutes);
    // ... etc
});
```

---

### Phase 3: Service Layer

**New Files:**
1. `ISessionSettingsService.cs` - Interface
2. `SessionSettingsService.cs` - Implementation
3. `SessionSettingsValidators.cs` - FluentValidation rules

**Key Methods:**

```csharp
public class SessionSettingsService : ISessionSettingsService
{
    private readonly IDbContextFactory<SessionDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly IAuditLogService _auditLog;
    private readonly IOptions<SessionSecurityOptions> _config;

    public async Task<EffectiveSessionSettingsDto> GetEffectiveSettingsAsync(
        int? schoolId, string? districtId)
    {
        // 1. Try school settings
        // 2. Try district settings
        // 3. Try system settings
        // 4. Fall back to config
    }

    public async Task UpdateDistrictSettingsAsync(
        string districtId,
        UpdateDistrictSettingsRequest request,
        int userId)
    {
        // Validate request
        // Load district
        // Track old values for audit
        // Apply changes
        // Save changes
        // Invalidate cache
        // Log to audit
    }
}
```

**Caching Implementation:**
```csharp
private async Task<T?> GetCachedAsync<T>(string key, Func<Task<T?>> factory)
{
    if (_cache.TryGetValue(key, out T? cached))
        return cached;

    var value = await factory();
    if (value != null)
    {
        _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5)
        });
    }
    return value;
}

public Task InvalidateCacheAsync(SettingsScope scope, string? scopeId)
{
    var key = scope switch
    {
        SettingsScope.System => "SessionSettings:System",
        SettingsScope.District => $"SessionSettings:District:{scopeId}",
        SettingsScope.School => $"SessionSettings:School:{scopeId}",
        _ => throw new ArgumentOutOfRangeException()
    };
    _cache.Remove(key);
    return Task.CompletedTask;
}
```

---

### Phase 4: Update SessionSecurityService

**Modify `GetTimeoutSettingsAsync`:**

Current:
```csharp
if (schoolId.HasValue)
{
    var school = await context.Schools.FindAsync(schoolId.Value);
    // Use school settings or config defaults
}
```

New:
```csharp
if (schoolId.HasValue)
{
    var school = await context.Schools
        .Include(s => s.District)
        .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value);

    // Try school setting
    var idleMinutes = school?.SessionIdleTimeoutMinutes;

    // Fall back to district
    if (!idleMinutes.HasValue && school?.District != null)
        idleMinutes = school.District.SessionIdleTimeoutMinutes;

    // Fall back to system settings
    if (!idleMinutes.HasValue)
    {
        var systemSettings = await GetSystemSettingsAsync();
        idleMinutes = isSharedMode
            ? systemSettings.SharedDeviceIdleTimeoutMinutes
            : systemSettings.DefaultIdleTimeoutMinutes;
    }

    // Fall back to config
    if (!idleMinutes.HasValue)
        idleMinutes = isSharedMode
            ? _config.SharedDeviceIdleTimeoutMinutes
            : _config.DefaultIdleTimeoutMinutes;
}
```

---

### Phase 5: Admin Portal UI

**New Page: `Pages/Admin/SessionSettings.razor`**

```razor
@page "/admin/session-settings"
@attribute [Authorize(Policy = "SchoolAdmin")]

<PageTitle>Session Settings</PageTitle>

<h3>Session Settings</h3>

<AuthorizeView Policy="SuperAdmin">
    <Authorized>
        <!-- System Settings Section -->
        <div class="card mb-4">
            <div class="card-header">
                <h5>System Defaults</h5>
            </div>
            <div class="card-body">
                @* System settings form *@
            </div>
        </div>

        <!-- All Districts -->
        <div class="card mb-4">
            <div class="card-header">
                <h5>District Settings</h5>
            </div>
            <div class="card-body">
                @* Districts table with edit *@
            </div>
        </div>
    </Authorized>
</AuthorizeView>

<AuthorizeView Policy="DistrictAdmin">
    <Authorized>
        <!-- Current District Only -->
        @* District settings for assigned district *@
    </Authorized>
</AuthorizeView>

<!-- Schools Section (filtered by role) -->
<div class="card">
    <div class="card-header">
        <h5>School Settings</h5>
    </div>
    <div class="card-body">
        @* Schools table with inheritance indicators *@
    </div>
</div>
```

**Navigation Link in `NavMenu.razor`:**
```razor
<AuthorizeView Policy="SchoolAdmin">
    <Authorized>
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="admin/session-settings">
                <span class="oi oi-cog" aria-hidden="true"></span> Session Settings
            </NavLink>
        </div>
    </Authorized>
</AuthorizeView>
```

---

### Phase 6: Service Registration

**Program.cs Additions:**
```csharp
// Session Settings Service
builder.Services.AddScoped<ISessionSettingsService, SessionSettingsService>();

// Validators
builder.Services.AddScoped<IValidator<UpdateSystemSettingsRequest>,
    UpdateSystemSettingsRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateDistrictSettingsRequest>,
    UpdateDistrictSettingsRequestValidator>();
builder.Services.AddScoped<IValidator<UpdateSchoolSettingsRequest>,
    UpdateSchoolSettingsRequestValidator>();
```

---

## Testing Strategy

### Unit Tests

1. **Settings Resolution Tests**
   - School override takes precedence
   - District fallback when school is null
   - System fallback when district is null
   - Config fallback when system is null

2. **Validation Tests**
   - Range validation (5-120, 30-1440, 1-10)
   - Cross-field validation (idle ≤ absolute)
   - Null handling

3. **Cache Tests**
   - Cache hit returns cached value
   - Cache miss fetches from database
   - Invalidation removes correct key

### Integration Tests

1. **Authorization Tests**
   - SuperAdmin can modify all scopes
   - DistrictAdmin limited to own district
   - SchoolAdmin limited to own school
   - Unauthorized access returns 403

2. **Audit Tests**
   - Changes create audit log entries
   - Old and new values recorded
   - User attribution correct

---

## Rollback Plan

1. **Database:** All changes are additive (new columns, new table)
   - Rollback: Set new columns to NULL, leave table empty
   - No data loss, existing functionality unaffected

2. **Code:** New service is isolated
   - Rollback: Remove service registration, revert SessionSecurityService
   - Existing behavior preserved

3. **UI:** New page only
   - Rollback: Remove navigation link
   - No impact on other pages

---

## Success Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Settings update latency | < 500ms | Response time logging |
| Cache hit rate | > 90% | Cache metrics |
| Audit coverage | 100% | Audit log query |
| Zero downtime | Yes | No application restart needed |
