using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using TnorvCdn.Api.Services;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 用户端备案接口：查看自己站点的备案状态、提交/更新备案（ICP API 接入前由管理员人工审核）。
/// </summary>
public static class UserIcpEndpoints
{
    public static void MapUserIcpEndpoints(this WebApplication app)
    {
        var grp = app.MapGroup("/api/user");

        // 我的站点备案状态
        grp.MapGet("/servers/icp", async (CdnDbContext db, IConfiguration cfg, string? q, string? icpStatus, int? page, int? pageSize) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var query = db.Servers.Where(s => s.UserId == uid).AsQueryable();
            if (!string.IsNullOrWhiteSpace(icpStatus)) query = query.Where(s => s.IcpStatus == icpStatus);
            if (!string.IsNullOrWhiteSpace(q)) { var kw = q.ToLower(); query = query.Where(s => s.Domains.Any(d => d.ToLower().Contains(kw))); }
            var list = await query
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, Domains = s.Domains, s.Region, s.IcpStatus, s.IcpNumber, s.IcpNote })
                .ToListAsync();
            var shaped = list.Select(s => new
            {
                s.Id,
                domains = s.Domains,
                s.Region,
                icpStatus = string.IsNullOrEmpty(s.IcpStatus) ? "none" : s.IcpStatus,
                icpNumber = s.IcpNumber,
                icpNote = s.IcpNote,
            });
            if (page != null || pageSize != null)
            {
                var pg = Math.Clamp(page ?? 1, 1, 1000);
                var psz = Math.Clamp(pageSize ?? 20, 1, 200);
                var total = shaped.Count();
                var items = shaped.Skip((pg - 1) * psz).Take(psz).ToList();
                return Results.Ok(new { total, page = pg, pageSize = psz, items });
            }
            return Results.Ok(shaped);
        });

        // 提交/更新备案（待审核；驳回后可重新提交）
        grp.MapPost("/servers/icp/submit", async (IcpUserSubmitRequest req, CdnDbContext db, IConfiguration cfg, AuditService audit) =>
        {
            var uid = await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
            if (uid == null) return Results.Json(new { code = 401, message = "未登录" });
            var server = await db.Servers.FindAsync(req.ServerId);
            if (server == null || server.UserId != uid) return Results.Json(new { code = 404, message = "站点不存在" });
            if (server.IcpStatus == "approved") return Results.Json(new { code = 400, message = "该站点已备案，无需重复提交" });
            server.IcpStatus = "pending";
            server.IcpNumber = req.IcpNumber ?? "";
            server.IcpNote = req.Note ?? "";
            await db.SaveChangesAsync();
            _ = audit.LogAsync($"user.icp.submit {req.ServerId}", $"uid={uid} icp={server.IcpNumber}");
            return Results.Ok(new { server.Id, server.IcpStatus, server.IcpNumber });
        });
    }

    public record IcpUserSubmitRequest(long ServerId, string? IcpNumber, string? Note);
}
