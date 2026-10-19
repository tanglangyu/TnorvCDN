namespace TnorvCdn.Api.Services;

/// <summary>
/// 三方 DNS 提供商抽象。多厂商支持：聚合DNS、阿里云、腾讯云 DNSPod、华为云、Cloudflare。
/// 通用设计
/// </summary>
public interface IDnsProvider
{
    /// <summary>提供商标识（聚合/aggregated、阿里云/aliyun、腾讯云/dnspod、华为云/huawei、Cloudflare/cloudflare）。</summary>
    string Name { get; }

    /// <summary>验证凭证有效性（列出域名），返回 (域名名, 域名ID) 列表；失败抛异常或返回空。</summary>
    Task<List<(string Domain, string DomainId)>> ListDomainsAsync();

    /// <summary>添加或更新解析记录（存在同主机名+同类型记录则更新，否则新增）。返回记录 ID（失败为 null）。</summary>
    Task<string?> UpsertRecordAsync(string domainId, DnsRecordSpec spec);

    /// <summary>删除解析记录。</summary>
    Task<bool> DeleteRecordAsync(string domainId, string recordId);

    /// <summary>按 主机名+类型+线路 查找记录 ID（未实现返回 null）。</summary>
    Task<string?> FindRecordIdAsync(string domainId, DnsRecordSpec spec) => Task.FromResult<string?>(null);
}

/// <summary>解析记录规格。</summary>
public record DnsRecordSpec(
    string Host,       // 主机名（test / @ / *）
    string Type,       // A / CNAME / TXT ...
    string Value,      // IP 或目标域名
    int Ttl = 600,
    string Line = "default");
