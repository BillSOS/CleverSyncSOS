# Service Contract: ISessionSettingsService

## Overview

New service for managing session settings at System, District, and School levels. Provides CRUD operations with authorization enforcement and audit logging.

---

## Interface Definition

```csharp
public interface ISessionSettingsService
{
    // Read Operations
    Task<SystemSessionSettingsDto> GetSystemSettingsAsync();
    Task<DistrictSessionSettingsDto> GetDistrictSettingsAsync(string districtId);
    Task<SchoolSessionSettingsDto> GetSchoolSettingsAsync(int schoolId);
    Task<EffectiveSessionSettingsDto> GetEffectiveSettingsAsync(int? schoolId, string? districtId);

    // Write Operations
    Task UpdateSystemSettingsAsync(UpdateSystemSettingsRequest request, int userId);
    Task UpdateDistrictSettingsAsync(string districtId, UpdateDistrictSettingsRequest request, int userId);
    Task UpdateSchoolSettingsAsync(int schoolId, UpdateSchoolSettingsRequest request, int userId);

    // Reset Operations
    Task ResetDistrictSettingsAsync(string districtId, int userId);
    Task ResetSchoolSettingsAsync(int schoolId, int userId);

    // Cache Management
    Task InvalidateCacheAsync(SettingsScope scope, string? scopeId = null);
}

public enum SettingsScope
{
    System,
    District,
    School
}
```

---

## Data Transfer Objects

### SystemSessionSettingsDto

```csharp
public record SystemSessionSettingsDto
{
    // Normal Mode Defaults
    public int? DefaultIdleTimeoutMinutes { get; init; }
    public int? DefaultAbsoluteTimeoutMinutes { get; init; }
    public int? DefaultMaxConcurrentSessions { get; init; }
    public int? SessionWarningMinutes { get; init; }

    // Shared Device Mode Defaults
    public int? SharedDeviceIdleTimeoutMinutes { get; init; }
    public int? SharedDeviceAbsoluteTimeoutMinutes { get; init; }
    public int? SharedDeviceMaxConcurrentSessions { get; init; }
    public bool? SharedDeviceAlwaysInvalidateAllSessions { get; init; }

    // Metadata
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedByName { get; init; }

    // Config File Defaults (read-only, for display)
    public int ConfigDefaultIdleTimeoutMinutes { get; init; }
    public int ConfigDefaultAbsoluteTimeoutMinutes { get; init; }
    public int ConfigDefaultMaxConcurrentSessions { get; init; }
}
```

### DistrictSessionSettingsDto

```csharp
public record DistrictSessionSettingsDto
{
    public string DistrictId { get; init; }
    public string DistrictName { get; init; }

    // Settings (null = inherit from system)
    public bool? IsSharedDeviceMode { get; init; }
    public int? SessionIdleTimeoutMinutes { get; init; }
    public int? SessionAbsoluteTimeoutMinutes { get; init; }
    public int? MaxConcurrentSessions { get; init; }
    public bool? InvalidateAllSessionsOnLogin { get; init; }
    public int? SessionWarningMinutes { get; init; }

    // Inheritance Info
    public SystemSessionSettingsDto SystemDefaults { get; init; }
}
```

### SchoolSessionSettingsDto

```csharp
public record SchoolSessionSettingsDto
{
    public int SchoolId { get; init; }
    public string SchoolName { get; init; }
    public string DistrictId { get; init; }

    // Settings (null = inherit from district/system)
    public bool IsSharedDeviceMode { get; init; }  // Not nullable in School entity
    public int? SessionIdleTimeoutMinutes { get; init; }
    public int? SessionAbsoluteTimeoutMinutes { get; init; }
    public int MaxConcurrentSessions { get; init; }  // Has default of 5
    public bool? InvalidateAllSessionsOnLogin { get; init; }

    // Inheritance Info
    public DistrictSessionSettingsDto? DistrictDefaults { get; init; }
}
```

### EffectiveSessionSettingsDto

```csharp
public record EffectiveSessionSettingsDto
{
    // Resolved Values
    public int IdleTimeoutMinutes { get; init; }
    public int AbsoluteTimeoutMinutes { get; init; }
    public int MaxConcurrentSessions { get; init; }
    public bool IsSharedDeviceMode { get; init; }
    public bool InvalidateAllSessionsOnLogin { get; init; }
    public int SessionWarningMinutes { get; init; }

    // Source Tracking
    public string IdleTimeoutSource { get; init; }  // "School", "District", "System", "Config"
    public string AbsoluteTimeoutSource { get; init; }
    public string MaxConcurrentSessionsSource { get; init; }
    public string SharedDeviceModeSource { get; init; }
    public string InvalidateAllSource { get; init; }
    public string WarningMinutesSource { get; init; }
}
```

