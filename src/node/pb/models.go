// Package pb 由 proto/tnorv.proto 生成；此处提供 JSON 兼容的轻量模型，
// 使节点在无 protoc 环境时也能以 HTTP/JSON 模式与主控通信（M1 阶段采用 JSON API，P1 切 gRPC）。
package pb

// NodeConfig 主控下发的完整节点配置快照
type AcmeConfig struct {
	Email        string `json:"email,omitempty"`        // ACME 账户邮箱（运营配置派生，支持按用户分散账户）
	Provider     string `json:"provider,omitempty"`     // letsencrypt / google / zerossl / custom
	DirectoryUrl string `json:"directoryUrl,omitempty"` // custom ACME 目录 URL
	// 备用 CA：主 CA 限流/签发失败时自动切换（如 Google Trust Services）
	BackupDirectoryUrl string `json:"backupDirectoryUrl,omitempty"`
	EabKid             string `json:"eabKid,omitempty"`     // 备用 CA EAB（Google/ZeroSSL 需要）
	EabHmacKey         string `json:"eabHmacKey,omitempty"` // 备用 CA EAB HMAC
}

type NodeConfig struct {
	Ops           *OpsConfig `json:"ops,omitempty"`
	GeneratedAt  string           `json:"generatedAt"` // RFC3339 字符串
	Version      int64            `json:"version,omitempty"`
	Servers      []*Server        `json:"servers"`
	Acme         *AcmeConfig      `json:"acme,omitempty"`   // ACME 签发账户配置（主控运营配置下发）
	Purges       []*PurgeCommand  `json:"purges,omitempty"` // 缓存刷新指令（消费一次）
	ManualCerts  []*ManualCert    `json:"manualCerts,omitempty"`
	L2Nodes      []*L2Node        `json:"l2Nodes,omitempty"`      // 二级汇聚节点（就近回源）
	SiblingNodes []*L2Node        `json:"siblingNodes,omitempty"` // 同组边缘节点（缓存共享 P2P 拉取，含自身）
	Upgrade      *UpgradeCommand  `json:"upgrade,omitempty"`      // 自升级指令（版本+下载地址+sha256）
	Routing      *RoutingPlan     `json:"routing,omitempty"`      // 动态就近回源路径（拨测驱动）
	Ddos         *DdosConfig      `json:"ddos,omitempty"`         // 集群级 DDoS 防护（nftables）
	Renews       []*RenewCommand  `json:"renew,omitempty"`        // 主动续期指令（管理员触发）
	Challenges   []*AcmeChallenge `json:"challenges,omitempty"`   // HTTP-01 挑战应答（受控签发，全节点监听）
	DnsRecords   []*DnsRecord     `json:"dnsRecords,omitempty"`   // 智能 DNS 记录（节点内建权威 DNS 应答用）
}

// DnsRecord 智能 DNS 解析记录（主控 NsDomains 快照下发，节点权威 DNS 按线路应答）
type DnsRecord struct {
	Id    int64  `json:"id"`
	Name  string `json:"name"`  // 完整域名，如 demo.example.com
	Type  string `json:"type"`  // A / AAAA / CNAME / TXT
	Value string `json:"value"` // 记录值
	Ttl   int    `json:"ttl"`
	Line  string `json:"line"`  // 线路：default / telecom / unicom / cmcc / overseas
}

// DdosConfig DDoS 防护：nftables 内核层每 IP 连接数/新建速率限制
type DdosConfig struct {
	Enabled       bool  `json:"enabled"`
	ConnLimit     int   `json:"connLimit"`     // 单 IP 最大并发连接数（0=不限）
	ConnRate      int   `json:"connRate"`      // 单 IP 每秒新建连接数
	BurstRate     int   `json:"burstRate"`     // 速率突发桶
	SynFloodLimit int   `json:"synFloodLimit"` // SYN 每秒上限（防护 SYN Flood）
	Ports         []int `json:"ports"`         // 应用端口（仅对这些端口生效）
	// 网络安全（network-security）：管理网段白名单（永远放行，置于限速规则前）
	AdminAllowCidrs []string `json:"adminAllowCidrs,omitempty"`
}

