namespace CleverSyncSOS.Core.Services;

/// <summary>
/// Service for converting UTC times to local times based on district timezone settings.
/// </summary>
public interface ILocalTimeService
{
    /// <summary>
    /// Gets the current local time for a school based on its district's timezone setting.
    /// </summary>
    /// <param name="schoolId">The school ID to get local time for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current local time for the school's district timezone.</returns>
    Task<DateTime> GetLocalTimeForSchoolAsync(int schoolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current local time for a district based on its timezone setting.
    /// </summary>
    /// <param name="districtId">The district ID to get local time for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current local time for the district's timezone.</returns>
    Task<DateTime> GetLocalTimeForDistrictAsync(int districtId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a UTC time to local time for a school based on its district's timezone setting.
    /// </summary>
    /// <param name="utcTime">The UTC time to convert.</param>
    /// <param name="schoolId">The school ID to get timezone for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local time for the school's district timezone.</returns>
    Task<DateTime> ConvertToLocalTimeAsync(DateTime utcTime, int schoolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a cached timezone context for a school to avoid repeated database lookups during sync.
    /// Use this when performing multiple time conversions for the same school.
    /// </summary>
    /// <param name="schoolId">The school ID to cache timezone for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A cached timezone context that provides local time without database lookups.</returns>
    Task<ISchoolTimeContext> CreateSchoolTimeContextAsync(int schoolId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cached timezone context for a school to avoid repeated database lookups during sync operations.
/// </summary>
public interface ISchoolTimeContext
{
    /// <summary>
    /// Gets the current local time using the cached timezone.
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// Converts a UTC time to local time using the cached timezone.
    /// </summary>
    /// <param name="utcTime">The UTC time to convert.</param>
    /// <returns>The local time in the cached timezone.</returns>
    DateTime ToLocal(DateTime utcTime);

    /// <summary>
    /// The timezone ID being used (e.g., "Eastern Standard Time").
    /// </summary>
    string TimeZoneId { get; }
}
