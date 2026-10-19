using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 登录会话管理（个人中心"我的登录设备"）：
/// 登录成功（账号密码/手机号验证码）写入 UserSession；登出删除当前会话；
/// 下线设备/其他设备 → 删除对应行并经 NotificationHub 推送 session_kicked（前端自动登出）。
/// JWT 本身无状态：被下线的会话在 token 自然过期前仍可用，本机制以"设备管理 + 即时登出引导"为准。
/// </summary>
public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/auth");

        static long? Uid(HttpContext ctx)
        {
            var raw = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(raw, out var id) ? id : null;
        }

        // 我的登录设备
        api.MapGet("/session/list", async (HttpContext ctx, CdnDbContext db) =>
        {
            var uid = Uid(ctx);
            if (uid == null) return Results.Unauthorized();
            var items = await db.UserSessions.AsNoTracking()
                .Where(s => s.UserId == uid.Value)
                .OrderByDescending(s => s.LastSeenAt)
                .Take(50)
                .ToListAsync();
            return Results.Ok(items.Select(s => new
            {
                sessionId = s.SessionId,
                ip = s.Ip,
                device = DeviceName(s.UserAgent),
                userAgent = s.UserAgent,
                createdAt = s.CreatedAt,
                lastSeenAt = s.LastSeenAt,
            }));
        });

        // 下线指定设备
        api.MapDelete("/session/{sessionId}", async (string sessionId, HttpContext ctx, CdnDbContext db, IHubContext<NotificationHub> hub) =>
        {
            var uid = Uid(ctx);
            if (uid == null) return Results.Unauthorized();
            var s = await db.UserSessions.FirstOrDefaultAsync(x => x.UserId == uid.Value && x.SessionId == sessionId);
            if (s == null) return Results.NotFound();
            db.UserSessions.Remove(s);
            await db.SaveChangesAsync();
            await KickAsync(hub, uid.Value, sessionId);
            return Results.Ok(new { ok = true });
        });

        // 下线其他设备（保留当前：以当前请求 UA+IP 最近一次登录为准）
        api.MapDelete("/session/others", async (HttpContext ctx, CdnDbContext db, IHubContext<NotificationHub> hub) =>
        {
            var uid = Uid(ctx);
            if (uid == null) return Results.Unauthorized();
            var ua = (ctx.Request.Headers.UserAgent.FirstOrDefault() ?? "")[..Math.Min(ctx.Request.Headers.UserAgent.FirstOrDefault()?.Length ?? 0, 200)];
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
            var mine = await db.UserSessions.Where(x => x.UserId == uid.Value)
                .OrderByDescending(x => x.LastSeenAt).ToListAsync();
            var keep = mine.FirstOrDefault(x => x.UserAgent == ua && x.Ip == ip);
            var others = mine.Where(x => x.SessionId != (keep?.SessionId ?? "")).ToList();
            db.UserSessions.RemoveRange(others);
            await db.SaveChangesAsync();
            foreach (var s in others) await KickAsync(hub, uid.Value, s.SessionId);
            return Results.Ok(new { ok = true, revoked = others.Count });
        });

        // 下线全部设备
        api.MapDelete("/session/all", async (HttpContext ctx, CdnDbContext db, IHubContext<NotificationHub> hub) =>
        {
            var uid = Uid(ctx);
            if (uid == null) return Results.Unauthorized();
            var all = await db.UserSessions.Where(x => x.UserId == uid.Value).ToListAsync();
            db.UserSessions.RemoveRange(all);
            await db.SaveChangesAsync();
            foreach (var s in all) await KickAsync(hub, uid.Value, s.SessionId);
            return Results.Ok(new { ok = true, revoked = all.Count });
        });
    }

    /// <summary>登录成功后登记会话（供登录端点调用）。</summary>
    public static async Task RegisterAsync(CdnDbContext db, long userId, string sessionId, HttpRequest? req)
    {
        var old = await db.UserSessions.Where(x => x.UserId == userId && x.SessionId == sessionId).ToListAsync();
        db.UserSessions.RemoveRange(old);
        var ua = req?.Headers.UserAgent.FirstOrDefault() ?? "";
        db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            SessionId = sessionId,
            Ip = req?.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = ua[..Math.Min(ua.Length, 200)],
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        // 会话数上限：最多保留 20 条（最早的清掉）
        var all = await db.UserSessions.Where(x => x.UserId == userId).OrderByDescending(x => x.LastSeenAt).ToListAsync();
        if (all.Count > 20) db.UserSessions.RemoveRange(all.Skip(20));
        await db.SaveChangesAsync();
    }

    /// <summary>登出时移除当前会话（供登出端点调用；sessionId 由前端传回或按 UA 匹配）。</summary>
    public static async Task UnregisterAsync(CdnDbContext db, long userId, string sessionId)
    {
        var rows = await db.UserSessions.Where(x => x.UserId == userId && x.SessionId == sessionId).ToListAsync();
        db.UserSessions.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task KickAsync(IHubContext<NotificationHub> hub, long userId, string sessionId)
    {
        var payload = JsonSerializer.Serialize(new { type = "session_kicked", title = "会话已下线", content = "您的登录会话已在其他设备被下线，请重新登录", sessionId });
        await hub.Clients.Group("user:" + userId).SendAsync("session_kicked", payload);
    }

    private static string DeviceName(string ua)
    {
        if (string.IsNullOrEmpty(ua)) return "未知设备";
        if (ua.Contains("Edg/")) return "Edge 浏览器";
        if (ua.Contains("Chrome/")) return "Chrome 浏览器";
        if (ua.Contains("Firefox/")) return "Firefox 浏览器";
        if (ua.Contains("Safari/") && !ua.Contains("Chrome")) return "Safari 浏览器";
        if (ua.Contains("curl")) return "命令行 (curl)";
        return ua.Length > 40 ? ua[..40] : ua;
    }
}
