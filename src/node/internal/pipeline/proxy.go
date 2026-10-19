package pipeline

import (
	"context"
	"crypto/tls"
	"io"
	"log"
	"net"
	"net/http"
	"net/http/httputil"
	"net/url"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"golang.org/x/net/http2"

	"tnorv/node/pb"
)

func copyHeader(dst, src http.Header) {
	for k, vv := range src {
		for _, v := range vv {
			dst.Add(k, v)
		}
	}
}

// 全局健康状态：跨配置热更新保留（router.Update 每 10s 重建 Pipeline，
// 若状态放在 ProxyLayer 内会丢失，导致不健康主源站被反复尝试）
var globalHealth sync.Map // key: "scheme://host:port" -> bool

// L2 汇聚节点表（就近回源）：边缘 L1 未命中先查 L2 缓存，未中再回源（省源站流量）
var globalL2 sync.Map // "host:port" -> struct{}{}

// 全局共享 transport（性能：连接池跨 Pipeline 重建复用，避免每 10s 重建连接池）
var sharedTransport = &http.Transport{
	DialContext:           (&net.Dialer{Timeout: 5 * time.Second}).DialContext,
	MaxIdleConns:          512,
	MaxIdleConnsPerHost:   64,
	IdleConnTimeout:       90 * time.Second,
	TLSHandshakeTimeout:   10 * time.Second,
	ExpectContinueTimeout: 1 * time.Second,
	ResponseHeaderTimeout: 30 * time.Second,
}

// sharedH2cTransport gRPC 回源通道通用设计：h2c 明文 HTTP/2 直连源站，
// AllowHTTP 允许 cleartext；仅站点 Advanced.Grpc 开启且请求为 gRPC 时使用。
var sharedH2cTransport = &http2.Transport{
	AllowHTTP: true,
	DialTLSContext: func(ctx context.Context, network, addr string, _ *tls.Config) (net.Conn, error) {
		// h2c：不做 TLS，直接明文拨号（HTTP/2 Prior Knowledge）
		return (&net.Dialer{Timeout: 5 * time.Second}).DialContext(ctx, network, addr)
	},
}

// isGrpcRequest gRPC 请求识别（content-type: application/grpc*）
func isGrpcRequest(r *http.Request) bool {
	return strings.HasPrefix(r.Header.Get("Content-Type"), "application/grpc")
}

// ProxyLayer 反代层：多源站权重调度 + 主备切换 + 主动健康检查。
// 策略模式：scheduleAlgorithm 决定选择策略（random / round-robin / ip-hash）。
type ProxyLayer struct {
	cfg         *pb.OriginConfig
	mu          sync.Mutex
	rrIndex     int
	grpcEnabled bool // 站点 gRPC h2c 回源开关（pipeline.New 按站点配置设置）
}

func NewProxyLayer(cfg *pb.OriginConfig) *ProxyLayer {
	// 注册源站到全局健康检查表
	if cfg != nil {
		for _, pool := range [][]*pb.Origin{cfg.Primaries, cfg.Backups} {
			for _, o := range pool {
				if o.Enabled {
					globalOrigins.Store(originKey(o), o)
				}
			}
		}
		// 路径源站规则中的源站同样注册健康检查
		for _, pr := range cfg.PathRules {
			for _, pool := range [][]*pb.Origin{pr.Primaries, pr.Backups} {
				for _, o := range pool {
					if o.Enabled {
						globalOrigins.Store(originKey(o), o)
					}
				}
			}
		}
	}
	// 全局健康检查循环只启动一次（跨 Pipeline 重建）
	healthOnce.Do(func() {
		go (&ProxyLayer{}).healthLoopGlobal()
	})
	return &ProxyLayer{cfg: cfg}
}

var healthOnce sync.Once

func originKey(o *pb.Origin) string {
	return o.Scheme + "://" + o.Host + ":" + itoaPort(o.Port)
}

// originSchemeHost websocket 透传用的目标地址（s3 按https 视角同常规代理）
func originSchemeHost(o *pb.Origin) (scheme, hostPort string) {
	scheme = o.Scheme
	if scheme == "s3" {
		scheme = "https"
	}
	if scheme == "" {
		scheme = "http"
	}
	return scheme, net.JoinHostPort(o.Host, itoaPort(o.Port))
}

