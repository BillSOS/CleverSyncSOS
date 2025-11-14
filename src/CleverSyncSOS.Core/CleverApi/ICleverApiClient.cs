using CleverSyncSOS.Core.CleverApi.Models;

namespace CleverSyncSOS.Core.CleverApi;

/// <summary>
/// Interface for Clever API client operations.
/// Handles data retrieval from Clever API v3.0 with pagination and rate limiting.
/// </summary>
public interface ICleverApiClient
{
    /// <summary>
    /// Retrieves all schools for a district.
    /// </summary>
    /// <param name="districtId">Clever district identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of schools</returns>
    Task<CleverSchool[]> GetSchoolsAsync(string districtId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves students for a school, optionally filtered by last modified date.
    /// </summary>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="lastModified">Optional: Only return students modified after this date (for incremental sync)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of students</returns>
    Task<CleverStudent[]> GetStudentsAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves teachers for a school, optionally filtered by last modified date.
    /// </summary>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="lastModified">Optional: Only return teachers modified after this date (for incremental sync)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of teachers</returns>
    Task<CleverTeacher[]> GetTeachersAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default);
}
