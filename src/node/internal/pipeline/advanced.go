// advanced.go 高级配置管道：URL跳转/重写/HTTP报头/访问鉴权/请求限制/字符编码。
// 通用设计。
package pipeline

import (
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"net/http"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"

	"tnorv/node/pb"
)

// advCompiled 预编译的高级配置（Pipeline 重建时编译一次）
type advCompiled struct {
	cfg        *pb.AdvancedConfig
	redirects  []compiledRedirect
	rewrites   []compiledRewrite
	reqLimit   *reqLimiter
	maxBodyMB  int64
}

type compiledRedirect struct {
	re *regexp.Regexp
	r  *pb.UrlRedirectRule
}

type compiledRewrite struct {
	re *regexp.Regexp
	r  *pb.RewriteRule
}

// reqLimiter 每 IP 每分钟请求数限制（滑窗计数，内存 O(active IPs)）
type reqLimiter struct {
	mu     sync.Mutex
	limit  int64
	counts map[string]*ipWindow
}

type ipWindow struct {
	windowStart time.Time
	count       int64
}

func newReqLimiter(perMinute int64) *reqLimiter {
	return &reqLimiter{limit: perMinute, counts: make(map[string]*ipWindow)}
}

func (l *reqLimiter) allow(ip string) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	now := time.Now()
	w, ok := l.counts[ip]
	if !ok || now.Sub(w.windowStart) >= time.Minute {
		// 简易清理：条目过多时重置全表
		if len(l.counts) > 100000 {
			l.counts = make(map[string]*ipWindow)
		}
		l.counts[ip] = &ipWindow{windowStart: now, count: 1}
		return true
	}
	w.count++
	return w.count <= l.limit
}

func compileAdvanced(cfg *pb.AdvancedConfig) *advCompiled {
	if cfg == nil {
		return nil
	}
	ac := &advCompiled{cfg: cfg}
	for _, r := range cfg.Redirects {
		if !r.Enabled || r.Pattern == "" || r.Target == "" {
			continue
		}
		if re, err := regexp.Compile(r.Pattern); err == nil {
			ac.redirects = append(ac.redirects, compiledRedirect{re: re, r: r})
		}
	}
	for _, r := range cfg.Rewrites {
		if !r.Enabled || r.Pattern == "" {
			continue
		}
		if re, err := regexp.Compile(r.Pattern); err == nil {
			ac.rewrites = append(ac.rewrites, compiledRewrite{re: re, r: r})
		}
	}
	if cfg.RequestLimitPerMinute > 0 {
		ac.reqLimit = newReqLimiter(cfg.RequestLimitPerMinute)
	}
	ac.maxBodyMB = cfg.MaxBodySizeMB
	return ac
}

// CheckAdvanced 在安全链之前执行（顺序：请求限制 → Basic 鉴权 → URL 跳转）。
// 返回 true 表示请求已终结（跳转/拒绝）。
func CheckAdvanced(ac *advCompiled, w http.ResponseWriter, r *http.Request) bool {
	if ac == nil {
		return false
	}
	// 1) 请求限制（次/分钟/IP）
	if ac.reqLimit != nil && !ac.reqLimit.allow(clientIP(r)) {
		w.Header().Set("Retry-After", "60")
		http.Error(w, "请求过于频繁，请稍后再试", http.StatusTooManyRequests)
		return true
	}

	// 1.5) 请求体大小限制（MaxBodySizeMB，0=不限）。声明了 Content-Length 的超限请求直接 413
	if ac.maxBodyMB > 0 && r.ContentLength > ac.maxBodyMB*1024*1024 {
		http.Error(w, "请求体过大", http.StatusRequestEntityTooLarge)
		return true
	}

	// 2) 访问鉴权（Basic Auth）
	if ac.cfg.BasicAuth != nil && ac.cfg.BasicAuth.Enabled && len(ac.cfg.BasicAuth.Users) > 0 {
		user, pass, ok := r.BasicAuth()
		valid := false
		if ok {
			full := sha256Hex(pass)
			for _, u := range ac.cfg.BasicAuth.Users {
				if subtle.ConstantTimeCompare([]byte(u.Username), []byte(user)) == 1 &&
					subtle.ConstantTimeCompare([]byte(strings.ToLower(u.PasswordHash)), []byte(full)) == 1 {
					valid = true
					break
				}
			}
		}
		if !valid {
			w.Header().Set("WWW-Authenticate", `Basic realm="TnorvCDN"`)
			http.Error(w, "需要访问鉴权", http.StatusUnauthorized)
			return true
		}
	}

	// 3) URL 跳转（301/302）
	for _, rd := range ac.redirects {
		if m := rd.re.FindStringSubmatch(r.URL.Path); m != nil {
			target := rd.r.Target
			for i := 1; i < len(m); i++ {
				target = strings.ReplaceAll(target, "$"+strconv.Itoa(i), m[i])
			}
			code := rd.r.Code
			if code != 302 && code != 307 && code != 308 {
				code = 301
			}
			http.Redirect(w, r, target, code)
			return true
		}
	}
	return false
}

