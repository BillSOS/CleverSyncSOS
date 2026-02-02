using CleverSyncSOS.Core.Database.SessionDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Services;

/// <summary>
/// Service for converting UTC times to local times based on district timezone settings.
/// </summary>
public class LocalTimeService : ILocalTimeService
{
    private readonly SessionDbContext _sessionDb;
    private readonly ILogger<LocalTimeService> _logger;

    // Default timezone if none is configured
    private const string DefaultTimeZone = "Eastern Standard Time";

    public LocalTimeService(SessionDbContext sessionDb, ILogger<LocalTimeService> logger)
    {
        _sessionDb = sessionDb;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DateTime> GetLocalTimeForSchoolAsync(int schoolId, CancellationToken cancellationToken = default)
    {
        var timeZoneId = await GetTimeZoneForSchoolAsync(schoolId, cancellationToken);
        return ConvertUtcToLocal(DateTime.UtcNow, timeZoneId);
    }

    /// <inheritdoc />
    public async Task<DateTime> GetLocalTimeForDistrictAsync(int districtId, CancellationToken cancellationToken = default)
    {
        var timeZoneId = await GetTimeZoneForDistrictAsync(districtId, cancellationToken);
        return ConvertUtcToLocal(DateTime.UtcNow, timeZoneId);
    }

    /// <inheritdoc />
    public async Task<DateTime> ConvertToLocalTimeAsync(DateTime utcTime, int schoolId, CancellationToken cancellationToken = default)
    {
        var timeZoneId = await GetTimeZoneForSchoolAsync(schoolId, cancellationToken);
        return ConvertUtcToLocal(utcTime, timeZoneId);
    }

    /// <inheritdoc />
    public async Task<ISchoolTimeContext> CreateSchoolTimeContextAsync(int schoolId, CancellationToken cancellationToken = default)
    {
        var timeZoneId = await GetTimeZoneForSchoolAsync(schoolId, cancellationToken);
        return new SchoolTimeContext(timeZoneId, _logger);
    }

    /// <summary>
    /// Gets the timezone ID for a school from its district.
    /// </summary>
    private async Task<string> GetTimeZoneForSchoolAsync(int schoolId, CancellationToken cancellationToken)
    {
        var school = await _sessionDb.Schools
            .Include(s => s.District)
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

        if (school?.District == null)
        {
            _logger.LogWarning("School {SchoolId} or its district not found. Using default timezone: {DefaultTimeZone}",
                schoolId, DefaultTimeZone);
            return DefaultTimeZone;
        }

        var timeZoneId = school.District.LocalTimeZone;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            _logger.LogWarning("District {DistrictId} has no timezone configured. Using default: {DefaultTimeZone}",
                school.District.DistrictId, DefaultTimeZone);
            return DefaultTimeZone;
        }

        return timeZoneId;
    }

    /// <summary>
    /// Gets the timezone ID for a district.
    /// </summary>
    private async Task<string> GetTimeZoneForDistrictAsync(int districtId, CancellationToken cancellationToken)
    {
        var district = await _sessionDb.Districts
            .FirstOrDefaultAsync(d => d.DistrictId == districtId, cancellationToken);

        if (district == null)
        {
            _logger.LogWarning("District {DistrictId} not found. Using default timezone: {DefaultTimeZone}",
                districtId, DefaultTimeZone);
            return DefaultTimeZone;
        }

        var timeZoneId = district.LocalTimeZone;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            _logger.LogWarning("District {DistrictId} has no timezone configured. Using default: {DefaultTimeZone}",
                districtId, DefaultTimeZone);
            return DefaultTimeZone;
        }

        return timeZoneId;
    }

    /// <summary>
    /// Converts a UTC time to local time using the specified timezone.
    /// </summary>
    private DateTime ConvertUtcToLocal(DateTime utcTime, string timeZoneId)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, timeZone);
        }
        catch (TimeZoneNotFoundException ex)
        {
            _logger.LogError(ex, "Timezone '{TimeZoneId}' not found. Using default: {DefaultTimeZone}",
                timeZoneId, DefaultTimeZone);
            var defaultTimeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, defaultTimeZone);
        }
        catch (InvalidTimeZoneException ex)
        {
            _logger.LogError(ex, "Invalid timezone '{TimeZoneId}'. Using default: {DefaultTimeZone}",
                timeZoneId, DefaultTimeZone);
            var defaultTimeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, defaultTimeZone);
        }
    }
}

/// <summary>
/// Cached timezone context for a school to avoid repeated database lookups during sync operations.
/// </summary>
public class SchoolTimeContext : ISchoolTimeContext
{
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger _logger;

    public SchoolTimeContext(string timeZoneId, ILogger logger)
    {
        TimeZoneId = timeZoneId;
        _logger = logger;

        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find timezone '{TimeZoneId}'. Using Eastern Standard Time.", timeZoneId);
            TimeZoneId = "Eastern Standard Time";
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
    }

    /// <inheritdoc />
    public string TimeZoneId { get; }

    /// <inheritdoc />
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

    /// <inheritdoc />
    public DateTime ToLocal(DateTime utcTime)
    {
        // If the time is already local (not UTC), return as-is
        if (utcTime.Kind == DateTimeKind.Local)
        {
            return utcTime;
        }

        // If unspecified, assume it's UTC
        var utc = utcTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)
            : utcTime;

        return TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);
    }
}