// hostFor 回源 Host 决策（与 ServeHTTP 的 Director 同语义）：路径规则覆盖 > 全局 HostOverride > 跟随源站 > 保持访问域名
func hostFor(origin *pb.Origin, prule *pb.PathOriginRule, cfg *pb.OriginConfig, r *http.Request) string {
	hostOverride := cfg.HostOverride
	if prule != nil && prule.HostOverride != "" {
		hostOverride = prule.HostOverride
	}
	switch {
	case hostOverride != "":
		return hostOverride
	case cfg.FollowOriginHost:
		return net.JoinHostPort(origin.Host, itoaPort(origin.Port))
	default:
		host := r.Host
		if i := strings.Index(host, ":"); i >= 0 {
			host = host[:i]
		}
		return host
	}
}

func itoaPort(p int) string {
	if p == 0 {
		return "80"
	}
	digits := ""
	n := p
	for n > 0 {
		digits = string(rune('0'+n%10)) + digits
		n /= 10
	}
	return digits
}

// pick 按算法选择健康源站；主源站全不健康时尝试备用源站。
// 路径源站规则优先：命中 pathPrefix 时使用该规则自己的主备源站组。
func (p *ProxyLayer) pick(r *http.Request) (origin *pb.Origin, rule *pb.PathOriginRule) {
	if pr := p.matchPathRule(r.URL.Path); pr != nil {
		if o := p.pickPool(pr.Primaries, r); o != nil {
			return o, pr
		}
		if o := p.pickPool(pr.Backups, r); o != nil {
			return o, pr
		}
		// 路径规则源站全挂：回退默认源站组
	}
	if primary := p.pickPool(p.cfg.Primaries, r); primary != nil {
		return primary, nil
	}
	return p.pickPool(p.cfg.Backups, r), nil
}

// matchPathRule 前缀匹配路径源站规则（先到先得）
func (p *ProxyLayer) matchPathRule(path string) *pb.PathOriginRule {
	for _, pr := range p.cfg.PathRules {
		if pr.PathPrefix != "" && strings.HasPrefix(path, pr.PathPrefix) {
			return pr
		}
	}
	return nil
}

// pickFrom 只返回健康的源站；全部不健康返回 nil（让备用源站接管）
func (p *ProxyLayer) pickFrom(pool []*pb.Origin, r *http.Request) *pb.Origin {
	return p.pickPool(pool, r)
}

func (p *ProxyLayer) pickPool(pool []*pb.Origin, r *http.Request) *pb.Origin {
	var candidates []*pb.Origin
	for _, o := range pool {
		if o.Enabled && p.isHealthy(o) {
			candidates = append(candidates, o)
		}
	}
	if len(candidates) == 0 {
		return nil
	}

	switch p.cfg.ScheduleAlgorithm {
	case "ip-hash":
		ip := clientIP(r)
		var sum byte
		for i := 0; i < len(ip); i++ {
			sum += ip[i]
		}
		return pickWeighted(candidates, int64(sum))
	case "round-robin":
		p.mu.Lock()
		idx := p.rrIndex
		p.rrIndex++
		p.mu.Unlock()
		return pickWeighted(candidates, int64(idx))
	default: // random：按权重随机（平滑加权，Weight=0 视为 1）
		return pickWeighted(candidates, time.Now().UnixNano())
	}
}

// pickWeighted 加权选择：total = Σweight（weight<=0 视为 1），在 [0,total) 上定位命中源站。
// round-robin 时以递增序号作为游标，权重比即流量比；ip-hash/random 时作哈希/随机落点。
func pickWeighted(candidates []*pb.Origin, cursor int64) *pb.Origin {
	var total int64
	weights := make([]int64, len(candidates))
	for i, o := range candidates {
		w := int64(o.Weight)
		if w <= 0 {
			w = 1
		}
		weights[i] = w
		total += w
	}
	if total == 0 {
		return candidates[int(cursor)%len(candidates)]
	}
	pos := cursor % total
	if pos < 0 {
		pos += total
	}
	for i, w := range weights {
		if pos < w {
			return candidates[i]
		}
		pos -= w
	}
	return candidates[len(candidates)-1]
}

func (p *ProxyLayer) isHealthy(o *pb.Origin) bool {
	key := originKey(o)
	if v, ok := globalHealth.Load(key); ok {
		return v.(bool)
	}
	return true // 未知状态视为健康（首次请求即探测）
}

