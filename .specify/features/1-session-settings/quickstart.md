# Quickstart: Session Settings Management Implementation

## Overview

This feature adds database-driven session settings management with a three-tier inheritance hierarchy (System → District → School) and role-based administration.

---

## Implementation Checklist

### Phase 1: Database Schema

- [ ] Create T-SQL script for District table changes
- [ ] Create T-SQL script for SystemSettings table
- [ ] Verify scripts are idempotent (safe to run multiple times)
- [ ] Execute scripts against development database

### Phase 2: Entity Layer

- [ ] Add session properties to `District.cs`
- [ ] Create `SystemSettings.cs` entity
- [ ] Update `SessionDbContext.cs` with new configurations
- [ ] Add `DbSet<SystemSettings>` property

### Phase 3: Service Layer

- [ ] Create `ISessionSettingsService` interface
- [ ] Implement `SessionSettingsService` class
- [ ] Add validators for request DTOs
- [ ] Update `SessionSecurityService` to use new hierarchy
- [ ] Add caching with `IMemoryCache`
- [ ] Register services in `Program.cs`

### Phase 4: Admin Portal UI

- [ ] Create `Pages/Admin/SessionSettings.razor`
- [ ] Add navigation link to `NavMenu.razor`
- [ ] Implement role-based content filtering
- [ ] Add form validation and error handling
- [ ] Implement "Reset to Default" functionality

### Phase 5: Testing

- [ ] Unit tests for settings resolution hierarchy
- [ ] Unit tests for validation rules
- [ ] Integration tests for authorization
- [ ] Manual testing with different roles

---

## Key Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `src/CleverSyncSOS.Core/Database/SessionDb/Entities/SystemSettings.cs` | System settings entity |
| `src/CleverSyncSOS.Core/Services/ISessionSettingsService.cs` | Service interface |
| `src/CleverSyncSOS.Core/Services/SessionSettingsService.cs` | Service implementation |
| `src/CleverSyncSOS.AdminPortal/Pages/Admin/SessionSettings.razor` | Admin UI page |
| `scripts/add-session-settings-schema.sql` | Database migration script |

### Modified Files

| File | Changes |
|------|---------|
| `District.cs` | Add 6 session properties |
| `SessionDbContext.cs` | Configure new entities/properties |
| `SessionSecurityService.cs` | Update resolution hierarchy |
| `NavMenu.razor` | Add Session Settings link |
| `Program.cs` | Register new services |

---

## Settings Resolution Order

```
1. School.{Property}           (if not null)
2. District.{Property}         (if not null)
3. SystemSettings.{Property}   (if not null)
4. SessionSecurityOptions      (appsettings.json)
5. Code Defaults               (hardcoded fallback)
```

---

## Validation Ranges

| Setting | Min | Max | Notes |
|---------|-----|-----|-------|
| Idle Timeout | 5 | 120 | Minutes |
| Absolute Timeout | 30 | 1440 | Minutes (24 hours max) |
| Max Concurrent Sessions | 1 | 10 | Per user |
| Warning Period | 1 | 10 | Minutes |

**Cross-field rule:** Idle Timeout ≤ Absolute Timeout

---

## Authorization Matrix

| Role | System | District | School |
|------|--------|----------|--------|
| SuperAdmin | Read/Write | Read/Write All | Read/Write All |
| DistrictAdmin | Read | Read/Write Own | Read/Write Own Schools |
| SchoolAdmin | Read | Read Own | Read/Write Own |

---

## Audit Trail

All changes logged with:
- Action (e.g., `UpdateDistrictSessionSettings`)
- Resource (e.g., `District:abc123`)
- Details (e.g., `IdleTimeout: 30 → 20`)
- UserId and timestamp

---

## Cache Strategy

- **Key Pattern:** `SessionSettings:{Scope}:{Id}`
- **TTL:** 5 minutes sliding expiration
- **Invalidation:** Explicit on settings update
- **Scope:** Per-instance (IMemoryCache)

---

## UI Components

### System Settings Tab (SuperAdmin only)
- Normal mode defaults (idle, absolute, concurrent, warning)
- Shared device mode defaults
- "Save" and "Reset to Config Defaults" buttons

### District Settings Tab
- List of districts with current settings
- Edit modal with inheritance indicators
- "Using System Default: X" labels for null values

### School Settings Tab
- List of schools with current settings
- Edit modal with inheritance indicators
- Badge showing "Shared Device Mode" if enabled

---

## Testing Scenarios

1. **SuperAdmin changes system idle timeout**
   - All districts/schools without overrides inherit new value
   - Audit log records change

2. **DistrictAdmin enables shared device mode for district**
   - All schools in district inherit setting
   - Schools with explicit overrides not affected

3. **SchoolAdmin resets to default**
   - School-level override removed
   - Inherits from district (or system if district null)

4. **Settings hierarchy resolution**
   - Verify correct source tracking in EffectiveSettingsDto
