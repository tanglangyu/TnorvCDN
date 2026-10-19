using System.Text.Json;

namespace TnorvCdn.Api.Core;

/// <summary>集群：一组边缘节点，网站部署到集群后所有节点生效。</summary>
public class Cluster
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? DnsDomain { get; set; }      // 集群子域名（如 cdn1.example.com）
    public string Region { get; set; } = "global"; // 加速区域：global 全球 / cn 中国大陆 / overseas 非中国大陆
    public bool Enabled { get; set; } = true;
    public long ConfigVersion { get; set; }           // 最新已发布配置版本（节点拉取比对，队列推送递增）

    /// <summary>集群级 DDoS 防护（nftables）。EF 不映射对象本身，持久化经 DdosJson（jsonb 列）。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("ddos")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DdosConfig? Ddos
    {
        get => string.IsNullOrEmpty(DdosJson) ? null : JsonSerializer.Deserialize<DdosConfig>(DdosJson, (JsonSerializerOptions?)null);
        set => DdosJson = value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null);
    }

    /// <summary>Ddos 的 JSON 持久化列（jsonb，由 EF 映射）。API 序列化时忽略。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DdosJson { get; set; }

    /// <summary>集群阈值告警 + 健康检查配置（jsonb 持久化，通用设计。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public OpsConfig? Ops
    {
        get => string.IsNullOrEmpty(OpsJson) ? null : System.Text.Json.JsonSerializer.Deserialize<OpsConfig>(OpsJson, (System.Text.Json.JsonSerializerOptions?)null);
        set => OpsJson = value == null ? null : System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null);
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? OpsJson { get; set; }

    /// <summary>集群级默认配置套件通用设计：
    /// 站点未启用/未配置的子块用集群默认兜底（站点级优先）。持久化经 DefaultsJson（jsonb）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public ClusterDefaults? Defaults
    {
        get => string.IsNullOrEmpty(DefaultsJson) ? null : System.Text.Json.JsonSerializer.Deserialize<ClusterDefaults>(DefaultsJson, (System.Text.Json.JsonSerializerOptions?)null);
        set => DefaultsJson = value == null ? null : System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null);
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DefaultsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>集群默认配置套件：站点级为空/未启用的子块由集群默认下发兜底。</summary>
public class ClusterDefaults
{
    public CacheConfig? Cache { get; set; }              // 站点 cache 未启用时兜底
    public SecurityConfig? Security { get; set; }        // 站点安全全空时兜底（IP名单/CC/WAF规则合并）
    public AdvancedConfig? Advanced { get; set; }        // 站点高级未配置字段兜底（压缩/WebP/字符集/gRPC/自定义页面等）
}

