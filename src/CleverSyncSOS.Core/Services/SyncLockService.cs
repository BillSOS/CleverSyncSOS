using CleverSyncSOS.Core.Database.SessionDb;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace CleverSyncSOS.Core.Services;

/// <summary>
/// Database-based distributed lock service for sync operations.
/// Uses SQL Server row-level locking via stored procedures.
/// </summary>
public class SyncLockService : ISyncLockService
{
    private readonly IDbContextFactory<SessionDbContext> _dbContextFactory;
    private readonly ILogger<SyncLockService> _logger;

    public SyncLockService(
        IDbContextFactory<SessionDbContext> dbContextFactory,
        ILogger<SyncLockService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<LockAcquisitionResult> TryAcquireLockAsync(
        string scope,
        string acquiredBy,
        string? initiatedBy = null,
        int durationMinutes = 30)
    {
        var lockId = Guid.NewGuid().ToString("N")[..16]; // Short unique ID
        var machineName = Environment.MachineName;

        _logger.LogInformation(
            "Attempting to acquire lock for scope '{Scope}' by {AcquiredBy} (initiated by {InitiatedBy})",
            scope, acquiredBy, initiatedBy ?? "unknown");

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_AcquireSyncLock";
            command.CommandType = CommandType.StoredProcedure;

            // Input parameters
            command.Parameters.Add(new SqlParameter("@Scope", SqlDbType.NVarChar, 100) { Value = scope });
            command.Parameters.Add(new SqlParameter("@LockId", SqlDbType.NVarChar, 50) { Value = lockId });
            command.Parameters.Add(new SqlParameter("@AcquiredBy", SqlDbType.NVarChar, 50) { Value = acquiredBy });
            command.Parameters.Add(new SqlParameter("@InitiatedBy", SqlDbType.NVarChar, 255) { Value = (object?)initiatedBy ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@LockDurationMinutes", SqlDbType.Int) { Value = durationMinutes });
            command.Parameters.Add(new SqlParameter("@MachineName", SqlDbType.NVarChar, 100) { Value = (object?)machineName ?? DBNull.Value });

            // Output parameters
            var successParam = new SqlParameter("@Success", SqlDbType.Bit) { Direction = ParameterDirection.Output };
            var currentHolderParam = new SqlParameter("@CurrentHolder", SqlDbType.NVarChar, 50) { Direction = ParameterDirection.Output };
            var currentHolderInitiatedByParam = new SqlParameter("@CurrentHolderInitiatedBy", SqlDbType.NVarChar, 255) { Direction = ParameterDirection.Output };
            var currentHolderAcquiredAtParam = new SqlParameter("@CurrentHolderAcquiredAt", SqlDbType.DateTime2) { Direction = ParameterDirection.Output };

            command.Parameters.Add(successParam);
            command.Parameters.Add(currentHolderParam);
            command.Parameters.Add(currentHolderInitiatedByParam);
            command.Parameters.Add(currentHolderAcquiredAtParam);

            await command.ExecuteNonQueryAsync();

            var success = (bool)successParam.Value;
            var currentHolder = currentHolderParam.Value == DBNull.Value ? null : (string)currentHolderParam.Value;
            var currentHolderInitiatedBy = currentHolderInitiatedByParam.Value == DBNull.Value ? null : (string)currentHolderInitiatedByParam.Value;
            var currentHolderAcquiredAt = currentHolderAcquiredAtParam.Value == DBNull.Value ? (DateTime?)null : (DateTime)currentHolderAcquiredAtParam.Value;

            if (success)
            {
                _logger.LogInformation(
                    "Lock acquired for scope '{Scope}' with LockId '{LockId}' by {AcquiredBy}",
                    scope, lockId, acquiredBy);

                return new LockAcquisitionResult
                {
                    Success = true,
                    LockId = lockId,
                    CurrentHolder = acquiredBy,
                    CurrentHolderInitiatedBy = initiatedBy,
                    CurrentHolderAcquiredAt = DateTime.UtcNow
                };
            }
            else
            {
                _logger.LogWarning(
                    "Failed to acquire lock for scope '{Scope}'. Already held by {CurrentHolder} (initiated by {InitiatedBy}) since {AcquiredAt}",
                    scope, currentHolder, currentHolderInitiatedBy, currentHolderAcquiredAt);

                return new LockAcquisitionResult
                {
                    Success = false,
                    CurrentHolder = currentHolder,
                    CurrentHolderInitiatedBy = currentHolderInitiatedBy,
                    CurrentHolderAcquiredAt = currentHolderAcquiredAt
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock for scope '{Scope}'", scope);

            return new LockAcquisitionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> ReleaseLockAsync(string scope, string lockId)
    {
        _logger.LogInformation("Releasing lock for scope '{Scope}' with LockId '{LockId}'", scope, lockId);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_ReleaseSyncLock";
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add(new SqlParameter("@Scope", SqlDbType.NVarChar, 100) { Value = scope });
            command.Parameters.Add(new SqlParameter("@LockId", SqlDbType.NVarChar, 50) { Value = lockId });

            var successParam = new SqlParameter("@Success", SqlDbType.Bit) { Direction = ParameterDirection.Output };
            command.Parameters.Add(successParam);

            await command.ExecuteNonQueryAsync();

            var success = (bool)successParam.Value;

            if (success)
            {
                _logger.LogInformation("Lock released for scope '{Scope}'", scope);
            }
            else
            {
                _logger.LogWarning("Failed to release lock for scope '{Scope}' - lock not found or not owned", scope);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for scope '{Scope}'", scope);
            return false;
        }
    }

    public async Task<bool> ExtendLockAsync(string scope, string lockId, int extendMinutes = 30)
    {
        _logger.LogDebug("Extending lock for scope '{Scope}' by {Minutes} minutes", scope, extendMinutes);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_ExtendSyncLock";
            command.CommandType = CommandType.StoredProcedure;

            command.Parameters.Add(new SqlParameter("@Scope", SqlDbType.NVarChar, 100) { Value = scope });
            command.Parameters.Add(new SqlParameter("@LockId", SqlDbType.NVarChar, 50) { Value = lockId });
            command.Parameters.Add(new SqlParameter("@ExtendMinutes", SqlDbType.Int) { Value = extendMinutes });

            var successParam = new SqlParameter("@Success", SqlDbType.Bit) { Direction = ParameterDirection.Output };
            command.Parameters.Add(successParam);

            await command.ExecuteNonQueryAsync();

            var success = (bool)successParam.Value;

            if (success)
            {
                _logger.LogDebug("Lock extended for scope '{Scope}'", scope);
            }
            else
            {
                _logger.LogWarning("Failed to extend lock for scope '{Scope}' - lock not found or not owned", scope);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extending lock for scope '{Scope}'", scope);
            return false;
        }
    }

    public async Task<bool> IsLockedAsync(string scope)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            // Check for non-expired lock
            return await dbContext.SyncLocks
                .AnyAsync(l => l.Scope == scope && l.ExpiresAt > DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking lock status for scope '{Scope}'", scope);
            return false; // Assume not locked on error to prevent blocking
        }
    }

    public async Task<LockInfo?> GetLockInfoAsync(string scope)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var lockEntry = await dbContext.SyncLocks
                .Where(l => l.Scope == scope && l.ExpiresAt > DateTime.UtcNow)
                .FirstOrDefaultAsync();

            if (lockEntry == null)
            {
                return new LockInfo
                {
                    Scope = scope,
                    IsLocked = false
                };
            }

            return new LockInfo
            {
                Scope = scope,
                IsLocked = true,
                AcquiredBy = lockEntry.AcquiredBy,
                InitiatedBy = lockEntry.InitiatedBy,
                AcquiredAt = lockEntry.AcquiredAt,
                ExpiresAt = lockEntry.ExpiresAt,
                MachineName = lockEntry.MachineName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lock info for scope '{Scope}'", scope);
            return null;
        }
    }

    public async Task<int> CleanupExpiredLocksAsync()
    {
        _logger.LogInformation("Cleaning up expired locks");

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var connection = dbContext.Database.GetDbConnection();

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_CleanupExpiredSyncLocks";
            command.CommandType = CommandType.StoredProcedure;

            var deletedCountParam = new SqlParameter("@DeletedCount", SqlDbType.Int) { Direction = ParameterDirection.Output };
            command.Parameters.Add(deletedCountParam);

            await command.ExecuteNonQueryAsync();

            var deletedCount = (int)deletedCountParam.Value;

            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} expired locks", deletedCount);
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired locks");
            return 0;
        }
    }
}
