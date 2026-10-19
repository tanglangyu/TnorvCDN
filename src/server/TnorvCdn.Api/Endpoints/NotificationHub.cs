using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 通知实时推送 Hub。认证走 JWT（query access_token，SignalR 协商自动携带）。
/// 连接后按角色加入 admin / user:{id} 组，服务端可定点或广播推送。
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var uid = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (role == "admin")
            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        if (long.TryParse(uid, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, "user:" + id);
        await base.OnConnectedAsync();
    }
}
