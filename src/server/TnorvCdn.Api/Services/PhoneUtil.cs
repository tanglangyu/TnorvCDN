using System.Text.RegularExpressions;

namespace TnorvCdn.Api.Services;

/// <summary>
/// 手机号国别码工具：系统内手机号统一存含国别码格式（默认 0086，如 008613974668880）。
/// 输入兼容 +86 / 86 / 0086 / 裸 11 位；其它国别码（00XX…）结构合法但发码被网关侧限制拒绝。
/// </summary>
public static class PhoneUtil
{
    private static readonly Regex CcRegex = new(@"^\+?(00)?(\d{2,4})", RegexOptions.Compiled);
    private static readonly Regex CnMobile = new(@"^1[3-9]\d{9}$", RegexOptions.Compiled);

    /// <summary>归一化为含国别码存储格式。裸 11 位以 1 开头的按默认国别 0086；带国别前缀的转为 00 前缀。</summary>
    public static string Normalize(string? input, string countryCode = "0086")
    {
        var s = Regex.Replace((input ?? "").Trim(), @"[\s\-()]", "");
        if (string.IsNullOrEmpty(s)) return "";
        if (s.StartsWith('+')) s = "00" + s[1..];
        if (s.StartsWith("00")) return s;
        if (s.StartsWith("86") && s.Length >= 13) s = "00" + s;          // 8613974668880
        if (CnMobile.IsMatch(s)) return countryCode + s;                  // 裸手机号 → 默认国别
        if (s.Length > 8 && CcRegex.IsMatch(s)) return "00" + s;          // 他国裸前缀尽力归一
        return countryCode + s;
    }

    /// <summary>提取国别码（00 前缀部分），如 0086；无法识别返回空。按已知国别码最长前缀匹配。</summary>
    private static readonly string[] KnownCountryCodes = ["886", "856", "855", "853", "852", "86", "81", "82", "852"];

    public static string CountryOf(string normalized)
    {
        if (string.IsNullOrEmpty(normalized) || !normalized.StartsWith("00")) return "";
        foreach (var cc in KnownCountryCodes)
            if (normalized.StartsWith("00" + cc)) return "00" + cc;
        return normalized.Length >= 6 ? "00" + normalized[2..4] : "";
    }

    /// <summary>结构校验（仅格式，不判断是否开放）。</summary>
    public static bool IsValid(string normalized)
    {
        var cc = CountryOf(normalized);
        if (cc == "0086") return CnMobile.IsMatch(normalized[4..]);
        return normalized.Length >= 8 && normalized.Length <= 20 && normalized.All(char.IsDigit);
    }

    /// <summary>是否为当前允许发码的国别（暂只开放 0086）。</summary>
    public static bool IsSupported(string normalized) => CountryOf(normalized) == "0086";

    /// <summary>掩码：国别码 + 前 3 后 4。</summary>
    public static string Mask(string normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return "";
        if (!normalized.StartsWith("00")) return normalized;
        var cc = CountryOf(normalized);
        var rest = normalized[cc.Length..];
        return rest.Length >= 8 ? cc + rest[..3] + "****" + rest[^4..] : normalized;
    }

    /// <summary>用户录入的裸号码（用于前端“更换手机号”时判断是否变化）。</summary>
    public static string BareNumber(string normalized)
    {
        var cc = CountryOf(normalized);
        return cc.Length > 0 && normalized.StartsWith(cc) ? normalized[cc.Length..] : normalized;
    }
}
