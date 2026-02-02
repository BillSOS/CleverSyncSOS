namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for bypass login authentication (Super Admin)
/// </summary>
public interface IBypassAuthenticationService
{
    /// <summary>
    /// Validates the bypass password using constant-time comparison
    /// </summary>
    /// <param name="providedPassword">The password provided by the user</param>
    /// <returns>True if password is valid, false otherwise</returns>
    Task<bool> ValidatePasswordAsync(string providedPassword);
}