// L2Node 二级汇聚节点信息（边缘未命中先查 L2 缓存，省源站流量）
type L2Node struct {
	Id   int64    `json:"id"`
	Name string   `json:"name"`
	Ips  []string `json:"ips"`
}

// ManualCert 手动上传证书（PEM 对，SNI 路由优先于 ACME）
// AcmeChallenge 受控签发的 HTTP-01 挑战（主控下发全部节点监听应答，10 分钟 TTL）
type AcmeChallenge struct {
	Path    string `json:"path"`    // /.well-known/acme-challenge/<token>
	KeyAuth string `json:"keyAuth"` // 应答内容
}

// RenewCommand 主动续期指令（管理员触发，节点消费后主控清除标记）
type RenewCommand struct {
	ServerId int64 `json:"serverId"`
}

type ManualCert struct {
	Domains   string `json:"domains"` // 逗号分隔
	CertPem   string `json:"certPem"`
	KeyPem    string `json:"keyPem"`
	ExpiresAt string `json:"expiresAt,omitempty"`
}

// PurgeCommand 缓存刷新指令
type PurgeCommand struct {
	Id        int64  `json:"id"`
	MatchType string `json:"matchType"` // url / directory / host / all
	Host      string `json:"host"`
	Value     string `json:"value"`
}

type Server struct {
	Id             int64           `json:"id"`
	ServerType     string          `json:"serverType,omitempty"` // http / tcp / tls / udp
	Domains        []string        `json:"domains"`
	HttpPorts      []int           `json:"httpPorts"`
	HttpsPorts     []int           `json:"httpsPorts"`
	L4Ports        []int           `json:"l4Ports,omitempty"`
	ForceHttps     bool            `json:"forceHttps"`
	SslMode        string          `json:"sslMode,omitempty"` // auto=ACME自动签发 / manual=手动上传 / off=不启用HTTPS
	Origin         OriginConfig    `json:"origin"`
	Cache          CacheConfig     `json:"cache"`
	Security       SecurityConfig  `json:"security"`
	Advanced       *AdvancedConfig `json:"advanced,omitempty"`
	Locations      []*LocationRule `json:"locations,omitempty"` // 路由规则（location 级覆盖）
	RenewRequested bool            `json:"renewRequested,omitempty"` // 管理员主动发起证书续期（节点消费一次后由主控清除）
	Blocked        bool            `json:"blocked,omitempty"` // 流量超限标记（主控月结判定，503）
	L4ProxyProtocol bool           `json:"l4ProxyProtocol,omitempty"` // L4 转发向源站发送 PROXY protocol v2（TOA 的用户态等价：真实客户端 IP 传递）
}

// LocationRule 路由规则：命中 pathPrefix 的请求按覆盖配置执行（先配置优先；nil=继承站点级）
type LocationRule struct {
	PathPrefix            string                    `json:"pathPrefix"`
	Enabled               bool                      `json:"enabled"`
	Cache                 *CacheConfig              `json:"cache,omitempty"`
	Security              *LocationSecurityOverride `json:"security,omitempty"`
	Advanced              *LocationAdvancedOverride `json:"advanced,omitempty"`
	RequestLimitPerMinute int64                     `json:"requestLimitPerMinute"` // -1=继承
	RemoteAddrHeader      *string                   `json:"remoteAddrHeader,omitempty"`
}

// LocationAdvancedOverride 路由规则高级覆盖：报头/重写替换、压缩与字符集覆盖
type LocationAdvancedOverride struct {
	Headers     []*HeaderRule `json:"headers,omitempty"`
	Rewrites    []*RewriteRule `json:"rewrites,omitempty"`
	Compression *bool         `json:"compression,omitempty"`
	Charset     string        `json:"charset,omitempty"`
}

// LocationSecurityOverride 路由规则安全覆盖
type LocationSecurityOverride struct {
	Cc          *CcConfig       `json:"cc,omitempty"`
	IpBlacklist []*IpRule       `json:"ipBlacklist,omitempty"`
	IpWhitelist []*IpRule       `json:"ipWhitelist,omitempty"`
	Referers    *RefererConfig  `json:"referers,omitempty"`
}

