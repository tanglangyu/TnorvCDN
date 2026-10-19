using TnorvCdn.Api.Data;

namespace TnorvCdn.Api.Services;

/// <summary>一级域名（注册域）备案校验：提取注册域（如 a.b.example.com → example.com），
/// 检查是否存在同注册域、备案 approved 且启用的站点。接备案 API 前以平台内已审核记录为准。</summary>
public static class IcpGate
{
    private static readonly string[] TwoLevelTld = ["com.cn","net.cn","org.cn","gov.cn","edu.cn","ac.cn"];

    public static string RegistrableDomain(string host)
    {
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        var parts = h.Split('.');
        if (parts.Length <= 2) return h;
        var last2 = string.Join(".", parts[^2..]);
        if (TwoLevelTld.Contains(last2) && parts.Length >= 3)
            return string.Join(".", parts[^3..]);
        return last2;
    }

    public static bool WildcardCovers(string pattern, string host)
    {
        if (!pattern.StartsWith('*')) return false;
        var suffix = pattern[1..]; // .example.com
        return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