// WebpRewrite WebP 自适应：Accept 支持 webp 且请求 png/jpg/jpeg 时改写为 .webp 路径。
// 返回改写后路径（"" = 不改写，走原路径）。
func WebpRewrite(ac *advCompiled, accept, path string) string {
	if ac == nil || !ac.cfg.WebpOptimization {
		return ""
	}
	if !strings.Contains(accept, "image/webp") {
		return ""
	}
	lower := strings.ToLower(path)
	for _, ext := range []string{".png", ".jpg", ".jpeg"} {
		if strings.HasSuffix(lower, ext) {
			return path[:len(path)-len(ext)] + ".webp"
		}
	}
	return ""
}

// ApplyRewrites 请求改写（转发前修改 r.URL.Path），返回改写后的路径。
func ApplyRewrites(ac *advCompiled, path string) string {
	if ac == nil {
		return path
	}
	for _, rw := range ac.rewrites {
		if m := rw.re.FindStringSubmatchIndex(path); m != nil {
			out := []byte(rw.re.ExpandString(nil, rw.r.Replacement, path, m))
			return string(out)
		}
	}
	return path
}

// ApplyRequestHeaders 请求方向报头（转发前）。
func ApplyRequestHeaders(ac *advCompiled, h http.Header) {
	if ac == nil {
		return
	}
	for _, hr := range ac.cfg.Headers {
		if !hr.Enabled || hr.Direction != "request" || hr.Name == "" {
			continue
		}
		switch hr.Action {
		case "del":
			h.Del(hr.Name)
		case "add":
			h.Add(hr.Name, hr.Value)
		default: // set
			h.Set(hr.Name, hr.Value)
		}
	}
}

// ApplyResponseHeaders 响应方向报头（WriteHeader 之前调用）。
func ApplyResponseHeaders(ac *advCompiled, h http.Header) {
	if ac == nil {
		return
	}
	for _, hr := range ac.cfg.Headers {
		if !hr.Enabled || hr.Direction != "response" || hr.Name == "" {
			continue
		}
		switch hr.Action {
		case "del":
			h.Del(hr.Name)
		case "add":
			h.Add(hr.Name, hr.Value)
		default:
			h.Set(hr.Name, hr.Value)
		}
	}
}

// ApplyCharset 设置响应字符编码（源站未指定 Content-Type charset 时）。
func ApplyCharset(ac *advCompiled, h http.Header) {
	if ac == nil || ac.cfg.Charset == "" {
		return
	}
	ct := h.Get("Content-Type")
	if ct != "" && !strings.Contains(ct, "charset") && (strings.HasPrefix(ct, "text/") || strings.Contains(ct, "json") || strings.Contains(ct, "xml")) {
		h.Set("Content-Type", ct+"; charset="+ac.cfg.Charset)
	}
}

func sha256Hex(s string) string {
	sum := sha256.Sum256([]byte(s))
	return hex.EncodeToString(sum[:])
}