// AdvancedConfig 高级配置：URL跳转/重写/HTTP报头/访问鉴权/压缩/WebP/请求限制
type AdvancedConfig struct {
	Redirects             []*UrlRedirectRule `json:"redirects,omitempty"`
	Rewrites              []*RewriteRule     `json:"rewrites,omitempty"`
	Headers               []*HeaderRule      `json:"headers,omitempty"`
	BasicAuth             *BasicAuthConfig   `json:"basicAuth,omitempty"`
	Compression           bool               `json:"compression"`
	WebpOptimization      bool               `json:"webpOptimization"`
	Charset               string             `json:"charset,omitempty"`
	RequestLimitPerMinute int64              `json:"requestLimitPerMinute"`
	MaxBodySizeMB         int64              `json:"maxBodySizeMB"`
	Websocket             bool               `json:"websocket"`
	WebsocketTimeoutSeconds int              `json:"websocketTimeoutSeconds,omitempty"`
	Grpc                  bool               `json:"grpc"`
	RemoteAddrHeader      string             `json:"remoteAddrHeader,omitempty"`
	CustomPages           *CustomPagesConfig `json:"customPages,omitempty"`
	Multimedia            *MultimediaConfig  `json:"multimedia,omitempty"`
	TrafficLimit          *TrafficLimitConfig `json:"trafficLimit,omitempty"`
}

// CustomPagesConfig 自定义页面：404/502/封禁 HTML（空=默认页）
type CustomPagesConfig struct {
	NotFound    string `json:"notFound,omitempty"`
	ServerError string `json:"serverError,omitempty"`
	Blocked     string `json:"blocked,omitempty"`
}

// MultimediaConfig 音视频：flv 直播透传（绕缓存禁缓冲）；mp4 Range 已默认支持
type MultimediaConfig struct {
	Mp4Drag bool `json:"mp4Drag"`
	FlvLive bool `json:"flvLive"`
}

// TrafficLimitConfig 流量限制（主控月结判定后下发 blocked 标记；本配置仅透传展示口径）
type TrafficLimitConfig struct {
	MonthlyGB int64  `json:"monthlyGB"`
	Action    string `json:"action"`
}

type UrlRedirectRule struct {
	Pattern string `json:"pattern"`
	Target  string `json:"target"`
	Code    int    `json:"code"`
	Enabled bool   `json:"enabled"`
}

type RewriteRule struct {
	Pattern     string `json:"pattern"`
	Replacement string `json:"replacement"`
	Enabled     bool   `json:"enabled"`
}

type HeaderRule struct {
	Direction string `json:"direction"` // request / response
	Action    string `json:"action"`    // set / add / del
	Name      string `json:"name"`
	Value     string `json:"value"`
	Enabled   bool   `json:"enabled"`
}

type BasicAuthConfig struct {
	Enabled bool            `json:"enabled"`
	Users   []BasicAuthUser `json:"users"`
}

type BasicAuthUser struct {
	Username     string `json:"username"`
	PasswordHash string `json:"passwordHash"` // SHA256 hex
}

type OriginConfig struct {
	Primaries         []*Origin         `json:"primaries"`
	Backups           []*Origin         `json:"backups"`
	ScheduleAlgorithm string            `json:"scheduleAlgorithm"` // random / round-robin / ip-hash
	HostOverride      string            `json:"hostOverride,omitempty"`
	FollowOriginHost  bool              `json:"followOriginHost"`
	PathRules         []*PathOriginRule `json:"pathRules"` // 按路径分源站（前缀匹配，先到先得）
}

// PathOriginRule 路径源站规则：命中 pathPrefix 的请求改用该源站组
type PathOriginRule struct {
	PathPrefix   string    `json:"pathPrefix"`
	Primaries    []*Origin `json:"primaries"`
	Backups      []*Origin `json:"backups"`
	HostOverride string    `json:"hostOverride,omitempty"`
}