/// <summary>高防 IP 实例管理：防护实例/线路/峰值带宽/绑定防护对象。</summary>
public class ProtectionInstance
{
    public long Id { get; set; }
    public long UserId { get; set; }                 // 所属用户（用户端购买归属）
    public string Name { get; set; } = "";          // 实例名（如 高防-华东-100G）
    public string Ip { get; set; } = "";            // 高防 IP
    public string Line { get; set; } = "BGP";       // 线路（BGP/三线/海外）
    public long PeakMbps { get; set; } = 1000;      // 峰值防护带宽
    public long ProtectedMbps { get; set; } = 100;  // 保底带宽
    public string ProtectedTargets { get; set; } = "";  // 防护对象（网站域名，逗号分隔）
    public string Status { get; set; } = "active";  // active / expired
    public DateTime? ExpireAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>工单（控制台工单系统）。</summary>
public class Ticket
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Status { get; set; } = "open";       // open / replied / closed
    public string Priority { get; set; } = "medium";   // low / medium / high
    public long UserId { get; set; }
    public string? Reply { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = "";        // U6：工单分类（账号/计费/网站/DNS/其他）
    public List<TicketMessage> Messages { get; set; } = [];  // U6：对话时间线（用户/客服逐条）
}

/// <summary>工单对话消息（U6 气泡时间线）。</summary>
public class TicketMessage
{
    public string From { get; set; } = "user";   // user / staff
    public string Content { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>优惠券。</summary>
public class Coupon
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public int DiscountPercent { get; set; } = 10;
    public int MaxUses { get; set; } = 100;
    public int Used { get; set; }
    public DateTime ExpireAt { get; set; } = DateTime.UtcNow.AddMonths(3);
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>注册邀请码。</summary>
public class Invitation
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public int MaxUses { get; set; } = 1;
    public int Used { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>智能 DNS 域名（NS 域名库）。</summary>
public class NsDomain
{
    public long Id { get; set; }
    public long UserId { get; set; }                 // 所属用户（用户端 CRUD 归属）
    public string Name { get; set; } = "";
    public string? Group { get; set; }
    public string Line { get; set; } = "default";
    public string Type { get; set; } = "A";          // A / AAAA / CNAME / TXT / NS
    public string Value { get; set; } = "";          // 记录值
    public int Ttl { get; set; } = 600;
    public string Status { get; set; } = "active";
    public string SyncStatus { get; set; } = "";   // synced=三方DNS已生效 / local=仅本站 / failed:xxx
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // ---- NS 托管接入通用设计：记录 kind=record 普通解析 / delegation 整域 NS 托管 ----
    public string Kind { get; set; } = "record";
    public long ServerId { get; set; }             // delegation 时关联的站点（0=无）；解析直接应答该站点可用节点 IP
    public string Delegated { get; set; } = "";    // "" 未检测 / checking / ok 委派已生效 / failed 未生效
    public DateTime? DelegatedCheckedAt { get; set; }
}

/// <summary>节点 SSH 授权（节点批量安装/远程运维）。</summary>
public class SshGrant
{
    public long Id { get; set; }
    public string NodeName { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "22";
    public string Username { get; set; } = "root";
    public string AuthType { get; set; } = "key";  // key / password
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>流量包（可售）。</summary>
public class FlowPackage
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long Bytes { get; set; } = 1073741824;   // 1GB
    public decimal PriceCents { get; set; } = 990;
    public bool Enabled { get; set; } = true;
}

/// <summary>区域探测记录：节点对站点的可访问性探测结果。</summary>
public class ProbeRecord
{
    public long Id { get; set; }
    public long NodeId { get; set; }
    public long ServerId { get; set; }
    public bool Ok { get; set; }
    public int Status { get; set; }
    public double LatencyMs { get; set; }
    public string Error { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>DDoS 防护配置（集群级，nftables 内核层限速）。</summary>
public class DdosConfig
{
    public bool Enabled { get; set; }
    public int ConnLimit { get; set; } = 100;      // 单 IP 最大并发连接数
    public int ConnRate { get; set; } = 50;        // 单 IP 每秒新建连接数
    public int BurstRate { get; set; } = 100;      // 速率突发桶
    public int SynFloodLimit { get; set; } = 200;  // SYN 每秒上限
    public int[] Ports { get; set; } = [80, 443];  // 生效端口
    public List<string> AdminAllowCidrs { get; set; } = [];  // network-security：管理网段白名单（永远放行）
}

/// <summary>边缘节点。</summary>
public class Node
{
    public long Id { get; set; }
    public long ClusterId { get; set; }
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";     // 注册后下发，节点通信凭证
    public string? InstallToken { get; set; }   // 未注册时用于自动注册
    public string[] PublicIps { get; set; } = [];
    public string? Hostname { get; set; }
    public string? Version { get; set; }
    public string Role { get; set; } = "edge";  // edge（边缘）/ l2（二级汇聚/就近回源）
    public string Line { get; set; } = "default"; // 智能DNS线路：default/telecom/unicom/mobile/inland/overseas
    public bool IsOnline { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>用户（多租户）。</summary>
public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool Enabled { get; set; } = true;
    public decimal BalanceCents { get; set; }        // 账户余额（分）
    public string? Features { get; set; }            // 用户功能开关 JSON通用设计
    public string SmsPhone { get; set; } = "";       // 绑定手机号（短信验证码校验后写入；手机号登录凭据）
    public string SmsEvents { get; set; } = "[]";    // 用户订阅的提醒项 JSON 数组（与全局启用项取交集生效）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>登录会话（个人中心"我的登录设备"）：登录成功写入，登出/下线删除；JWT 无状态，本表仅做设备管理与踢下线展示。</summary>
public class UserSession
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string SessionId { get; set; } = "";
    public string Ip { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

/// <summary>用户 API 密钥通用设计：
/// 用于 OpenAPI 调用（Bearer AK 或 HMAC 签名，此处简化为 Bearer）。</summary>
public class AccessKey
{
    public long Id { get; set; }
    public long UserId { get; set; }                 // 0 = 管理员全局密钥
    public string Name { get; set; } = "";           // 备注名
    public string KeyId { get; set; } = "";          // 公开标识（列表展示）
    public string KeySecret { get; set; } = "";      // 密钥（创建时一次可见，存哈希）
    public bool Enabled { get; set; } = true;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>用户实名认证通用设计。</summary>
public class UserIdentity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Type { get; set; } = "personal";   // personal / enterprise
    public string RealName { get; set; } = "";       // 个人姓名 / 企业联系人
    public string IdNumber { get; set; } = "";       // 证件号 / 统一社会信用代码
    public string EnterpriseName { get; set; } = ""; // 企业名称（enterprise 时）
    public string Status { get; set; } = "pending";  // pending / approved / rejected
    public string Note { get; set; } = "";           // 审核备注
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>充值/购买订单（余额支付；支付宝/微信扫码支付经 PayGateway 回调入账）。</summary>
public class Order
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string OrderNo { get; set; } = "";        // T + yyyyMMddHHmmss + 随机
    public string Kind { get; set; } = "recharge";   // recharge / plan / flow_package
    public long? PlanId { get; set; }                // kind=plan 时目标套餐
    public long? FlowPackageId { get; set; }
    public int Months { get; set; } = 1;             // 购买月数
    public decimal AmountCents { get; set; }
    public string Status { get; set; } = "pending";  // pending / paid / cancelled
    public string PayMethod { get; set; } = "balance"; // balance / alipay / wechat
    public string? CouponCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}

/// <summary>网站（加速域名服务）。</summary>
public class Server
{
    public long Id { get; set; }
    public long ClusterId { get; set; }
    public long UserId { get; set; }
    public string[] Domains { get; set; } = [];      // 支持通配符 *.example.com（JSON 列存储）
    public string DomainsJson { get; set; } = "";    // EF 转换辅助，不使用
    public string Region { get; set; } = "global";  // 加速区域：global 全球 / cn 中国大陆 / overseas 非中国大陆
    // ---- 域名备案（中国大陆节点必须备案；接入备案 API 前由管理员人工审核）----
    public string IcpStatus { get; set; } = "none";   // none 未提交 / pending 待审核 / approved 已备案 / rejected 已驳回
    public string IcpNumber { get; set; } = "";       // 备案号（如 粤ICP备XXXXXXXX号）
    public string IcpNote { get; set; } = "";         // 审核/驳回备注
    public string ServerType { get; set; } = "http";   // http / tcp / tls / udp（L4 负载均衡类型）
    public string? Group { get; set; }                 // 站点分组标签（用户端自助管理）
    public int[] HttpPorts { get; set; } = [80];
    public int[] L4Ports { get; set; } = [];           // L4 类型时的监听端口（支持范围语法在 P1 展开）
    public bool L4ProxyProtocol { get; set; }              // L4 转发发 PROXY protocol v2（TOA 用户态等价：传递真实客户端 IP）
    public int[] HttpsPorts { get; set; } = [];
    public bool ForceHttps { get; set; }
    public string SslMode { get; set; } = "auto";    // auto=ACME自动签发 / manual=手动上传证书 / off=不启用HTTPS
    // ---- 接入方式：cname=CNAME接入（默认，解析指向集群接入域名）/ ns=NS托管接入（智能DNS直接应答节点IP，无CNAME拉平）----
    public string AccessMode { get; set; } = "cname";
    public DateTime? AccessModeChangedAt { get; set; }   // 最近一次切换时间（NS托管生效判定用）
    // ---- 下发策略：false=默认批量下发（随 Publish:DeferredIntervalSeconds 周期生效）/ true=管理员为该站点开启实时下发（变更立即发布）----
    public bool RealtimePublish { get; set; }
    // ---- SSL 自动签发状态机（用户侧只读展示）----
    public string SslState { get; set; } = "";        // "" 无 / pending_dns 域名未解析 / pending_issuing 签发中 / issued 已签发 / failed 签发失败
    public string SslIssuedDomains { get; set; } = ""; // 已签发证书覆盖的域名（逗号分隔）
    public DateTime? SslExpiresAt { get; set; }        // 当前证书到期时间
    public DateTime? SslLastAttemptAt { get; set; }    // 最近一次签发尝试时间
    public string SslLastError { get; set; } = "";     // 最近一次签发失败原因
    public DateTime? SslRenewRequestedAt { get; set; } // 管理员主动发起续期的时间（防重复点击）
    public bool Enabled { get; set; } = true;

    public OriginConfig Origin { get; set; } = new();
    public CacheConfig Cache { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();

    /// <summary>高级配置（URL跳转/重写/报头/鉴权/压缩/限速）。持久化经 AdvancedJson（jsonb）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AdvancedJson { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public AdvancedConfig Advanced
    {
        get => string.IsNullOrEmpty(AdvancedJson) ? new AdvancedConfig() : System.Text.Json.JsonSerializer.Deserialize<AdvancedConfig>(AdvancedJson, (System.Text.Json.JsonSerializerOptions?)null) ?? new AdvancedConfig();
        set => AdvancedJson = System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null);
    }

    /// <summary>路由规则（location 级配置子树，通用设计：
    /// 命中 pathPrefix 的请求按本条覆盖缓存/安全/部分高级设置，未命中的字段回落站点级。持久化经 LocationsJson（jsonb）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LocationsJson { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<LocationRule> Locations
    {
        get => string.IsNullOrEmpty(LocationsJson) ? [] : System.Text.Json.JsonSerializer.Deserialize<List<LocationRule>>(LocationsJson, (System.Text.Json.JsonSerializerOptions?)null) ?? [];
        set => LocationsJson = System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>路由规则（location）：路径前缀 + 覆盖配置。先配置的规则优先；null 字段 = 继承站点级。</summary>
public class LocationRule
{
    public string PathPrefix { get; set; } = "";          // 如 /api/、/static/
    public bool Enabled { get; set; } = true;
    // 覆盖项（null=继承站点级配置）
    public CacheConfig? Cache { get; set; }               // 缓存覆盖
    public LocationSecurityOverride? Security { get; set; } // 安全覆盖（CC + IP 黑白名单 + 防盗链）
    public LocationAdvancedOverride? Advanced { get; set; } // 高级覆盖（报头/重写/压缩/字符集）
    public long RequestLimitPerMinute { get; set; } = -1; // -1=继承；>=0 覆盖
    public string? RemoteAddrHeader { get; set; }         // null=继承
}

/// <summary>路由规则高级覆盖：报头/重写规则替换、压缩与字符集覆盖。</summary>
public class LocationAdvancedOverride
{
    public List<HeaderRule> Headers { get; set; } = [];   // 请求/响应报头（替换站点级）
    public List<RewriteRule> Rewrites { get; set; } = []; // 重写规则（替换站点级）
    public bool? Compression { get; set; }                // null=继承；false=该路径禁压缩
    public string Charset { get; set; } = "";             // 空=继承
}

/// <summary>路由规则安全覆盖：CC 限流与 IP 名单（与站点级合并执行）。</summary>
public class LocationSecurityOverride
{
    public CcConfig? Cc { get; set; }
    public List<IpRule>? IpBlacklist { get; set; }
    public List<IpRule>? IpWhitelist { get; set; }
    public RefererConfig? Referers { get; set; }
}

/// <summary>源站配置：主源站按权重调度，备用源站故障时切换。</summary>
public class OriginConfig
{
    public List<Origin> Primaries { get; set; } = [];
    public List<Origin> Backups { get; set; } = [];
    public string ScheduleAlgorithm { get; set; } = "round-robin"; // random / round-robin / ip-hash
    public string? HostOverride { get; set; }       // 回源 Host，空=跟随访问域名
    public bool FollowOriginHost { get; set; }      // Host 跟随源站地址
    public List<PathOriginRule> PathRules { get; set; } = []; // 按路径分源站（前缀匹配，先到先得）
}

/// <summary>路径源站规则：命中 pathPrefix 的请求改用该源站组通用设计。</summary>
public class PathOriginRule
{
    public string PathPrefix { get; set; } = "";    // 如 /api、/static
    public List<Origin> Primaries { get; set; } = [];
    public List<Origin> Backups { get; set; } = [];
    public string? HostOverride { get; set; }
}

public class Origin
{
    public string Scheme { get; set; } = "http";    // http/https/s3（对象存储）
    public string Host { get; set; } = "";          // IP 或域名（s3 时为 endpoint）
    public int Port { get; set; } = 80;
    public int Weight { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    // ---- S3 对象存储源站（scheme=s3 时生效，AWS SigV4 签名，兼容 MinIO/OSS/COS/七牛 Kodo）----
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Bucket { get; set; }
    public string? Region { get; set; } = "us-east-1";
}

/// <summary>缓存配置。</summary>
public class CacheConfig
{
    public bool Enabled { get; set; }
    public List<CacheRule> Rules { get; set; } = [];
}

public class CacheRule
{
    public string MatchType { get; set; } = "extension"; // extension / path-prefix / path-exact / homepage / all
    public string? Value { get; set; }                   // "png,jpg" 或 "/static/"
    public int TtlSeconds { get; set; } = 3600;
    public int ClientTtlSeconds { get; set; }            // >0 时生成 Expires
    public bool IgnoreQueryString { get; set; }          // 缓存键忽略参数
}

/// <summary>安全配置。</summary>
public class SecurityConfig
{
    public List<IpRule> IpWhitelist { get; set; } = [];
    public List<IpRule> IpBlacklist { get; set; } = [];
    public CcConfig Cc { get; set; } = new();
    public List<string> DeniedCountries { get; set; } = [];  // ISO 代码
    public List<string> DeniedProvinces { get; set; } = [];  // 中国省份
    public List<string> DeniedCidrs { get; set; } = [];      // 区域封禁 CIDR（按国家聚合下发）
    public List<UaRule> UaRules { get; set; } = [];
    public List<WafRule> WafRules { get; set; } = [];        // 通用规则引擎（条件-操作符-动作）
    public UrlAuthConfig UrlAuth { get; set; } = new();      // URL 防盗链（A-D 签名方案）
    public JsShieldConfig JsShield { get; set; } = new();    // 5秒盾（JS 挑战）
    public RefererConfig Referers { get; set; } = new();     // Referer 防盗链
    public List<EdgeScript> EdgeScripts { get; set; } = [];  // 边缘脚本（expr-lang）
    public AiGuardConfig AiGuard { get; set; } = new();      // AI 语义防护（LLM 二次判定）
    public int BlockStatus { get; set; }                     // 拦截动作状态码（0=默认403；firewall-actions）
}

/// <summary>高级配置：URL跳转/重写/HTTP报头/访问鉴权/压缩/请求限制通用设计。</summary>
public class AdvancedConfig
{
    public List<UrlRedirectRule> Redirects { get; set; } = [];       // URL 跳转
    public List<RewriteRule> Rewrites { get; set; } = [];            // 重写规则
    public List<HeaderRule> Headers { get; set; } = [];              // 自定义 HTTP 报头
    public BasicAuthConfig BasicAuth { get; set; } = new();          // 访问鉴权（Basic Auth）
    public bool Compression { get; set; } = true;                    // 内容压缩（Gzip/Brotli）
    public bool WebpOptimization { get; set; }                       // WebP 自适应（Accept 协商改写扩展名）
    public string Charset { get; set; } = "";                        // 字符编码（空=跟随源站）
    public long RequestLimitPerMinute { get; set; }                  // 请求限制（次/分钟/IP，0=不限）
    public long MaxBodySizeMB { get; set; }                          // 请求体大小限制（MB，0=不限）
    public bool Websocket { get; set; } = true;                      // Websocket 透传
    public int WebsocketTimeoutSeconds { get; set; }                 // Websocket 空闲超时（秒，0=默认 300）
    public bool Grpc { get; set; }                                   // gRPC（HTTP/2 h2c 回源透传，节点支持后生效）
    public string RemoteAddrHeader { get; set; } = "";               // 访客IP取值头（如 CF-Connecting-IP / X-Forwarded-For；空=默认链路）
    public CustomPagesConfig? CustomPages { get; set; }              // 自定义页面（404/502/封禁 HTML）
    public MultimediaConfig? Multimedia { get; set; }                // 音视频设置
    public TrafficLimitConfig? TrafficLimit { get; set; }            // 流量限制（月流量上限，主控月结判定）
}

/// <summary>Referer 防盗链：白/黑名单 + 空Referer 策略。</summary>
public class RefererConfig
{
    public bool Enabled { get; set; }
    public bool AllowEmpty { get; set; } = true;      // 空 Referer 放行（直开/书签）
    public List<string> Whitelist { get; set; } = []; // 允许的域名后缀（如 example.com 匹配 *.example.com）
    public List<string> Blacklist { get; set; } = []; // 禁止的域名后缀（优先于白名单）
}

/// <summary>自定义页面：节点在 404/502/封禁时返回的 HTML（空=默认页）。</summary>
public class CustomPagesConfig
{
    public string NotFound { get; set; } = "";    // 404
    public string ServerError { get; set; } = ""; // 502/504
    public string Blocked { get; set; } = "";     // WAF/CC/限流封禁页
}

/// <summary>音视频设置：FLV 直播流透传（不缓存不缓冲）、MP4 拖拽（Range 已默认支持）。</summary>
public class MultimediaConfig
{
    public bool Mp4Drag { get; set; } = true;   // Range 请求（节点已支持）
    public bool FlvLive { get; set; }           // .flv 直播拉流透传（绕过缓存、禁缓冲）
}

/// <summary>流量限制：单站月流量上限；超限动作 block（503）或仅标记。</summary>
public class TrafficLimitConfig
{
    public long MonthlyGB { get; set; }             // 0=不限
    public string Action { get; set; } = "block";   // block=超限返回 503 / mark=仅标记日志
}

/// <summary>IP 库清单通用设计：发布期合并进全集群站点安全配置。</summary>
public class IpLibraryItem
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "deny";        // deny=黑名单 / allow=白名单
    public string Cidrs { get; set; } = "";           // 每行一个 IP/CIDR
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>公告通用设计。</summary>
public class Post
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Enabled { get; set; } = true;         // 上架中
    public bool Popup { get; set; }                   // 登录后弹窗展示
    public string Audience { get; set; } = "user";    // user 用户端 / admin 管理端 / all
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>URL 跳转规则（301/302）。</summary>
public class UrlRedirectRule
{
    public string Pattern { get; set; } = "";     // 前缀或正则
    public string Target { get; set; } = "";      // 目标 URL（支持 $1 捕获）
    public int Code { get; set; } = 301;          // 301/302
    public bool Enabled { get; set; } = true;
}

/// <summary>重写规则（路径改写，转发前生效）。</summary>
public class RewriteRule
{
    public string Pattern { get; set; } = "";     // 正则
    public string Replacement { get; set; } = ""; // 替换（$1 捕获）
    public bool Enabled { get; set; } = true;
}

/// <summary>HTTP 报头规则（请求/响应方向增删）。</summary>
public class HeaderRule
{
    public string Direction { get; set; } = "response"; // request / response
    public string Action { get; set; } = "set";         // set / add / del
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>访问鉴权（Basic Auth 用户列表）。</summary>
public class BasicAuthConfig
{
    public bool Enabled { get; set; }
    public List<BasicAuthUser> Users { get; set; } = [];
}

public class BasicAuthUser
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = ""; // SHA256 hex（不存明文）
    public string Plain { get; set; } = "";        // 仅输入用，存库时转为 Hash
}

/// <summary>AI 防护：可疑请求采样送 LLM 语义判定（OpenAI 兼容接口），不阻塞主请求路径。</summary>
public class AiGuardConfig
{
    public bool Enabled { get; set; }
    public string ApiUrl { get; set; } = "";     // OpenAI 兼容 /chat/completions
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public int SampleRate { get; set; } = 10;    // 采样率 %（对被本地规则标记 log/低置信度的请求）
    public double BlockThreshold { get; set; } = 0.8; // 恶意概率超过则加入临时拦截表
}

/// <summary>边缘脚本：expr-lang 表达式（请求改写/短路响应）。</summary>
public class EdgeScript
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Expr { get; set; } = "";
}

/// <summary>5秒盾：首次访问返回 JS 挑战页，通过后种 cookie 放行；连续失败 N 次封禁。</summary>
public class JsShieldConfig
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "js";       // js（JS 跳转挑战）/ meta（meta 刷新）
    public string Secret { get; set; } = "";       // 挑战签名密钥（空则用节点内置盐）
    public int CookieSeconds { get; set; } = 3600; // 通过后 cookie 有效期
    public int MaxFails { get; set; } = 5;         // 连续失败封禁阈值
    public int BanSeconds { get; set; } = 600;     // 封禁时长
    public string[] ExcludePaths { get; set; } = [];  // 例外路径（如回调接口）
}

/// <summary>URL 鉴权（防盗链）：A-D 四种时间戳签名方案通用设计。</summary>
public class UrlAuthConfig
{
    public bool Enabled { get; set; }
    public string Type { get; set; } = "a";        // a / b / c / d
    public string Key { get; set; } = "";          // 签名密钥
    public int ExpirySeconds { get; set; } = 1800; // 时间戳有效期
}

/// <summary>WAF 规则：条件集（全部满足）→ 动作。通用设计
public class WafRule
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<WafCondition> Conditions { get; set; } = [];
    public string Action { get; set; } = "block"; // block / allow / captcha / js_cookie / redirect / log
    public string ActionValue { get; set; } = ""; // redirect 目标 URL 等
    public int Priority { get; set; } = 100;      // 小者优先
}

/// <summary>WAF 条件：字段 + 操作符 + 值（可取反）。</summary>
public class WafCondition
{
    public string Field { get; set; } = "uri";    // uri / ip / user_agent / referer / header_X / method / query_X / body_length / host
    public string Operator { get; set; } = "contains"; // 见 WafOperators
    public string Value { get; set; } = "";
    public bool Negate { get; set; }
}

/// <summary>支持的操作符集通用设计。</summary>
public static class WafOperators
{
    public static readonly string[] All =
    [
        "eq", "ne", "contains", "not-contains",          // 精确/包含
        "prefix", "suffix", "regex",                     // 前缀/后缀/正则
        "in", "not-in",                                  // 值列表（分号分隔）
        "gt", "lt", "gte", "lte",                        // 数值比较
        "exists", "not-exists",                          // 字段存在性
        "in-cidr", "not-in-cidr",                        // IP 段（ip 字段专用）
        "empty", "not-empty",                            // 空值
    ];
}

public class IpRule
{
    public string Cidr { get; set; } = "";      // 单IP 或 CIDR
    public string? Note { get; set; }
}

public class CcConfig
{
    public bool Enabled { get; set; }
    public int Threshold { get; set; } = 120;   // 单 IP 每分钟请求数
    public int WindowSeconds { get; set; } = 60;
    public int BanSeconds { get; set; } = 300;  // 超限封禁时长
    public string[] ExcludePaths { get; set; } = [];  // 例外 URL
    public List<string> ExcludePathsList { get; set; } = []; // 路由规则场景用 list 形态
}

public class UaRule
{
    public string Keyword { get; set; } = "";   // 支持 * 通配
    public bool Allow { get; set; }             // true=允许，false=拒绝
}

/// <summary>访问日志记录。</summary>
public class AccessLogRecord
{
    public long Id { get; set; }
    public long ServerId { get; set; }
    public long NodeId { get; set; }
    public string RemoteAddr { get; set; } = "";
    public string Method { get; set; } = "";
    public string Host { get; set; } = "";
    public string Uri { get; set; } = "";
    public int Status { get; set; }
    public long BytesSent { get; set; }
    public string Referer { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public double RequestTimeMs { get; set; }
    public string CacheStatus { get; set; } = "";
    public string? UpstreamAddr { get; set; }
    public string RequestId { get; set; } = "";
    public string WafAction { get; set; } = "";
    public string? BlockReason { get; set; }
    public DateTime Timestamp { get; set; }
}

// ---- 多租户计费（P1，通用设计----

/// <summary>套餐：限制网站数/域名数/月流量，超额停服或降速。</summary>
public class Plan
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int MaxServers { get; set; } = 10;          // 网站数上限
    public int MaxDomains { get; set; } = 20;          // 域名数上限（含通配符）
    public long MonthlyTrafficMB { get; set; } = 51200; // 月流量上限（MB），0=不限
    public decimal PriceCents { get; set; }            // 月价（分）
    public bool Enabled { get; set; } = true;
    public string? Features { get; set; }              // 功能开关/配额 JSON（空=全开，向后兼容），见 PlanFeatures
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>用户订阅：用户 → 套餐 + 周期。</summary>
public class Subscription
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long PlanId { get; set; }
    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
    public bool Enabled { get; set; } = true;
    public bool AutoRenew { get; set; }                // 到期自动续费（余额扣款；false=到期自然失效）
}

/// <summary>月度用量（按用户+月份聚合，写入时 MERGE）。</summary>
public class UsageRecord
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public long Requests { get; set; }
    public long TrafficMB { get; set; }
    public long PeakBandwidthKbps { get; set; } // 95 峰值简化为峰值带宽
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ---- 证书管理（手动上传 + 到期提醒）----

/// <summary>手动上传证书：PEM 对存储，随配置下发到节点 SNI。</summary>
public class Certificate
{
    public long Id { get; set; }
    public long ClusterId { get; set; }
    public string Domains { get; set; } = "";       // 逗号分隔（主域名+SAN）
    public string CertPem { get; set; } = "";       // 证书链 PEM
    public string KeyPem { get; set; } = "";        // 私钥 PEM
    public DateTime? ExpiresAt { get; set; }        // 从证书解析
    public string Source { get; set; } = "manual";  // manual / acme
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>站内通知（管理端/用户端通知中心；SignalR 实时推送 + 列表留存）。</summary>
public class Notification
{
    public long Id { get; set; }
    public long? UserId { get; set; }                // null = 广播（全员可见）
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int Type { get; set; }                    // 0=系统通知 1=告警 2=工单 3=财务
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>站点配置模板：一键应用预设的 HTTPS/缓存/安全配置到指定网站。</summary>
public class SiteConfigTemplate
{
    public long Id { get; set; }
    public string Name { get; set; } = "";           // 模板名（如 电商网站防护 / 博客加速 / 高安全模式）
    public string Description { get; set; } = "";    // 模板说明
    public bool BuiltIn { get; set; }                // 内置模板不可删除
    public string ConfigJson { get; set; } = "";     // {sslMode, forceHttps, cache:{...}, security:{...}} 片段
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>集群运维配置：资源阈值告警 + 健康检查参数（心跳越限写 Notification 告警，30 分钟节流）。</summary>
public class OpsConfig
{
    // ---- 阈值告警（百分比/绝对值，0=不启用）----
    public int CpuPercent { get; set; }          // CPU 使用率阈值
    public int MemPercent { get; set; }          // 内存使用率阈值
    public double Load { get; set; }             // 1min 负载阈值
    // ---- 健康检查（区域探测参数，随配置下发节点 probe 循环）----
    public int ProbeIntervalSeconds { get; set; } = 15;  // 探测间隔
    public int ProbeTimeoutMs { get; set; } = 5000;      // 单次超时
    public int ProbeFailThreshold { get; set; } = 3;     // 连续失败判异常
    // ---- 集群运维开关通用设计----
    public bool AlertsEnabled { get; set; } = true;      // 阈值告警总开关（message）
    public bool MetricsEnabled { get; set; } = true;     // 实时指标采集开关（metrics）
    public int UpgradeWindowStart { get; set; } = -1;    // 升级维护窗口起始小时（0-23，-1=不限；schedule）
    public int UpgradeWindowEnd { get; set; } = -1;      // 升级维护窗口结束小时（0-23；跨零点如 22~6）
    // ---- 运营展示开关（对用户端暴露的信息面）----
    public bool ShowNodesOnline { get; set; } = true;    // 用户端站点列表是否展示节点在线数（false 时 /user/servers 不返回该字段）
    // ---- 日志保留策略（小时级 Billing 循环执行清理；0=永久保留）----
    public int LogKeepDays { get; set; } = 30;           // 访问日志保留天数
    public int AuditKeepDays { get; set; } = 90;         // 审计日志保留天数
    public int SmsKeepDays { get; set; } = 90;           // 短信日志保留天数
}

/// <summary>DNS 三方接入配置（运营配置，存数据库，管理员可编辑；替代 appsettings.json Dns 节）。
/// 单行表（Id=1）。各厂商凭证存 ProvidersJson（{"aggregated":{"uid":"...","key":"..."},...}），
/// 读取经 DnsSettingsService（内存缓存，更新即失效）。</summary>
public class DnsSettings
{
    public long Id { get; set; } = 1;                // 固定单行
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "aggregated"; // aggregated / aliyun / dnspod / huawei / cloudflare
    public string DefaultDomainId { get; set; } = "";    // DNS 主域名 ID（如聚合DNS提供商的主域名 ID）
    public string ProvidersJson { get; set; } = "{}";    // 按厂商的凭证字典
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>短信网关与提醒全局配置（单行，管理员经 /admin/sms/settings 维护；网关为 Tnorv SMS）。
/// 用户需先到 sms.tnorv.com 完成实名认证→添加资质→报备签名→报备模板，才可正式使用。</summary>
public class SmsSettings
{
    public long Id { get; set; } = 1;                      // 固定单行
    public bool Enabled { get; set; }                      // 总开关（关闭=全站不发短信，验证码也不发）
    public string ApiBase { get; set; } = ""; // 短信网关地址（自行填写）
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";            // 明文仅创建/重置可见；此处存库供签名
    public string Signature { get; set; } = "";   // 短信签名（不带【】；正式使用需报备）
    public bool VerifySign { get; set; } = true;           // 密钥侧「校验签名」开关（决定是否带 timestamp/signature）
    public string EnabledEvents { get; set; } = "[]";      // 全局启用的提醒项 JSON 数组（见 SmsEvents.All）
    public int DailyLimitPerMobile { get; set; } = 10;     // 单手机日发送上限（风控：短信监管严格）
    public int CodeExpireSeconds { get; set; } = 300;      // 验证码有效期（秒）
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>短信发送记录（管理端可查；status=queued/blocked/failed/sent）。</summary>
public class SmsLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }                // 关联用户（验证码/系统提醒可为空）
    public string Mobile { get; set; } = "";
    public string Event { get; set; } = "";          // 事件键（verify/sub_expiring/.../test）
    public string Content { get; set; } = "";        // 实际下发内容（含签名前缀）
    public string BatchId { get; set; } = "";        // 网关批次号
    public string Status { get; set; } = "";         // queued / blocked / failed / sent
    public string Error { get; set; } = "";          // 失败/拦截原因
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>短信提醒事件目录：全局启用项与用户订阅项的并集生效；事件内容禁止携带域名（短信风控）。</summary>
public static class SmsEvents
{
    public static readonly (string Key, string Label)[] All =
    [
        ("sub_expiring", "订阅到期提醒"),
        ("balance_low", "余额不足提醒"),
        ("traffic_over", "流量超限提醒"),
        ("cert_failed", "证书签发失败"),
        ("cert_expiring", "证书到期提醒"),
        ("icp_result", "备案审核结果"),
        ("ticket_reply", "工单回复提醒"),
        ("node_offline", "节点离线告警"),
    ];
    public static readonly HashSet<string> Keys = All.Select(x => x.Key).ToHashSet();
}

/// <summary>ACME 证书签发账户（运营配置，管理员可编辑，存数据库）。
/// 邮箱是 ACME 账户唯一标识：单邮箱注册账户过多时 Let's Encrypt 可能限流签发失败，
/// 因此支持按用户/站点拼接派生邮箱（如 u{userId}.tnorvcdnssl@deathin.icu）分散账户。</summary>
public class AcmeAccount
{
    public long Id { get; set; }
    public string Name { get; set; } = "";            // 账户备注名
    public string EmailTemplate { get; set; } = ""; // 邮箱模板，支持 {userId} 占位符；空 = 用 DefaultEmail
    public string DefaultEmail { get; set; } = "";  // 无占位符时统一使用的邮箱
    public bool Enabled { get; set; } = true;
    public string Note { get; set; } = "";
    // ---- CA 提供商（备用证书源）：主 CA 被限流时节点自动切换备用 ----
    public string Provider { get; set; } = "letsencrypt";        // letsencrypt / google / zerossl / custom
    public string DirectoryUrl { get; set; } = "";               // custom 时必填（ACME 目录 URL）
    public string BackupDirectoryUrl { get; set; } = "";         // 备用 CA 目录（如 Google Trust Services），限流/失败时自动切换
    public string EabKid { get; set; } = "";                     // 备用 CA External Account Binding（Google/ZeroSSL 需要）
    public string EabHmacKey { get; set; } = "";                 // 备用 CA EAB HMAC key
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>节点上传的 ACME 签发证书（受控签发：执行节点签好后上报主控，主控下发全部节点 SNI）。</summary>
public class NodeCertUpload
{
    public string Domain { get; set; } = "";
    public string CertPem { get; set; } = "";   // 证书链 PEM
    public string KeyPem { get; set; } = "";    // 私钥 PEM（节点生成 CSR 的私钥，仅节点↔主控通道传输）
}

/// <summary>证书签发事件日志（管理员可查看的签发/续期/失败时间线；按站点+域名）。</summary>
public class CertEvent
{
    public long Id { get; set; }
    public long ServerId { get; set; }
    public long NodeId { get; set; }            // 上报节点
    public string Domain { get; set; } = "";
    public string Status { get; set; } = "";    // pending_dns / issuing / issued / failed / renew_requested / timeout
    public string Error { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
