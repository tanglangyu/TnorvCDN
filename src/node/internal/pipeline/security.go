package pipeline

import (
	"net"
	"net/http"
	"net/url"
	"strings"
	"sync"

	"tnorv/node/pb"
)

// BlockResult 拦截结果
type BlockResult struct {
	Status    int
	Reason    string
	Redirect  string  // 302 目标（waf redirect 动作）
	LogOnly   bool    // log 动作：不拦截仅记录
	Challenge bool    // captcha/js_cookie 动作：发人机验证挑战页
}

// cfgWafValue 找回规则名对应的 redirect 目标
func (s *Security) cfgWafValue(name string) string {
	for _, r := range s.cfg.WafRules {
		if r.Name == name {
			return r.ActionValue
		}
	}
	return "/"
}

// Security 安全检查链：黑白名单 → UA 名单 → WAF 规则引擎 → URL 鉴权 → CC
type Security struct {
	cfg      *pb.SecurityConfig
	whiteNet []*net.IPNet
	blackNet []*net.IPNet
	cc       *ccLimiter
	waf      []*compiledWafRule
	urlAuth  *urlAuthView
}

// 全局 CC 限流器（跨配置热更新保留）
var (
	globalCc   *ccLimiter
	globalCcMu sync.Mutex
)

func NewSecurity(cfg *pb.SecurityConfig) *Security {
	s := &Security{cfg: cfg}
	for _, rule := range cfg.IpWhitelist {
		if n := parseCIDR(rule.Cidr); n != nil {
			s.whiteNet = append(s.whiteNet, n)
		}
	}
	for _, rule := range cfg.IpBlacklist {
		if n := parseCIDR(rule.Cidr); n != nil {
			s.blackNet = append(s.blackNet, n)
		}
	}
	// 区域封禁（GeoIP CIDR）：随黑白名单一起匹配
	for _, cidr := range cfg.DeniedCidrs {
		if n := parseCIDR(cidr); n != nil {
			s.blackNet = append(s.blackNet, n)
		}
	}
	if cfg.Cc.Enabled {
		s.cc = getOrCreateCcLimiter(cfg.Cc.Threshold, cfg.Cc.WindowSeconds, cfg.Cc.BanSeconds)
	}
	// WAF 规则编译（预编译正则/CIDR/列表，请求期零开销）
	views := make([]*wafRuleView, len(cfg.WafRules))
	for i, r := range cfg.WafRules {
		conds := make([]*wafConditionView, len(r.Conditions))
		for j, c := range r.Conditions {
			conds[j] = &wafConditionView{Field: c.Field, Operator: c.Operator, Value: c.Value, Negate: c.Negate}
		}
		views[i] = &wafRuleView{
			Name: r.Name, Enabled: r.Enabled, Conditions: conds,
			Action: r.Action, ActionValue: r.ActionValue, Priority: r.Priority,
		}
	}
	s.waf = compileWafRules(views)
	s.urlAuth = &urlAuthView{
		Enabled:       cfg.UrlAuth.Enabled,
		Type:          cfg.UrlAuth.Type,
		Key:           cfg.UrlAuth.Key,
		ExpirySeconds: cfg.UrlAuth.ExpirySeconds,
	}
	return s
}

// getOrCreateCcLimiter 全局唯一 CC 限流器：配置热更新重建 Pipeline 时保留计数状态。
// 阈值变化时更新参数但保留各 IP 计数历史。
func getOrCreateCcLimiter(threshold, windowSec, banSec int) *ccLimiter {
	globalCcMu.Lock()
	defer globalCcMu.Unlock()
	if globalCc == nil {
		globalCc = newCcLimiter(threshold, windowSec, banSec)
	} else {
		globalCc.UpdateParams(threshold, windowSec, banSec)
	}
	return globalCc
}

func parseCIDR(s string) *net.IPNet {
	if !strings.Contains(s, "/") {
		s += "/32"
	}
	if _, n, err := net.ParseCIDR(s); err == nil {
		return n
	}
	return nil
}

func inList(nets []*net.IPNet, ip net.IP) bool {
	for _, n := range nets {
		if n.Contains(ip) {
			return true
		}
	}
	return false
}

// blockStatus firewall-actions：拦截动作状态码（默认 403；站点/集群默认可覆盖为 404 等）
func (s *Security) blockStatus() int {
	if s.cfg.BlockStatus > 0 {
		return s.cfg.BlockStatus
	}
	return 403
}

