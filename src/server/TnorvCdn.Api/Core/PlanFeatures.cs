using System.Text.Json;

namespace TnorvCdn.Api.Core;

/// <summary>
/// 套餐功能开关/配额矩阵：Plan.Features（JSON）的解析与读取。
/// 空/null = 全部放行（向后兼容旧套餐）；键缺失 = 放行；显式 false = 拒绝。
/// </summary>
public class PlanFeatures
{
    // 功能开关键（bool）
    public static readonly string[] FeatureKeys =
    [
        "cache", "waf", "cc", "jsShield", "urlAuth", "referers", "edgeScripts", "aiGuard",
        "basicAuth", "customPages", "webp", "websocket", "grpc", "compression",
        "https", "locations", "ns", "lb", "antiDdos", "api", "tickets", "finance",
    ];

    // 数值配额键（int，缺失=不设限）
    public static readonly string[] QuotaKeys = ["maxLocations", "maxCerts"];

    private readonly Dictionary<string, JsonElement> _map;

    public PlanFeatures(string? json)
    {
        _map = [];
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var p in doc.RootElement.EnumerateObject())
                    _map[p.Name] = p.Value.Clone();
            }
            catch { /* 解析失败按全开放处理（fail-open，与 User.Features 口径一致） */ }
        }
    }

    /// <summary>功能开关：null=未配置（放行），true/false=显式开关。</summary>
    public bool? Feature(string key) =>
        _map.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True ? true
        : _map.TryGetValue(key, out var v2) && v2.ValueKind == JsonValueKind.False ? false
        : null;

    /// <summary>数值配额：null=未设限。</summary>
    public int? Quota(string key) =>
        _map.TryGetValue(key, out var v) && v.TryGetInt32(out var i) ? i : null;

    /// <summary>合并用户级开关（管理员对单用户强制关，优先于套餐）。</summary>
    public bool? MergeUser(JsonDocument? userFeatures)
    {
        if (userFeatures == null) return null;
        foreach (var p in userFeatures.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.False)
                return false; // 用户级显式 false → 强制拒绝
        return null;
    }
}
