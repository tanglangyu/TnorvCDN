namespace TnorvCdn.Api.Services;

/// <summary>
/// 线路库：统一各厂商的分线路参数映射 + 省/国家级线路目录。
/// 节点 Line 字段存统一名称（如 "电信"、"广东"），各 Provider 转换为厂商专有值。
/// </summary>
public static class LineRegistry
{
    /// <summary>统一线路目录（名称 → 说明），前端线路下拉直接取用。</summary>
    public static readonly (string Name, string Desc)[] Catalog =
    {
        ("default",  "默认（全网）"),
        // 运营商（国内 ISP）
        ("电信",     "中国电信"),
        ("联通",     "中国联通"),
        ("移动",     "中国移动"),
        ("教育网",   "中国教育网"),
        ("广电",     "中国广电"),
        // 国家/地区
        ("境外",     "海外（全部境外）"),
        ("港澳台",   "港澳台地区"),
        ("美国",     "美国"),
        ("日本",     "日本"),
        ("新加坡",   "新加坡"),
        ("德国",     "德国"),
        // 省份（国内主流厂商均支持省级线路）
        ("北京","北京"), ("上海","上海"), ("广东","广东"), ("江苏","江苏"), ("浙江","浙江"),
        ("山东","山东"), ("河南","河南"), ("四川","四川"), ("湖北","湖北"), ("湖南","湖南"),
        ("福建","福建"), ("河北","河北"), ("陕西","陕西"), ("安徽","安徽"), ("辽宁","辽宁"),
    };

    /// <summary>统一名称 → 聚合DNS line 参数（直接透传统一名）。</summary>
    public static string ForAggregated(string line) => string.IsNullOrWhiteSpace(line) ? "default" : line;

    /// <summary>统一名称 → DNSPod RecordLine。</summary>
    public static string ForDnspod(string line) => line switch
    {
        "default" => "默认",
        "电信" => "电信", "联通" => "联通", "移动" => "移动", "教育网" => "教育网",
        "境外" => "境外",
        "北京" => "北京", "上海" => "上海", "广东" => "广东", "江苏" => "江苏", "浙江" => "浙江",
        "山东" => "山东", "河南" => "河南", "四川" => "四川", "湖北" => "湖北", "湖南" => "湖南",
        "福建" => "福建", "河北" => "河北", "陕西" => "陕西", "安徽" => "安徽", "辽宁" => "辽宁",
        _ => line, // 厂商原生值直接透传
    };

    /// <summary>统一名称 → 阿里云解析 Line（解析线路代码）。</summary>
    public static string ForAliyun(string line) => line switch
    {
        "default" => "default",
        "电信" => "telecom", "联通" => "unicom", "移动" => "mobile", "教育网" => "edu",
        "境外" => "oversea",
        "北京" => "cn_beijing", "上海" => "cn_shanghai", "广东" => "cn_guangdong", "江苏" => "cn_jiangsu", "浙江" => "cn_zhejiang",
        "山东" => "cn_shandong", "河南" => "cn_henan", "四川" => "cn_sichuan", "湖北" => "cn_hubei", "湖南" => "cn_hunan",
        "福建" => "cn_fujian", "河北" => "cn_hebei", "陕西" => "cn_shaanxi", "安徽" => "cn_anhui", "辽宁" => "cn_liaoning",
        _ => line,
    };

    /// <summary>统一名称 → 华为云 line。</summary>
    public static string ForHuawei(string line) => line switch
    {
        "default" => "default_view",
        "电信" => "DianXin", "联通" => "LianTong", "移动" => "YiDong",
        "境外" => "Abroad",
        _ => line,
    };

    /// <summary>统一名称 → Cloudflare（CF 无分线路，仅 default）。</summary>
    public static string ForCloudflare(string line) => "default";
}