type Origin struct {
	Scheme  string `json:"scheme"` // http / https / s3（对象存储）
	Host    string `json:"host"`
	Port    int    `json:"port"`
	Weight  int    `json:"weight"`
	Enabled bool   `json:"enabled"`
	// S3 对象存储源站（scheme=s3，SigV4 签名，兼容 MinIO/OSS/COS）
	AccessKey string `json:"accessKey,omitempty"`
	SecretKey string `json:"secretKey,omitempty"`
	Bucket    string `json:"bucket,omitempty"`
	Region    string `json:"region,omitempty"`
}

type CacheConfig struct {
	Enabled bool         `json:"enabled"`
	Rules   []*CacheRule `json:"rules"`
}

type CacheRule struct {
	MatchType         string `json:"matchType"` // extension / path-prefix / path-exact / homepage / all
	Value             string `json:"value,omitempty"`
	TtlSeconds        int    `json:"ttlSeconds"`
	ClientTtlSeconds  int    `json:"clientTtlSeconds"`
	IgnoreQueryString bool   `json:"ignoreQueryString"`
}

type SecurityConfig struct {
	IpWhitelist     []*IpRule      `json:"ipWhitelist"` // 单IP或CIDR
	IpBlacklist     []*IpRule      `json:"ipBlacklist"`
	Cc              CcConfig       `json:"cc"`
	DeniedCountries []string       `json:"deniedCountries,omitempty"` // ISO 代码（展示用）
	DeniedCidrs     []string       `json:"deniedCidrs,omitempty"`     // 区域封禁 CIDR 列表（主控按国家聚合下发）
	DeniedProvinces []string       `json:"deniedProvinces"`
	UaRules         []*UaRule      `json:"uaRules"`
	WafRules        []*WafRule     `json:"wafRules"`              // 通用规则引擎
	UrlAuth         UrlAuthConfig  `json:"urlAuth"`               // URL 防盗链（A-D 签名方案）
	JsShield        JsShieldConfig `json:"jsShield"`              // 5秒盾（JS 挑战）
	Referers        *RefererConfig `json:"referers,omitempty"`    // Referer 防盗链
	EdgeScripts     []*EdgeScript  `json:"edgeScripts,omitempty"` // 边缘脚本（expr-lang）
	AiGuard         *AiGuardConfig `json:"aiGuard,omitempty"`     // AI 语义防护
	BlockStatus     int            `json:"blockStatus,omitempty"` // 拦截动作状态码（默认 403；firewall-actions 覆盖）
}

// AiGuardConfig AI 防护：可疑请求采样送 LLM 语义判定
type AiGuardConfig struct {
	Enabled        bool    `json:"enabled"`
	ApiUrl         string  `json:"apiUrl"`
	ApiKey         string  `json:"apiKey"`
	Model          string  `json:"model"`
	SampleRate     int     `json:"sampleRate"`     // 采样率 %
	BlockThreshold float64 `json:"blockThreshold"` // 恶意分阈值
}

// EdgeScript 边缘脚本：expr-lang 表达式，请求改写/短路响应
type EdgeScript struct {
	Name    string `json:"name"`
	Enabled bool   `json:"enabled"`
	Expr    string `json:"expr"` // expr-lang 表达式
}

// SiblingIPs 展平兄弟节点 IP 列表（拨测/共享用）
func (c *NodeConfig) SiblingIPs() []string {
	var out []string
	for _, n := range c.SiblingNodes {
		out = append(out, n.Ips...)
	}
	return out
}

// RoutingPlan 动态回源路径：mode=direct 直连源站；mode=via 经 Via 中转（拨测驱动计算）
type RoutingPlan struct {
	Mode     string  `json:"mode"`             // direct / via
	Via      string  `json:"via,omitempty"`    // 中转节点 addr（host:3128）
	DirectMs float64 `json:"directMs"`         // 本节点→源站直连 RTT
	ViaMs    float64 `json:"viaMs,omitempty"`  // 两跳总 RTT
	Origin   string  `json:"origin,omitempty"` // 计算所用的主源站地址
}

// UpgradeCommand 节点自升级指令
type UpgradeCommand struct {
	Version string `json:"version"`
	URL     string `json:"url"`
	SHA256  string `json:"sha256,omitempty"`
}