// Check 依次执行检查；返回 nil 表示放行。
// locRef：location 级防盗链覆盖（nil=用站点级）；locAllow：location 白名单命中跳过整条链。
func (s *Security) Check(r *http.Request, srv *pb.Server) *BlockResult {
	if r.Header.Get("X-Tnorv-Loc-Allow") == "1" {
		return nil
	}
	ip := net.ParseIP(clientIP(r))
	if ip == nil {
		return &BlockResult{Status: 400, Reason: "invalid client ip"}
	}

	// 白名单优先通用设计
	if inList(s.whiteNet, ip) {
		return nil
	}
	if inList(s.blackNet, ip) {
		return &BlockResult{Status: s.blockStatus(), Reason: "ip blacklisted"}
	}

	// UA 名单（允许优先）
	if len(s.cfg.UaRules) > 0 {
		ua := r.UserAgent()
		for _, rule := range s.cfg.UaRules {
			if matchWildcard(ua, rule.Keyword) && rule.Allow {
				return nil
			}
		}
		for _, rule := range s.cfg.UaRules {
			if matchWildcard(ua, rule.Keyword) && !rule.Allow {
				return &BlockResult{Status: s.blockStatus(), Reason: "user-agent blocked"}
			}
		}
	}

	// WAF 规则引擎（条件-操作符-动作，优先级排序；allow 命中即放行后续链）
	if action, ruleName := matchWaf(s.waf, r); action != "" {
		switch action {
		case "allow":
			return nil
		case "redirect":
			return &BlockResult{Status: 302, Reason: "waf redirect: " + ruleName, Redirect: s.cfgWafValue(ruleName)}
		case "log":
			// AI 防护：log 命中视为可疑 → 异步采样送 LLM 语义判定
			go AiGuardAudit(s.cfg.AiGuard, r, "waf log: "+ruleName)
			return &BlockResult{Status: 0, Reason: "waf log: " + ruleName, LogOnly: true}
		case "captcha", "js_cookie":
			// 人机验证动作：返回 JS 挑战页（与 5 秒盾同机制，通过后种 cookie 放行）
			return &BlockResult{Status: 200, Reason: "waf challenge: " + ruleName, Challenge: true}
		default: // block 等一律拦截（状态码可由 firewall-actions 配置）
			return &BlockResult{Status: s.blockStatus(), Reason: "waf: " + ruleName}
		}
	}

	// URL 防盗链鉴权（A-D 签名方案）
	if reason := checkUrlAuth(s.urlAuth, r); reason != "" {
		return &BlockResult{Status: 403, Reason: "url auth: " + reason}
	}

	// Referer 防盗链（黑名单优先 → 白名单为空=仅放空白名单逻辑 → 空 Referer 策略）
	// location 级覆盖优先（路由规则配了 referers 时整组替换）
	if ref := s.cfg.Referers; ref != nil && ref.Enabled {
		if reason := checkReferer(ref, r); reason != "" {
			return &BlockResult{Status: 403, Reason: "referer: " + reason}
		}
	}

	// CC 频率限制
	if s.cc != nil && !isExcludedPath(r.URL.Path, s.cfg.Cc.ExcludePaths) {
		if !s.cc.Allow(clientIP(r)) {
			return &BlockResult{Status: 429, Reason: "rate limited"}
		}
	}

	return nil
}

func isExcludedPath(path string, excludes []string) bool {
	for _, e := range excludes {
		if ok, _ := matchPrefix(path, e); ok {
			return true
		}
	}
	return false
}

func matchPrefix(s, prefix string) (bool, error) {
	return strings.HasPrefix(s, prefix), nil
}

// matchWildcard 支持 * 通配（不区分大小写，通用设计
func matchWildcard(s, pattern string) bool {
	s, pattern = strings.ToLower(s), strings.ToLower(pattern)
	if pattern == "*" || pattern == "" {
		return true
	}
	parts := strings.Split(pattern, "*")
	pos := 0
	for i, part := range parts {
		if part == "" {
			continue
		}
		idx := strings.Index(s[pos:], part)
		if idx < 0 {
			return false
		}
		if i == 0 && idx != 0 && !strings.HasPrefix(pattern, "*") {
			return false
		}
		pos += idx + len(part)
	}
	if !strings.HasSuffix(pattern, "*") && !strings.HasSuffix(s, parts[len(parts)-1]) {
		return false
	}
	return true
}

// checkReferer Referer 防盗链判定：域名后缀匹配（example.com 匹配 example.com / *.example.com）。
// 黑名单命中 → 拒；空 Referer → AllowEmpty 决定；白名单非空且未命中 → 拒；其余放行。
func checkReferer(cfg *pb.RefererConfig, r *http.Request) string {
	ref := r.Header.Get("Referer")
	host := ""
	if ref != "" {
		if u, err := url.Parse(ref); err == nil {
			host = strings.ToLower(u.Hostname())
		}
	}
	matchList := func(list []string) bool {
		for _, suffix := range list {
			suffix = strings.ToLower(strings.TrimSpace(suffix))
			if suffix == "" {
				continue
			}
			if host == suffix || strings.HasSuffix(host, "."+suffix) {
				return true
			}
		}
		return false
	}
	if host == "" {
		// 空 Referer：直开/书签/部分 App
		if !cfg.AllowEmpty {
			return "empty referer denied"
		}
	} else if matchList(cfg.Blacklist) {
		return "referer blacklisted"
	} else if len(cfg.Whitelist) > 0 && !matchList(cfg.Whitelist) {
		return "referer not in whitelist"
	}
	return ""
}