// ServeHTTP 反代转发：优先 L2 汇聚节点（就近回源），无 L2 或 L2 失败则直连源站
func (p *ProxyLayer) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	// L2 就近回源：存在同集群 L2 节点时优先经 L2 转发（L2 命中则源站零流量）
	// 拨测驱动的动态回源：主控计算绕行更优时，经兄弟节点 :3128 转发（与 L2 相同的转发协议）
	if via, _ := globalVia.Load().(string); via != "" {
		viaReq := r.Clone(r.Context())
		viaReq.URL.Scheme = "http"
		viaReq.URL.Host = via
		viaReq.Host = r.Host
		viaReq.Header.Set("X-Tnorv-L2", "1")
		resp, err := sharedTransport.RoundTrip(viaReq)
		if err == nil && resp.StatusCode < 500 {
			copyHeader(w.Header(), resp.Header)
			w.WriteHeader(resp.StatusCode)
			if resp.Body != nil {
				io.Copy(w, resp.Body)
				resp.Body.Close()
			}
			return
		}
		if resp != nil {
			resp.Body.Close()
		}
		globalHealth.Store("l2:"+via, false) // 中转失败：回退直连
	}

	if l2 := pickL2(); l2 != "" {
		l2Req := r.Clone(r.Context())
		l2Req.URL.Scheme = "http"
		l2Req.URL.Host = l2
		l2Req.Host = r.Host
		l2Req.Header.Set("X-Tnorv-L2", "1")
		resp, err := sharedTransport.RoundTrip(l2Req)
		if err == nil && resp.StatusCode < 500 {
			copyHeader(w.Header(), resp.Header)
			w.WriteHeader(resp.StatusCode)
			if resp.Body != nil {
				io.Copy(w, resp.Body)
				resp.Body.Close()
			}
			return
		}
		if resp != nil {
			resp.Body.Close()
		}
		// L2 失败：标记不健康，回退直连源站
		globalHealth.Store("l2:"+l2, false)
	}

	origin, prule := p.pick(r)
	if origin == nil {
		http.Error(w, "502 Bad Gateway: no healthy upstream", http.StatusBadGateway)
		return
	}

	// SSRF 防护：源站地址禁止回环/链路本地（安全基线；内网源站场景由管理员配置白名单放行，P1）
	if isForbiddenHost(origin.Host) {
		http.Error(w, "502 Bad Gateway: origin not allowed", http.StatusBadGateway)
		return
	}

	// S3 对象存储源站：path-style 映射 /path → endpoint/bucket/path + SigV4 签名
	if isS3Origin(origin) {
		p.serveS3(w, r, origin)
		return
	}

	// FastCGI 回源（scheme=fastcgi）：经 FastCGI 协议转发（PHP-FPM / fcgiwrap）
	if origin.Scheme == "fastcgi" {
		p.serveFastCGI(w, r, origin, prule)
		return
	}

	target := &url.URL{
		Scheme: map[string]string{"s3": "https"}[origin.Scheme],
		Host:   net.JoinHostPort(origin.Host, itoaPort(origin.Port)),
	}
	if target.Scheme == "" {
		target.Scheme = origin.Scheme
	}

	proxy := httputil.NewSingleHostReverseProxy(target)
	proxy.Transport = sharedTransport
	// gRPC 透传：站点开启 gRPC 且请求为 application/grpc 时走 h2c 回源
	if p.grpcEnabled && isGrpcRequest(r) {
		proxy.Transport = sharedH2cTransport
	}

	// Host 策略：路径规则可覆盖默认
	hostOverride := p.cfg.HostOverride
	followOrigin := p.cfg.FollowOriginHost
	if prule != nil && prule.HostOverride != "" {
		hostOverride = prule.HostOverride
	}

	originalDirector := proxy.Director
	proxy.Director = func(req *http.Request) {
		originalDirector(req)
		// 回源 Host 策略通用设计
		switch {
		case hostOverride != "":
			req.Host = hostOverride
		case followOrigin:
			req.Host = target.Host
		default:
			// 保持用户访问的域名（req.Host 不变）
		}
		// 传递真实 IP通用设计
		if clientIP := clientIP(r); clientIP != "" {
			req.Header.Set("X-Real-IP", clientIP)
			xff := req.Header.Get("X-Forwarded-For")
			if xff == "" {
				req.Header.Set("X-Forwarded-For", clientIP)
			} else {
				req.Header.Set("X-Forwarded-For", xff+", "+clientIP)
			}
		}
	}

	proxy.ErrorHandler = func(w http.ResponseWriter, r *http.Request, err error) {
		// 标记不健康，快速失败
		globalHealth.Store(originKey(origin), false)
		w.WriteHeader(http.StatusBadGateway)
	}

	proxy.ServeHTTP(w, r)

	// 请求成功则恢复健康标记
	globalHealth.Store(originKey(origin), true)
}