// JsShieldConfig 5秒盾：首次访问返回 JS 挑战页，通过后种 cookie 放行；连续失败 N 次封禁
type JsShieldConfig struct {
	Enabled       bool     `json:"enabled"`
	Mode          string   `json:"mode"`          // js / meta
	Secret        string   `json:"secret"`        // 挑战签名密钥（空则用内置盐）
	CookieSeconds int      `json:"cookieSeconds"` // 通过后 cookie 有效期
	MaxFails      int      `json:"maxFails"`      // 连续失败封禁阈值
	BanSeconds    int      `json:"banSeconds"`    // 封禁时长
	ExcludePaths  []string `json:"excludePaths"`
}

// UrlAuthConfig URL 鉴权（防盗链）：时间戳签名方案 a/b/c/d
type UrlAuthConfig struct {
	Enabled       bool   `json:"enabled"`
	Type          string `json:"type"`          // a / b / c / d
	Key           string `json:"key"`           // 签名密钥
	ExpirySeconds int    `json:"expirySeconds"` // 时间戳有效期
}

// WafRule 条件集（全部满足）→ 动作
type WafRule struct {
	Name        string          `json:"name"`
	Enabled     bool            `json:"enabled"`
	Conditions  []*WafCondition `json:"conditions"`
	Action      string          `json:"action"`      // block / allow / redirect / log / captcha
	ActionValue string          `json:"actionValue"` // redirect 目标 URL
	Priority    int             `json:"priority"`
}

// WafCondition 字段 + 操作符 + 值（可取反）
type WafCondition struct {
	Field    string `json:"field"` // uri / ip / user_agent / referer / header_X / method / query_X / body_length / host
	Operator string `json:"operator"`
	Value    string `json:"value"`
	Negate   bool   `json:"negate"`
}

type IpRule struct {
	Cidr string `json:"cidr"` // 单IP或CIDR
	Note string `json:"note,omitempty"`
}

type CcConfig struct {
	Enabled       bool     `json:"enabled"`
	Threshold     int      `json:"threshold"` // 单IP每窗口请求数
	WindowSeconds int      `json:"windowSeconds"`
	BanSeconds    int      `json:"banSeconds"`
	ExcludePaths  []string `json:"excludePaths"`
}

type UaRule struct {
	Keyword string `json:"keyword"` // 支持 * 通配
	Allow   bool   `json:"allow"`
}

// ---- 节点 API 请求/响应（M1 阶段 HTTP JSON；与 proto 保持同构） ----

type RegisterRequest struct {
	InstallToken string   `json:"installToken"`
	Hostname     string   `json:"hostname"`
	Version      string   `json:"version"`
	PublicIps    []string `json:"publicIps"`
	Os           string   `json:"os"`
	Arch         string   `json:"arch"`
}

type RegisterResponse struct {
	NodeId      int64  `json:"nodeId"`
	NodeToken   string `json:"nodeToken"`
	ClusterName string `json:"clusterName"`
}

type GetConfigRequest struct {
	NodeToken     string `json:"nodeToken"`
	ConfigVersion int64  `json:"configVersion"`
}

type GetConfigResponse struct {
	ConfigVersion int64       `json:"configVersion"`
	ConfigJson    string      `json:"configJson"`
	Changed       bool        `json:"changed"`
	PublicIp      string      `json:"publicIp,omitempty"` // 主控看到的节点连接来源 IP（NAT 节点公网 IP 校正）
	Parsed        *NodeConfig `json:"-"`
}

type HeartbeatRequest struct {
	NodeToken     string     `json:"nodeToken"`
	ConfigVersion int64      `json:"configVersion"`
	Stats         *NodeStats `json:"stats"`
}

