# Database-Driven Session Settings Management

## Feature Summary

Enable administrators to configure session security parameters (concurrent users, sharing mode, and expiration timeouts) through the Admin Portal UI instead of requiring application configuration file changes. Settings are stored in the database and can be managed at different organizational levels based on administrator roles.

## Problem Statement

Currently, session security settings (idle timeout, absolute timeout, concurrent session limits, shared device mode) are defined in `appsettings.json` and require application redeployment to change. This creates several problems:

- **Operational friction**: IT staff must involve developers to adjust session policies
- **Inflexibility**: All schools share the same default settings unless database overrides are manually configured
- **No self-service**: District and school administrators cannot adjust settings for their specific environments
- **Audit gaps**: Configuration file changes are not tracked in the application's audit system

## Functional Requirements

### FR-SS-001: Session Settings Storage in Database

- The system MUST store all session security parameters in the SessionDb database
- Settings MUST be configurable at three levels: System (global defaults), District, and School
- School-level settings MUST override district-level settings
- District-level settings MUST override system-level settings
- The system MUST fall back to the next higher level when a setting is not defined

### FR-SS-002: Configurable Parameters

The following parameters MUST be configurable through the Admin Portal:

| Parameter | Description | Valid Range |
|-----------|-------------|-------------|
| Idle Timeout | Minutes of inactivity before session expires | 5-120 minutes |
| Absolute Timeout | Maximum session duration regardless of activity | 30-1440 minutes |
| Max Concurrent Sessions | Maximum simultaneous sessions per user | 1-10 |
| Shared Device Mode | Enable stricter security for shared computers | On/Off |
| Invalidate All Sessions on Login | Revoke all existing sessions when user logs in | On/Off |
| Session Warning Period | Minutes before expiration to show warning | 1-10 minutes |

### FR-SS-003: Role-Based Access Control for Settings

- **SuperAdmin** MUST be able to view and modify settings at all levels (System, District, School)
- **DistrictAdmin** MUST be able to view and modify settings for schools within their assigned district
- **SchoolAdmin** MUST be able to view and modify settings only for their assigned school
- Users MUST NOT be able to modify settings outside their authorization scope
- All setting changes MUST be logged in the audit trail

### FR-SS-004: Settings Management User Interface

- The Admin Portal MUST provide a dedicated "Session Settings" page accessible from the Administration menu
- The interface MUST display current effective settings and their source (System/District/School)
- The interface MUST allow authorized users to override settings at their level
- The interface MUST provide a "Reset to Default" option to remove overrides
- Changes MUST take effect immediately for new sessions (existing sessions use original settings)

### FR-SS-005: Settings Inheritance Display

- The UI MUST clearly indicate when a setting is inherited vs. explicitly set
- The UI MUST show the inheritance chain (e.g., "Using District default: 30 minutes")
- When viewing school settings, the UI MUST show both the effective value and any district/system defaults

### FR-SS-006: Validation and Constraints

- The system MUST validate all settings are within acceptable ranges before saving
- The system MUST prevent idle timeout from exceeding absolute timeout
- The system MUST display meaningful error messages for invalid configurations
- Shared device mode MUST automatically enforce stricter defaults (shorter timeouts, single concurrent session)

### FR-SS-007: Migration from Configuration File

- Existing `appsettings.json` values MUST be used as system-level defaults
- The system MUST continue reading from configuration file for any parameters not yet stored in the database
- A one-time migration MUST NOT be required; the system reads from config when database values are null

## Non-Functional Requirements

### NFR-SS-001: Performance

- Settings retrieval MUST complete within 100ms
- Settings SHOULD be cached with appropriate invalidation when changed
- Cache invalidation MUST propagate to all application instances

### NFR-SS-002: Security

- All settings changes MUST be recorded in the audit log with user, timestamp, old value, and new value
- Settings API endpoints MUST enforce role-based authorization
- Settings MUST NOT be exposed to users without appropriate administrative roles

### NFR-SS-003: Reliability

- Invalid settings MUST NOT prevent application startup
- The system MUST gracefully fall back to configuration file defaults if database is unavailable