// pickL2 选择一个健康的 L2 汇聚节点（RoundTrip 用 HTTP 代理语义，返回 host:port）
func pickL2() string {
	var candidates []string
	globalL2.Range(func(k, v any) bool {
		key := k.(string)
		if h, ok := globalHealth.Load("l2:" + key); ok && !h.(bool) {
			return true // 不健康的 L2 跳过
		}
		candidates = append(candidates, key)
		return true
	})
	if len(candidates) == 0 {
		return ""
	}
	return candidates[time.Now().UnixNano()%int64(len(candidates))]
}

// SetRoutingPlan 应用主控下发的动态回源路径（拨测驱动）：
// mode=via 时把中转节点当作转发代理（经 peer :3128），直连路径被拨测证明更快时保持 direct
var globalVia atomic.Value // string "host:3128" 或 ""

func SetRoutingPlan(plan *pb.RoutingPlan) {
	if plan != nil && plan.Mode == "via" && plan.Via != "" {
		globalVia.Store(plan.Via)
		log.Printf("[routing] 经中转回源: via=%s directMs=%.1f viaMs=%.1f", plan.Via, plan.DirectMs, plan.ViaMs)
	} else {
		globalVia.Store("")
	}
}

// SetL2Nodes 更新 L2 汇聚节点表（配置热更新时调用）
func SetL2Nodes(ips []string) {
	globalL2 = sync.Map{}
	for _, ip := range ips {
		globalL2.Store(ip+":3128", struct{}{})
	}
}

// isForbiddenHost SSRF 防护：禁止回环/链路本地/未指定地址作为源站
func isForbiddenHost(host string) bool {
	ip := net.ParseIP(host)
	if ip == nil {
		return false // 域名由 DNS 解析，P1 加解析结果校验
	}
	return ip.IsLoopback() || ip.IsLinkLocalUnicast() || ip.IsLinkLocalMulticast() || ip.IsUnspecified()
}

// healthLoopGlobal 全局源站健康检查：扫描所有已注册源站（由各 ProxyLayer 注册到 globalOrigins）
var globalOrigins sync.Map // originKey -> *pb.Origin

// healthLoopGlobal 周期探测（全局单例）
func (p *ProxyLayer) healthLoopGlobal() {
	ticker := time.NewTicker(15 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		globalOrigins.Range(func(_, v any) bool {
			o := v.(*pb.Origin)
			if !o.Enabled {
				return true
			}
			conn, err := net.DialTimeout("tcp", net.JoinHostPort(o.Host, itoaPort(o.Port)), 3*time.Second)
			healthy := err == nil
			if conn != nil {
				conn.Close()
			}
			globalHealth.Store(originKey(o), healthy)
			return true
		})
	}
}

var _ = context.Background
var _ = strings.TrimSpace

// serveS3 对象存储源站转发：URL 映射为 endpoint/bucket/key，SigV4 签名后转发。
// 对象存储只支持读语义（GET/HEAD），写请求回 405。
func (p *ProxyLayer) serveS3(w http.ResponseWriter, r *http.Request, origin *pb.Origin) {
	if r.Method != http.MethodGet && r.Method != http.MethodHead {
		http.Error(w, "405 Method Not Allowed: object storage origin is read-only", http.StatusMethodNotAllowed)
		return
	}
	if isForbiddenHost(origin.Host) {
		http.Error(w, "502 Bad Gateway: origin not allowed", http.StatusBadGateway)
		return
	}
	scheme := "https"
	if origin.Port != 443 { // 仅 443 走 TLS；其余端口视为明文端点（MinIO/OSS 自建常见）
		scheme = "http"
	}
	u := &url.URL{
		Scheme: scheme,
		Host:   net.JoinHostPort(origin.Host, itoaPort(origin.Port)),
		Path:   "/" + origin.Bucket + r.URL.Path,
	}
	if r.URL.RawQuery != "" {
		u.RawQuery = r.URL.RawQuery
	}
	req := r.Clone(r.Context())
	req.URL = u
	req.Host = u.Host
	req.Header.Del("Cookie") // 对象存储不接受站点 cookie
	s3Sign(req, origin)
	resp, err := sharedTransport.RoundTrip(req)
	if err != nil {
		globalHealth.Store(originKey(origin), false)
		http.Error(w, "502 Bad Gateway: object storage unreachable", http.StatusBadGateway)
		return
	}
	defer resp.Body.Close()
	globalHealth.Store(originKey(origin), true)
	copyHeader(w.Header(), resp.Header)
	w.WriteHeader(resp.StatusCode)
	io.Copy(w, resp.Body)
}