---

## Request Objects

### UpdateSystemSettingsRequest

```csharp
public record UpdateSystemSettingsRequest
{
    public int? DefaultIdleTimeoutMinutes { get; init; }
    public int? DefaultAbsoluteTimeoutMinutes { get; init; }
    public int? DefaultMaxConcurrentSessions { get; init; }
    public int? SessionWarningMinutes { get; init; }
    public int? SharedDeviceIdleTimeoutMinutes { get; init; }
    public int? SharedDeviceAbsoluteTimeoutMinutes { get; init; }
    public int? SharedDeviceMaxConcurrentSessions { get; init; }
    public bool? SharedDeviceAlwaysInvalidateAllSessions { get; init; }
}
```

### UpdateDistrictSettingsRequest

```csharp
public record UpdateDistrictSettingsRequest
{
    public bool? IsSharedDeviceMode { get; init; }
    public int? SessionIdleTimeoutMinutes { get; init; }
    public int? SessionAbsoluteTimeoutMinutes { get; init; }
    public int? MaxConcurrentSessions { get; init; }
    public bool? InvalidateAllSessionsOnLogin { get; init; }
    public int? SessionWarningMinutes { get; init; }
}
```

### UpdateSchoolSettingsRequest

```csharp
public record UpdateSchoolSettingsRequest
{
    public bool IsSharedDeviceMode { get; init; }
    public int? SessionIdleTimeoutMinutes { get; init; }
    public int? SessionAbsoluteTimeoutMinutes { get; init; }
    public int MaxConcurrentSessions { get; init; }
    public bool? InvalidateAllSessionsOnLogin { get; init; }
}
```

---

## Validation Rules

### UpdateSystemSettingsRequest Validation

```csharp
public class UpdateSystemSettingsRequestValidator : AbstractValidator<UpdateSystemSettingsRequest>
{
    public UpdateSystemSettingsRequestValidator()
    {
        RuleFor(x => x.DefaultIdleTimeoutMinutes)
            .InclusiveBetween(5, 120)
            .When(x => x.DefaultIdleTimeoutMinutes.HasValue);

        RuleFor(x => x.DefaultAbsoluteTimeoutMinutes)
            .InclusiveBetween(30, 1440)
            .When(x => x.DefaultAbsoluteTimeoutMinutes.HasValue);

        RuleFor(x => x.DefaultMaxConcurrentSessions)
            .InclusiveBetween(1, 10)
            .When(x => x.DefaultMaxConcurrentSessions.HasValue);

        RuleFor(x => x.SessionWarningMinutes)
            .InclusiveBetween(1, 10)
            .When(x => x.SessionWarningMinutes.HasValue);

        // Cross-field validation
        RuleFor(x => x)
            .Must(x => !x.DefaultIdleTimeoutMinutes.HasValue ||
                       !x.DefaultAbsoluteTimeoutMinutes.HasValue ||
                       x.DefaultIdleTimeoutMinutes <= x.DefaultAbsoluteTimeoutMinutes)
            .WithMessage("Idle timeout cannot exceed absolute timeout");
    }
}
```

---

## Authorization Requirements

| Operation | SuperAdmin | DistrictAdmin | SchoolAdmin |
|-----------|------------|---------------|-------------|
| GetSystemSettings | Read | Read | Read |
| UpdateSystemSettings | Write | Deny | Deny |
| GetDistrictSettings | Read All | Read Own | Deny |
| UpdateDistrictSettings | Write All | Write Own | Deny |
| GetSchoolSettings | Read All | Read District | Read Own |
| UpdateSchoolSettings | Write All | Write District | Write Own |

---

## Audit Events

| Method | Audit Action | Resource Format |
|--------|--------------|-----------------|
| UpdateSystemSettingsAsync | UpdateSystemSessionSettings | System |
| UpdateDistrictSettingsAsync | UpdateDistrictSessionSettings | District:{id} |
| UpdateSchoolSettingsAsync | UpdateSchoolSessionSettings | School:{id} |
| ResetDistrictSettingsAsync | ResetSessionSettingsToDefault | District:{id} |
| ResetSchoolSettingsAsync | ResetSessionSettingsToDefault | School:{id} |

---

## Error Handling

| Error Condition | Exception Type | Message |
|-----------------|----------------|---------|
| District not found | ArgumentException | District with ID '{id}' not found |
| School not found | ArgumentException | School with ID '{id}' not found |
| Validation failure | ValidationException | {Property}: {Error message} |
| Unauthorized | UnauthorizedAccessException | User not authorized to modify {scope} settings |
