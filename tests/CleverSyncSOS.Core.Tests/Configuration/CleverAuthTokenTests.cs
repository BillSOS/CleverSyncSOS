// ---
// speckit:
//   type: test
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-003 Token Management
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Configuration;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Configuration;

/// <summary>
/// Unit tests for CleverAuthToken class.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-003)
/// Tests: Token expiration and refresh logic
/// </summary>
public class CleverAuthTokenTests
{
    /// <summary>
    /// Test: Token expiration detection.
    /// Source: FR-003 - Prevent expired token usage
    /// </summary>
    [Fact]
    public void IsExpired_WhenTokenIsExpired_ReturnsTrue()
    {
        // Arrange
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            IssuedAt = DateTime.UtcNow.AddHours(-2) // Issued 2 hours ago, expires in 1 hour
        };

        // Act
        var isExpired = token.IsExpired;

        // Assert
        Assert.True(isExpired);
    }

    /// <summary>
    /// Test: Token not expired when still valid.
    /// Source: FR-003 - Prevent expired token usage
    /// </summary>
    [Fact]
    public void IsExpired_WhenTokenIsValid_ReturnsFalse()
    {
        // Arrange
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            IssuedAt = DateTime.UtcNow // Just issued
        };

        // Act
        var isExpired = token.IsExpired;

        // Assert
        Assert.False(isExpired);
    }

    /// <summary>
    /// Test: Token should refresh at 75% of lifetime.
    /// Source: FR-003 - Refresh tokens proactively at 75% of their lifetime
    /// </summary>
    [Fact]
    public void ShouldRefresh_At75PercentLifetime_ReturnsTrue()
    {
        // Arrange - Token issued 46 minutes ago with 60 minute lifetime (76.7% elapsed)
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600, // 60 minutes
            IssuedAt = DateTime.UtcNow.AddMinutes(-46) // 76.7% elapsed
        };

        // Act
        var shouldRefresh = token.ShouldRefresh(75.0);

        // Assert
        Assert.True(shouldRefresh);
    }

    /// <summary>
    /// Test: Token should not refresh before 75% threshold.
    /// Source: FR-003 - Refresh tokens proactively at 75% of their lifetime
    /// </summary>
    [Fact]
    public void ShouldRefresh_Before75PercentLifetime_ReturnsFalse()
    {
        // Arrange - Token issued 30 minutes ago with 60 minute lifetime (50% elapsed)
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600, // 60 minutes
            IssuedAt = DateTime.UtcNow.AddMinutes(-30) // 50% elapsed
        };

        // Act
        var shouldRefresh = token.ShouldRefresh(75.0);

        // Assert
        Assert.False(shouldRefresh);
    }

    /// <summary>
    /// Test: Expired token should always refresh.
    /// Source: FR-003 - Prevent expired token usage
    /// </summary>
    [Fact]
    public void ShouldRefresh_WhenExpired_ReturnsTrue()
    {
        // Arrange
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            IssuedAt = DateTime.UtcNow.AddHours(-2) // Expired
        };

        // Act
        var shouldRefresh = token.ShouldRefresh(75.0);

        // Assert
        Assert.True(shouldRefresh);
    }

    /// <summary>
    /// Test: ExpiresAt calculation is correct.
    /// Source: FR-003 - Token lifetime tracking
    /// </summary>
    [Fact]
    public void ExpiresAt_CalculatesCorrectly()
    {
        // Arrange
        var issuedAt = DateTime.UtcNow;
        var expiresIn = 3600; // 1 hour
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = expiresIn,
            IssuedAt = issuedAt
        };

        // Act
        var expectedExpiresAt = issuedAt.AddSeconds(expiresIn);

        // Assert
        Assert.Equal(expectedExpiresAt, token.ExpiresAt);
    }

    /// <summary>
    /// Test: TimeUntilExpiration calculation.
    /// Source: FR-003 - Token Management
    /// </summary>
    [Fact]
    public void TimeUntilExpiration_CalculatesCorrectly()
    {
        // Arrange
        var token = new CleverAuthToken
        {
            AccessToken = "test-token",
            ExpiresIn = 3600, // 1 hour
            IssuedAt = DateTime.UtcNow.AddMinutes(-30) // 30 minutes ago
        };

        // Act
        var timeUntilExpiration = token.TimeUntilExpiration;

        // Assert - Should be approximately 30 minutes remaining
        Assert.True(timeUntilExpiration.TotalMinutes >= 29 && timeUntilExpiration.TotalMinutes <= 31);
    }
}
