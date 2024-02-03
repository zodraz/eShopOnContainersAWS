using Ordering.SignalrHub.Metrics;

namespace Microsoft.eShopOnContainers.Services.Ordering.SignalrHub;

[Authorize]
public class NotificationsHub : Hub
{

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Context.User.Identity.Name);
        await base.OnConnectedAsync();
        RealtimeMapMetrics.SignalRConnections.ChangeBy(1);
    }

    public override async Task OnDisconnectedAsync(Exception ex)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.User.Identity.Name);
        await base.OnDisconnectedAsync(ex);
        RealtimeMapMetrics.SignalRConnections.ChangeBy(-1);
    }
}
