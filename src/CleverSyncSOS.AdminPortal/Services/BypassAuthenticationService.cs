using System.Security.Cryptography;
using System.Text;
using Azure.Security.KeyVault.Secrets;
using CleverSyncSOS.AdminPortal.Configuration;
using Microsoft.Extensions.Options;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of bypass authentication for Super Admin
/// </summary>
public class BypassAuthenticationService : IBypassAuthenticationService
{
    private readonly SecretClient? _secretClient;
    private readonly AuthenticationOptions _authOptions;
    private readonly ILogger<BypassAuthenticationService> _logger;
    private string? _cachedPasswordHash;

    public BypassAuthenticationService(
        SecretClient? secretClient,
        IOptions<AuthenticationOptions> authOptions,
        ILogger<BypassAuthenticationService> logger)
    {
        _secretClient = secretClient;
        _authOptions = authOptions.Value;
        _logger = logger;
    }

    public async Task<bool> ValidatePasswordAsync(string providedPassword)
    {
        if (!_authOptions.BypassLoginEnabled)
        {
            _logger.LogWarning("Bypass login is disabled but validation was attempted");
            return false;
        }

        if (_secretClient == null)
        {
            _logger.LogError("Secret client is not configured for bypass authentication");
            return false;
        }

        try
        {
            // Retrieve password from Key Vault (with caching)
            // Uses new naming convention: {FunctionalName} for global secrets
            if (_cachedPasswordHash == null)
            {
                var secret = await _secretClient.GetSecretAsync(
                    CleverSyncSOS.Core.Configuration.KeyVaultSecretNaming.Global.SuperAdminPassword);
                var actualPassword = secret.Value.Value;

                // Hash the password for comparison
                _cachedPasswordHash = HashPassword(actualPassword);
            }

            // Hash the provided password
            var providedPasswordHash = HashPassword(providedPassword);

            // Perform constant-time comparison to prevent timing attacks
            var isValid = ConstantTimeEquals(_cachedPasswordHash, providedPasswordHash);

            if (isValid)
            {
                _logger.LogInformation("Bypass login successful");
            }
            else
            {
                _logger.LogWarning("Bypass login failed - invalid password");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating bypass password");
            return false;
        }
    }

    /// <summary>
    /// Hash password using SHA256
    /// </summary>
    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}
