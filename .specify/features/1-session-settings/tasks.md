# Tasks: Database-Driven Session Settings Management

## Overview

Implementation tasks for enabling administrators to configure session security parameters through the Admin Portal with role-based access at System, District, and School levels.

---

## Phase 1: Database Schema

### Setup Tasks

- [ ] [DB-001] [P1] Create T-SQL script `scripts/add-district-session-columns.sql` with idempotent ALTER statements for 6 new columns on Districts table
- [ ] [DB-002] [P1] Create T-SQL script `scripts/create-system-settings-table.sql` with idempotent CREATE TABLE and singleton row initialization
- [ ] [DB-003] [P1] Execute both scripts against development SessionDb database
- [ ] [DB-004] [P1] Verify scripts are safe to run multiple times (idempotency test)

---

## Phase 2: Entity Layer

### Core Entity Tasks

- [ ] [ENT-001] [P2] Add 6 session properties to `src/CleverSyncSOS.Core/Database/SessionDb/Entities/District.cs` with XML documentation
- [ ] [ENT-002] [P2] Create new entity `src/CleverSyncSOS.Core/Database/SessionDb/Entities/SystemSettings.cs` with all properties
- [ ] [ENT-003] [P2] Update `src/CleverSyncSOS.Core/Database/SessionDb/SessionDbContext.cs` to configure SystemSettings entity
- [ ] [ENT-004] [P2] Add `DbSet<SystemSettings>` property to SessionDbContext
- [ ] [ENT-005] [P2] Configure District entity new properties in SessionDbContext OnModelCreating

---

## Phase 3: Service Layer

### Service Interface and DTOs

- [ ] [SVC-001] [P3] Create `src/CleverSyncSOS.Core/Services/ISessionSettingsService.cs` interface with all method signatures
- [ ] [SVC-002] [P3] Create DTOs in `src/CleverSyncSOS.Core/Services/SessionSettingsService.cs`:
  - SystemSessionSettingsDto
  - DistrictSessionSettingsDto
  - SchoolSessionSettingsDto
  - EffectiveSessionSettingsDto
  - SettingsScope enum
- [ ] [SVC-003] [P3] Create request DTOs:
  - UpdateSystemSettingsRequest
  - UpdateDistrictSettingsRequest
  - UpdateSchoolSettingsRequest

### Service Implementation

- [ ] [SVC-004] [P3] Implement `SessionSettingsService` class with constructor injection (IDbContextFactory, IMemoryCache, IAuditLogService, IOptions<SessionSecurityOptions>)
- [ ] [SVC-005] [P3] Implement `GetSystemSettingsAsync()` method
- [ ] [SVC-006] [P3] Implement `GetDistrictSettingsAsync(string districtId)` method
- [ ] [SVC-007] [P3] Implement `GetSchoolSettingsAsync(int schoolId)` method
- [ ] [SVC-008] [P3] Implement `GetEffectiveSettingsAsync(int? schoolId, string? districtId)` with full hierarchy resolution
- [ ] [SVC-009] [P3] Implement `UpdateSystemSettingsAsync(request, userId)` with audit logging
- [ ] [SVC-010] [P3] Implement `UpdateDistrictSettingsAsync(districtId, request, userId)` with audit logging
- [ ] [SVC-011] [P3] Implement `UpdateSchoolSettingsAsync(schoolId, request, userId)` with audit logging
- [ ] [SVC-012] [P3] Implement `ResetDistrictSettingsAsync(districtId, userId)` with audit logging
- [ ] [SVC-013] [P3] Implement `ResetSchoolSettingsAsync(schoolId, userId)` with audit logging
- [ ] [SVC-014] [P3] Implement `InvalidateCacheAsync(scope, scopeId)` method

### Caching Implementation

