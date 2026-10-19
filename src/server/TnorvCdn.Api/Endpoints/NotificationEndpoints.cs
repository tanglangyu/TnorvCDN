using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 站内通知：REST 列表/未读数/已读/删除/发送 + SignalR 实时推送（/hubs/notification）。
/// UserId=null 的通知为广播，所有人可见；标记已读按用户维度写 IsRead。
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/notification").RequireAuthorization();

        grp.MapGet("/list", async (CdnDbContext db, HttpContext ctx, int? page, int? pageIndex, int? pageSize) =>
        {
            var uid = await UidAsync(ctx, db);
            // 兼容两套前端参数名：page（RapidDev 管理端 messages 页）与 pageIndex（通知铃组件）
            var p0 = Math.Max(1, page ?? pageIndex ?? 1); var ps = Math.Clamp((pageSize ?? 0) <= 0 ? 20 : pageSize!.Value, 1, 100);
            var q = db.Notifications.Where(n => n.UserId == null || n.UserId == uid);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(n => n.Id).Skip((p0 - 1) * ps).Take(ps)
                .Select(n => new { n.Id, n.Title, n.Content, n.Type, IsRead = n.IsRead || n.UserId == null ? n.IsRead : n.IsRead, n.CreatedAt })
                .ToListAsync();
            return Results.Ok(new { total, items });
        });

        grp.MapGet("/unread-count", async (CdnDbContext db, HttpContext ctx) =>
        {
            var uid = await UidAsync(ctx, db);
            var count = await db.Notifications.CountAsync(n => !n.IsRead && (n.UserId == null || n.UserId == uid));
            return Results.Ok(count);
        });

        grp.MapPut("/{id:long}/read", async (long id, CdnDbContext db) =>
        {
            var n = await db.Notifications.FindAsync(id);
            if (n == null) return Results.NotFound();
            n.IsRead = true;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        grp.MapPut("/read-all", async (CdnDbContext db, HttpContext ctx) =>
        {
            var uid = await UidAsync(ctx, db);
            await db.Notifications.Where(n => !n.IsRead && (n.UserId == null || n.UserId == uid))
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
            return Results.Ok();
        });

        grp.MapDelete("/{id:long}", async (long id, CdnDbContext db) =>
        {
            var n = await db.Notifications.FindAsync(id);
            if (n == null) return Results.NotFound();
            db.Notifications.Remove(n);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        grp.MapDelete("/read", async (CdnDbContext db, HttpContext ctx) =>
        {
            var uid = await UidAsync(ctx, db);
            await db.Notifications.Where(n => n.IsRead && (n.UserId == null || n.UserId == uid)).ExecuteDeleteAsync();
            return Results.Ok();
        });

        // 管理员发送通知（站内信 + SignalR 实时推送）
        grp.MapPost("/send", async (SendRequest req, CdnDbContext db, HttpContext ctx, IHubContext<NotificationHub> hub) =>
        {
            var n = new Notification
            {
                UserId = req.UserId == 0 ? null : req.UserId,
                Title = req.Title, Content = req.Content ?? "",
            };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            // 实时推送（按用户组；广播推 All）
            var payload = JsonSerializer.Serialize(new { id = n.Id, title = n.Title, content = n.Content, type = n.Type, createdAt = n.CreatedAt });
            if (n.UserId == null)
                await hub.Clients.Group("admin").SendAsync("notification", payload);
            else
                await hub.Clients.Group("user:" + n.UserId).SendAsync("notification", payload);
            return Results.Ok(new { id = n.Id });
        });
    }

    private static async Task<long?> UidAsync(HttpContext ctx, CdnDbContext db)
    {
        var idStr = ctx.User.FindFirst("sub")?.Value ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(idStr, out var id)) return id;
        // 回退：按用户名查询
        var name = ctx.User.Identity?.Name;
        if (!string.IsNullOrEmpty(name))
        {
            var u = await db.Users.FirstOrDefaultAsync(x => x.Username == name);
            return u?.Id;
        }
        return null;
    }

    public record SendRequest(long UserId, string Title, string? Content);
}
