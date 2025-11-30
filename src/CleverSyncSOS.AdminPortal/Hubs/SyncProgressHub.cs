using CleverSyncSOS.AdminPortal.Models.ViewModels;
using CleverSyncSOS.AdminPortal.Services;
using Microsoft.AspNetCore.SignalR;

namespace CleverSyncSOS.AdminPortal.Hubs;

/// <summary>
/// SignalR hub for broadcasting sync progress updates to connected clients.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SyncProgressHub : Hub
{
    private readonly ILogger<SyncProgressHub> _logger;
    private readonly IAuditLogService _auditLogService;

    public SyncProgressHub(ILogger<SyncProgressHub> logger, IAuditLogService auditLogService)
    {
        _logger = logger;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// Client subscribes to progress updates for a specific sync scope.
    /// </summary>
    public async Task SubscribeToSyncProgress(string scope)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"sync-{scope}");
        _logger.LogInformation("Client {ConnectionId} subscribed to sync progress for {Scope}",
            Context.ConnectionId, scope);
    }

    /// <summary>
    /// Client unsubscribes from progress updates.
    /// </summary>
    public async Task UnsubscribeFromSyncProgress(string scope)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sync-{scope}");
        _logger.LogInformation("Client {ConnectionId} unsubscribed from sync progress for {Scope}",
            Context.ConnectionId, scope);
    }

    /// <summary>
    /// Server-side method to broadcast progress update to all subscribed clients.
    /// Called by SyncCoordinatorService during sync operations.
    /// </summary>
    public async Task BroadcastProgress(string scope, SyncProgressUpdate progress)
    {
        await Clients.Group($"sync-{scope}").SendAsync("ReceiveProgress", progress);
    }

    /// <summary>
    /// Server-side method to broadcast sync completion to all subscribed clients.
    /// </summary>
    public async Task BroadcastCompletion(string scope, SyncResultViewModel result)
    {
        await Clients.Group($"sync-{scope}").SendAsync("ReceiveCompletion", result);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
