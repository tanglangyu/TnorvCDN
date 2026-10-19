using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;
using System.Security.Claims;
using System.Security.Cryptography;

namespace TnorvCdn.Api.Endpoints;

/// <summary>
/// 用户 API 密钥（：创建/列表/启停/删除 + AK Bearer 认证支持。
/// KeySecret 仅创建响应返回一次（存 SHA256 哈希）；认证时按 KeyId 查找并哈希比对。
/// </summary>
public static class AccessKeyEndpoints
{
    public static void MapAccessKeyEndpoints(this WebApplication app)
    {
        // 用户端（本人密钥）
        var user = app.MapGroup("/api/user").RequireAuthorization();
        user.MapGet("/accesskeys", async (CdnDbContext db, IConfiguration cfg, string? q, int? page, int? pageSize) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            var keys = await db.AccessKeys.Where(k => k.UserId == uid.Value)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new { k.Id, k.Name, k.KeyId, k.Enabled, k.LastUsedAt, k.CreatedAt })
                .ToListAsync();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.ToLower();
                keys = keys.Where(k => k.Name.ToLower().Contains(kw) || k.KeyId.ToLower().Contains(kw)).ToList();
            }
            if (page != null || pageSize != null)
            {
                var pg = Math.Clamp(page ?? 1, 1, 1000);
                var psz = Math.Clamp(pageSize ?? 20, 1, 200);
                var total = keys.Count;
                var items = keys.Skip((pg - 1) * psz).Take(psz).ToList();
                return Results.Ok(new { total, page = pg, pageSize = psz, items });
            }
            return Results.Ok(keys);
        });
        user.MapPost("/accesskeys", async (CreateKeyRequest req, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            if (!await UserServerSettingsEndpoints.FeatureEnabledAsync(db, uid.Value, "api"))
                return Results.Unauthorized();
            return await CreateAsync(db, uid.Value, req.Name);
        });
        user.MapPut("/accesskeys/{id:long}/toggle", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            var k = await db.AccessKeys.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (k == null) return Results.NotFound();
            k.Enabled = !k.Enabled;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, enabled = k.Enabled });
        });
        user.MapDelete("/accesskeys/{id:long}", async (long id, CdnDbContext db, IConfiguration cfg) =>
        {
            var uid = await UidAsync(cfg);
            if (uid == null) return Results.Unauthorized();
            var k = await db.AccessKeys.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid.Value);
            if (k == null) return Results.NotFound();
            db.AccessKeys.Remove(k);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // 管理端（全局密钥 UserId=0 + 任意用户密钥管理）
        var admin = app.MapGroup("/api/admin").RequireAuthorization();
        admin.MapGet("/accesskeys", async (CdnDbContext db, long? userId) =>
        {
            var q = db.AccessKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt);
            var list = userId.HasValue
                ? await q.Where(k => k.UserId == userId.Value).ToListAsync()
                : await q.Take(200).ToListAsync();
            // 展示 KeyId；Secret 哈希不返回
            return Results.Ok(list.Select(k => new { k.Id, k.UserId, k.Name, k.KeyId, k.Enabled, k.LastUsedAt, k.CreatedAt }));
        });
        admin.MapPost("/accesskeys", async (CreateKeyRequest req, CdnDbContext db, long? userId) =>
        {
            if (req.UserId <= 0) return Results.BadRequest("userId 必填（管理员全局密钥传 0 用 user-scoped 端点）");
            return await CreateAsync(db, req.UserId, req.Name);
        });
        admin.MapDelete("/accesskeys/{id:long}", async (long id, CdnDbContext db) =>
        {
            var k = await db.AccessKeys.FindAsync(id);
            if (k == null) return Results.NotFound();
            db.AccessKeys.Remove(k);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    private static async Task<IResult> CreateAsync(CdnDbContext db, long userId, string? name)
    {
        var keyId = "ak_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var secret = "sk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        var k = new AccessKey
        {
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(name) ? "默认密钥" : name.Trim(),
            KeyId = keyId,
            KeySecret = hash, // 存哈希
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.AccessKeys.Add(k);
        await db.SaveChangesAsync();
        // Secret 明文仅此一次返回
        return Results.Ok(new { id = k.Id, keyId, secret, name = k.Name });
    }

    /// <summary>AK Bearer 认证：Authorization: Bearer ak_xxx.sk_xxx → 校验哈希并注入 Claims。</summary>
    public static async Task<(bool ok, long? userId)> VerifyAsync(CdnDbContext db, string authHeader)
    {
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ak_")) return (false, null);
        var token = authHeader["Bearer ".Length..].Trim();
        var dot = token.IndexOf('.');
        if (dot <= 0) return (false, null);
        var keyId = token[..dot];
        var secret = token[(dot + 1)..];
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
        var k = await db.AccessKeys.FirstOrDefaultAsync(x => x.KeyId == keyId && x.Enabled);
        if (k == null || k.KeySecret != hash) return (false, null);
        k.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, k.UserId == 0 ? null : k.UserId);
    }

    private static async Task<long?> UidAsync(IConfiguration cfg)
    {
        var http = new HttpContextAccessor().HttpContext;
        var idStr = http?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? http?.User.FindFirst("sub")?.Value;
        if (long.TryParse(idStr, out var id)) return id;
        return await UserCdnExtraEndpoints.UserIdAsyncPublic(cfg);
    }

    public record CreateKeyRequest(string? Name, long UserId = 0);
}