## User Scenarios & Testing

### Scenario 1: SuperAdmin Configures System Defaults

**Given** a SuperAdmin is logged into the Admin Portal
**When** they navigate to Administration → Session Settings
**Then** they see the System Defaults section with all configurable parameters
**And** they can modify any parameter and save changes
**And** the changes are reflected as new defaults for all districts and schools without explicit overrides

### Scenario 2: DistrictAdmin Customizes District Settings

**Given** a DistrictAdmin is logged into the Admin Portal
**When** they navigate to Administration → Session Settings
**Then** they see only their district's settings (not system-wide settings)
**And** they can override parameters for their district
**And** the UI shows which values are inherited from system defaults vs. explicitly set
**And** schools in their district inherit these settings unless they have school-level overrides

### Scenario 3: SchoolAdmin Enables Shared Device Mode

**Given** a SchoolAdmin for Lincoln Elementary is logged into the Admin Portal
**When** they navigate to Administration → Session Settings
**Then** they see only Lincoln Elementary's settings
**And** they can enable "Shared Device Mode"
**And** the system automatically suggests stricter timeout values
**And** the change takes effect for new user sessions at that school

### Scenario 4: Settings Inheritance Resolution

**Given** System default idle timeout is 30 minutes
**And** District X has overridden idle timeout to 20 minutes
**And** School A (in District X) has no explicit idle timeout setting
**When** a user at School A logs in
**Then** their session uses 20-minute idle timeout (from District X)
**And** the Admin Portal shows "Using District default: 20 minutes" for School A

### Scenario 5: Reset to Default

**Given** a SchoolAdmin has previously set a custom idle timeout of 15 minutes
**When** they click "Reset to Default" for the idle timeout setting
**Then** the school-level override is removed
**And** the school inherits the district or system default
**And** the audit log records the reset action

## Success Criteria

1. **Self-Service Configuration**: District and school administrators can adjust session settings without developer involvement
2. **Immediate Effect**: New sessions use updated settings within 1 minute of configuration change
3. **Clear Visibility**: Administrators can see exactly where each setting value originates (system/district/school)
4. **Complete Audit Trail**: All setting changes are logged with before/after values and user attribution
5. **Zero Downtime**: Settings can be changed without application restart or service interruption
6. **Role Compliance**: Users cannot view or modify settings outside their authorization scope (verified by attempting unauthorized access)

## Key Entities

### SessionSettings (New Entity)

| Field | Description |
|-------|-------------|
| Id | Primary key |
| Scope | Enum: System, District, School |
| ScopeId | District ID or School ID (null for System scope) |
| IdleTimeoutMinutes | Nullable int - idle timeout override |
| AbsoluteTimeoutMinutes | Nullable int - absolute timeout override |
| MaxConcurrentSessions | Nullable int - concurrent session limit override |
| IsSharedDeviceMode | Nullable bool - shared device mode override |
| InvalidateAllSessionsOnLogin | Nullable bool - session invalidation policy override |
| SessionWarningMinutes | Nullable int - warning period override |
| CreatedAt | Timestamp |
| CreatedBy | User ID who created |
| UpdatedAt | Timestamp |
| UpdatedBy | User ID who last modified |

## Assumptions

- The existing `SessionSecurityOptions` class in `appsettings.json` will remain as the source for system-level defaults
- Settings cached in memory will be invalidated when database changes occur (cache-aside pattern)
- Existing per-school settings (`School.IsSharedDeviceMode`, `School.MaxConcurrentSessions`, etc.) will be migrated to the new `SessionSettings` entity
- The Admin Portal already has the necessary navigation structure to add a new "Session Settings" page

## Dependencies

- SessionDb database schema must be extended with the new `SessionSettings` table
- Existing `SessionSecurityService` must be updated to read from database with config file fallback
- Admin Portal routing must include the new settings management page

## Out of Scope

- Per-user session settings (only organizational levels: System, District, School)
- Real-time synchronization of settings across multiple application instances (eventual consistency via cache TTL is acceptable)
- Bulk import/export of settings
- Settings version history beyond audit log entries