- [ ] [SVC-015] [P3] Implement private `GetCachedAsync<T>()` helper method with 5-minute sliding expiration
- [ ] [SVC-016] [P3] Add cache key pattern constants: `SessionSettings:System`, `SessionSettings:District:{id}`, `SessionSettings:School:{id}`

### Validation

- [ ] [SVC-017] [P3] Create `src/CleverSyncSOS.AdminPortal/Validators/UpdateSystemSettingsRequestValidator.cs` with FluentValidation rules
- [ ] [SVC-018] [P3] Create `src/CleverSyncSOS.AdminPortal/Validators/UpdateDistrictSettingsRequestValidator.cs` with FluentValidation rules
- [ ] [SVC-019] [P3] Create `src/CleverSyncSOS.AdminPortal/Validators/UpdateSchoolSettingsRequestValidator.cs` with FluentValidation rules
- [ ] [SVC-020] [P3] Add cross-field validation: IdleTimeout <= AbsoluteTimeout

---

## Phase 4: Update SessionSecurityService

### Hierarchy Integration

- [ ] [SEC-001] [P4] Modify `GetTimeoutSettingsAsync()` in `src/CleverSyncSOS.Core/Services/SessionSecurityService.cs` to include District lookup
- [ ] [SEC-002] [P4] Modify `GetMaxConcurrentSessionsAsync()` to include District lookup
- [ ] [SEC-003] [P4] Modify `IsSharedDeviceModeAsync()` to include District lookup
- [ ] [SEC-004] [P4] Modify `ShouldInvalidateAllSessionsOnLoginAsync()` to include District lookup
- [ ] [SEC-005] [P4] Add SystemSettings lookup as fallback before config defaults
- [ ] [SEC-006] [P4] Add Include for District navigation property in School queries

---

## Phase 5: Admin Portal UI

### US-1: SuperAdmin System Settings

- [ ] [UI-001] [P5] [US-1] Create `src/CleverSyncSOS.AdminPortal/Pages/Admin/SessionSettings.razor` page with route `/admin/session-settings`
- [ ] [UI-002] [P5] [US-1] Add `@attribute [Authorize(Policy = "SchoolAdmin")]` for base access
- [ ] [UI-003] [P5] [US-1] Implement System Settings section with AuthorizeView for SuperAdmin only
- [ ] [UI-004] [P5] [US-1] Add form fields for normal mode defaults (idle, absolute, concurrent, warning)
- [ ] [UI-005] [P5] [US-1] Add form fields for shared device mode defaults
- [ ] [UI-006] [P5] [US-1] Implement Save button with validation and audit logging
- [ ] [UI-007] [P5] [US-1] Implement "Reset to Config Defaults" button

### US-2: SuperAdmin District Management

- [ ] [UI-008] [P5] [US-2] Add Districts section with table listing all districts
- [ ] [UI-009] [P5] [US-2] Display current settings with inheritance indicators ("Using System Default: X")
- [ ] [UI-010] [P5] [US-2] Add Edit modal for district settings
- [ ] [UI-011] [P5] [US-2] Implement Save with validation and cache invalidation

### US-3: DistrictAdmin Settings

- [ ] [UI-012] [P5] [US-3] Add AuthorizeView section for DistrictAdmin showing only their district
- [ ] [UI-013] [P5] [US-3] Display read-only system defaults for reference
- [ ] [UI-014] [P5] [US-3] Enable edit form for district-level overrides
- [ ] [UI-015] [P5] [US-3] Show schools in district with current effective settings

### US-4: SchoolAdmin Settings

- [ ] [UI-016] [P5] [US-4] Add AuthorizeView section for SchoolAdmin showing only their school
- [ ] [UI-017] [P5] [US-4] Display inheritance chain (District → System → Config)
- [ ] [UI-018] [P5] [US-4] Enable edit form for school-level overrides
- [ ] [UI-019] [P5] [US-4] Add "Reset to District Default" button

### US-5: Settings Inheritance Display

