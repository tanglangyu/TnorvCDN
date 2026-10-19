using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TnorvCdn.Api.Core;
using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 套餐功能配额统一校验（PlanGate）：
/// 合并 Plan.Features（套餐级开关）与 User.Features（管理员对单用户强制关，优先）。
/// 口径：管理员放行；无配置/键缺失=放行（向后兼容）；显式 false=拒绝（403）。
/// </summary>
public static class PlanGate
{
    public static readonly string FeatureNames = "cache/waf/cc/jsShield/urlAuth/referers/edgeScripts/aiGuard/basicAuth/customPages/webp/websocket/grpc/compression/https/locations/ns/lb/antiDdos/api/tickets/finance";

    public sealed record GateResult(bool Ok, string? Reason, PlanFeatures? Plan, JsonDocument? UserFeatures);

    public static async Task<GateResult> CheckAsync(CdnDbContext db, long uid, string feature)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null) return new GateResult(false, "用户不存在", null, null);
        JsonDocument? userDoc = null;
        if (!string.IsNullOrWhiteSpace(user.Features))
        {
            try { userDoc = JsonDocument.Parse(user.Features); } catch { }
        }
        if (userDoc != null && userDoc.RootElement.ValueKind == JsonValueKind.Object
            && userDoc.RootElement.TryGetProperty(feature, out var uv) && uv.ValueKind == JsonValueKind.False)
            return new GateResult(false, "管理员已停用您的该功能", null, userDoc);

        if (user.IsAdmin) return new GateResult(true, null, null, userDoc);

        // 生效订阅的套餐开关
        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == uid && s.Enabled && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.ExpiresAt).FirstOrDefaultAsync();
        PlanFeatures? plan = null;
        if (sub != null)
        {
            var pf = await db.Plans.AsNoTracking().Where(p => p.Id == sub.PlanId).Select(p => p.Features).FirstOrDefaultAsync();
            plan = new PlanFeatures(pf);
            if (plan.Feature(feature) == false)
                return new GateResult(false, $"当前套餐未包含该功能，请升级套餐", plan, userDoc);
        }
        return new GateResult(true, null, plan, userDoc);
    }

    /// <summary>便捷判定：true=放行；false=调用方返回 403（msg 带原因）。</summary>
    public static async Task<(bool ok, string? msg)> AllowAsync(CdnDbContext db, long uid, string feature)
    {
        var r = await CheckAsync(db, uid, feature);
        return (r.Ok, r.Reason);
    }

    /// <summary>数值配额：返回 null=不设限；管理员不设限。</summary>
    public static async Task<int?> QuotaAsync(CdnDbContext db, long uid, string quotaKey)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null || user.IsAdmin) return null;
        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == uid && s.Enabled && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.ExpiresAt).FirstOrDefaultAsync();
        if (sub == null) return null;
        var pf = await db.Plans.AsNoTracking().Where(p => p.Id == sub.PlanId).Select(p => p.Features).FirstOrDefaultAsync();
        return new PlanFeatures(pf).Quota(quotaKey);
    }

    /// <summary>当前用户生效套餐的功能视图（供 /user/my-plan 返回前端禁用 UI）。</summary>
    public static async Task<object?> MyFeaturesAsync(CdnDbContext db, long uid)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null) return null;
        JsonDocument? userDoc = null;
        if (!string.IsNullOrWhiteSpace(user.Features))
        {
            try { userDoc = JsonDocument.Parse(user.Features); } catch { }
        }
        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == uid && s.Enabled && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.ExpiresAt).FirstOrDefaultAsync();
        string? planJson = null;
        string? planName = null;
        if (sub != null)
        {
            var p = await db.Plans.AsNoTracking().Where(x => x.Id == sub.PlanId)
                .Select(x => new { x.Features, x.Name }).FirstOrDefaultAsync();
            planJson = p?.Features; planName = p?.Name;
        }
        var plan = new PlanFeatures(planJson);
        var features = new Dictionary<string, bool>();
        foreach (var k in PlanFeatures.FeatureKeys)
        {
            var userOff = userDoc != null && userDoc.RootElement.ValueKind == JsonValueKind.Object
                && userDoc.RootElement.TryGetProperty(k, out var uv) && uv.ValueKind == JsonValueKind.False;
            features[k] = !userOff && plan.Feature(k) != false;
        }
        var quotas = new Dictionary<string, int?>();
        foreach (var k in PlanFeatures.QuotaKeys) quotas[k] = plan.Quota(k);
        return new { planName, features, quotas };
    }
}