type NodeStats struct {
	CpuUsage      float64        `json:"cpuUsage"`
	MemUsage      float64        `json:"memUsage"`
	BytesIn       uint64         `json:"bytesIn"`
	BytesOut      uint64         `json:"bytesOut"`
	Connections   uint32         `json:"connections"`
	Qps           float64        `json:"qps"`
	Load          float64        `json:"load"`
	UptimeSeconds uint32         `json:"uptimeSeconds"`
	Probes        []*ProbeResult `json:"probes,omitempty"`       // 区域探测：本节点对集群站点的可访问性
	OriginProbes  []*OriginProbe `json:"originProbes,omitempty"` // 源站拨测：本节点到各源站的 TCP RTT
	PeerProbes    []*PeerProbe   `json:"peerProbes,omitempty"`   // 节点拨测：本节点到兄弟节点 :3128 的 RTT
	CertStates    []*CertState   `json:"certStates,omitempty"`   // ACME 证书签发状态上报（域名→状态）
}

// CertState ACME 证书签发状态（节点→主控上报）
type CertState struct {
	Domain    string `json:"domain"`
	Status    string `json:"status"` // pending_dns / issuing / issued / failed
	Error     string `json:"error,omitempty"`
	ExpiresAt string `json:"expiresAt,omitempty"` // RFC3339，issued 时有值
}

// OriginProbe 源站拨测结果（就近回源决策数据）
type OriginProbe struct {
	Origin    string  `json:"origin"`
	Reachable bool    `json:"reachable"`
	LatencyMs float64 `json:"latencyMs"`
}

// PeerProbe 节点拨测结果
type PeerProbe struct {
	Peer      string  `json:"peer"`
	Reachable bool    `json:"reachable"`
	LatencyMs float64 `json:"latencyMs"`
}

// ProbeResult 区域探测结果：节点对某站点 URL 的探测
type ProbeResult struct {
	ServerId  int64   `json:"serverId"`
	Url       string  `json:"url"`
	Ok        bool    `json:"ok"`
	Status    int     `json:"status"`
	LatencyMs float64 `json:"latencyMs"`
	Error     string  `json:"error,omitempty"`
}

type HeartbeatResponse struct {
	Ok            bool  `json:"ok"`
	LatestVersion int64 `json:"latestVersion"`
}

type AccessLog struct {
	ServerId      int64   `json:"serverId"`
	RemoteAddr    string  `json:"remoteAddr"`
	Method        string  `json:"method"`
	Scheme        string  `json:"scheme"`
	Host          string  `json:"host"`
	Uri           string  `json:"uri"`
	Proto         string  `json:"proto"`
	Status        int32   `json:"status"`
	BytesSent     uint64  `json:"bytesSent"`
	Referer       string  `json:"referer"`
	UserAgent     string  `json:"userAgent"`
	RequestTimeMs float64 `json:"requestTimeMs"`
	CacheStatus   string  `json:"cacheStatus"`
	UpstreamAddr  string  `json:"upstreamAddr"`
	RequestId     string  `json:"requestId"`
	NodeId        string  `json:"nodeId"`
	Timestamp     int64   `json:"timestamp"`
	WafAction     string  `json:"wafAction,omitempty"`
	BlockReason   string  `json:"blockReason,omitempty"`
}

type ReportAccessLogsRequest struct {
	NodeToken string       `json:"nodeToken"`
	Logs      []*AccessLog `json:"logs"`
}

type ReportAccessLogsResponse struct {
	Ok bool `json:"ok"`
}

// RefererConfig Referer 防盗链：白/黑名单（域名后缀匹配）+ 空 Referer 策略
type RefererConfig struct {
	Enabled    bool     `json:"enabled"`
	AllowEmpty bool     `json:"allowEmpty"`
	Whitelist  []string `json:"whitelist"`
	Blacklist  []string `json:"blacklist"`
}

// OpsConfig 集群运维配置：阈值告警（主控侧判定）+ 健康检查参数（节点 probe 循环）
type OpsConfig struct {
	CpuPercent           int     `json:"cpuPercent"`
	MemPercent           int     `json:"memPercent"`
	Load                 float64 `json:"load"`
	ProbeIntervalSeconds int     `json:"probeIntervalSeconds"`
	ProbeTimeoutMs       int     `json:"probeTimeoutMs"`
	ProbeFailThreshold   int     `json:"probeFailThreshold"`
}