- [ ] [UI-020] [P5] [US-5] Implement inheritance indicator badges showing source of each setting
- [ ] [UI-021] [P5] [US-5] Add tooltips explaining "Using [Source] Default" labels
- [ ] [UI-022] [P5] [US-5] Style overridden values differently from inherited values

### Navigation

- [ ] [UI-023] [P5] Add navigation link to `src/CleverSyncSOS.AdminPortal/Shared/NavMenu.razor` under Admin section
- [ ] [UI-024] [P5] Apply SchoolAdmin policy to navigation visibility

---

## Phase 6: Service Registration

### DI Configuration

- [ ] [REG-001] [P6] Register ISessionSettingsService in `src/CleverSyncSOS.AdminPortal/Program.cs`
- [ ] [REG-002] [P6] Register all FluentValidation validators in Program.cs
- [ ] [REG-003] [P6] Verify IMemoryCache is registered (should be via AddMemoryCache)

---

## Phase 7: Testing

### Unit Tests

- [ ] [TEST-001] [P7] Create `tests/CleverSyncSOS.Core.Tests/Services/SessionSettingsServiceTests.cs`
- [ ] [TEST-002] [P7] Test: School override takes precedence over district
- [ ] [TEST-003] [P7] Test: District fallback when school setting is null
- [ ] [TEST-004] [P7] Test: System fallback when district setting is null
- [ ] [TEST-005] [P7] Test: Config fallback when system setting is null
- [ ] [TEST-006] [P7] Test: Validation rejects out-of-range values
- [ ] [TEST-007] [P7] Test: Cross-field validation (idle <= absolute)
- [ ] [TEST-008] [P7] Test: Cache hit returns cached value
- [ ] [TEST-009] [P7] Test: Cache invalidation removes correct key

### Integration Tests

- [ ] [TEST-010] [P7] Create `tests/CleverSyncSOS.Integration.Tests/Services/SessionSettingsIntegrationTests.cs`
- [ ] [TEST-011] [P7] Test: SuperAdmin can update system settings
- [ ] [TEST-012] [P7] Test: SuperAdmin can update any district settings
- [ ] [TEST-013] [P7] Test: DistrictAdmin can only update own district
- [ ] [TEST-014] [P7] Test: DistrictAdmin cannot update other districts (403)
- [ ] [TEST-015] [P7] Test: SchoolAdmin can only update own school
- [ ] [TEST-016] [P7] Test: Audit log records all changes with old/new values

---

## Verification Checklist

### Constitution Compliance

- [ ] [VER-001] No EF Core migrations created (T-SQL scripts only)
- [ ] [VER-002] All scripts are idempotent
- [ ] [VER-003] No breaking changes to existing tables
- [ ] [VER-004] Clever IDs not affected
- [ ] [VER-005] Audit trail complete for all changes

### Functional Verification

- [ ] [VER-006] SuperAdmin can modify all scopes
- [ ] [VER-007] DistrictAdmin limited to own district and schools
- [ ] [VER-008] SchoolAdmin limited to own school
- [ ] [VER-009] Settings take effect immediately after save
- [ ] [VER-010] Inheritance indicators display correctly

---

## Task Dependencies

```
Phase 1 (DB) → Phase 2 (Entity) → Phase 3 (Service) → Phase 4 (Security) → Phase 5 (UI) → Phase 6 (Registration)
                                                                                              ↓
                                                                                         Phase 7 (Testing)
```

**Critical Path:** DB-001 → ENT-001 → SVC-004 → SEC-001 → UI-001 → REG-001

---

## Validation Ranges Reference

| Setting | Min | Max | Unit |
|---------|-----|-----|------|
| Idle Timeout | 5 | 120 | Minutes |
| Absolute Timeout | 30 | 1440 | Minutes |
| Max Concurrent Sessions | 1 | 10 | Count |
| Warning Period | 1 | 10 | Minutes |

**Cross-field Rule:** Idle Timeout ≤ Absolute Timeout
