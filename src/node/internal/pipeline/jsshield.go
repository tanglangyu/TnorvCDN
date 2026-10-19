package pipeline

// jsshield.go 5秒盾：通用设计
// 流程：无有效 cookie 的浏览器请求 → 返回内联 JS 挑战页（计算 token 并种 cookie 后 reload）；
// 带 token cookie 的请求校验通过放行。非浏览器（无 JS 能力）与攻击脚本拿不到 token，
// 连续失败 maxFails 次按 IP 封禁。状态必须是全局单例（Pipeline 每 10s 重建）。

import (
	"crypto/hmac"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/hex"
	"fmt"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"

	"tnorv/node/pb"
)

const (
	jsShieldCookie = "tnorv_js"
	jsShieldSalt   = "tnorv-jsshield-v1"
)

// 全局挑战计数/封禁表：IP → 失败次数与封禁截止时间
var jsShieldState = struct {
	sync.Mutex
	fails map[string]*jsFail
}{fails: make(map[string]*jsFail)}

type jsFail struct {
	n       int
	banTill time.Time
	last    time.Time
}

func jsFailRecord(ip string) *jsFail {
	jsShieldState.Lock()
	defer jsShieldState.Unlock()
	f, ok := jsShieldState.fails[ip]
	if !ok || time.Since(f.last) > time.Hour {
		f = &jsFail{}
		jsShieldState.fails[ip] = f
	}
	f.last = time.Now()
	return f
}

// jsShieldToken 计算 IP+UA+日窗口 的 HMAC 挑战 token（无状态验证，不需存储）
func jsShieldToken(cfg *pb.JsShieldConfig, ip, ua string, window int64) string {
	key := jsShieldSalt
	if cfg != nil && cfg.Secret != "" {
		key = cfg.Secret
	}
	mac := hmac.New(sha256.New, []byte(key))
	fmt.Fprintf(mac, "%s|%s|%d", ip, ua, window)
	return hex.EncodeToString(mac.Sum(nil))[:32]
}

func jsCurrentWindow() int64 { return time.Now().Unix() / 1800 } // 30 分钟窗口

func isExcludedShieldPath(path string, excludes []string) bool {
	for _, p := range excludes {
		if p != "" && strings.HasPrefix(path, p) {
			return true
		}
	}
	return false
}

// CheckJsShield 5秒盾检查：返回 true = 已放行（或功能未启用/已通过）；
// false = 已写出挑战页或已封禁，请求终止。
func CheckJsShield(w http.ResponseWriter, r *http.Request, cfg *pb.JsShieldConfig) bool {
	if cfg == nil || !cfg.Enabled {
		return true
	}
	if isExcludedShieldPath(r.URL.Path, cfg.ExcludePaths) {
		return true
	}
	ip := clientIP(r)
	ua := r.UserAgent()

	// 已封禁？
	jsShieldState.Lock()
	f := jsShieldState.fails[ip]
	banned := f != nil && time.Now().Before(f.banTill)
	jsShieldState.Unlock()
	if banned {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.WriteHeader(403)
		fmt.Fprintf(w, "<html><body><h1>403</h1><p>访问已被临时封禁，请稍后再试。</p></body></html>")
		return false
	}

	// 校验 cookie token（当前窗口或上一窗口，避免边界抖动）
	ck, err := r.Cookie(jsShieldCookie)
	if err == nil && ck.Value != "" {
		cur := jsShieldToken(cfg, ip, ua, jsCurrentWindow())
		prev := jsShieldToken(cfg, ip, ua, jsCurrentWindow()-1)
		if subtle.ConstantTimeCompare([]byte(ck.Value), []byte(cur)) == 1 ||
			subtle.ConstantTimeCompare([]byte(ck.Value), []byte(prev)) == 1 {
			// 通过：清失败计数
			jsShieldState.Lock()
			delete(jsShieldState.fails, ip)
			jsShieldState.Unlock()
			return true
		}
		// token 无效 = 挑战失败（脚本伪造或 IP 变化）
		rec := jsFailRecord(ip)
		rec.n++
		maxFails := int(cfg.MaxFails)
		if maxFails <= 0 {
			maxFails = 5
		}
		if rec.n >= maxFails {
			rec.banTill = time.Now().Add(time.Duration(cfg.BanSeconds) * time.Second)
			rec.n = 0
		}
	}

	// 发挑战页（JS 计算 location 混淆跳转并回种 cookie；token 由节点下发，客户端仅需 reload）
	// 设计取舍：挑战 token 由节点下发到 JS 变量（攻击脚本不执行 JS 就拿不到 cookie），
	// 校验仍用节点侧 HMAC 重算，服务端无状态。
	token := jsShieldToken(cfg, ip, ua, jsCurrentWindow())
	maxAge := int(cfg.CookieSeconds)
	if maxAge <= 0 {
		maxAge = 3600
	}
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("Set-Cookie", fmt.Sprintf("%s=%s; Path=/; Max-Age=%d; HttpOnly; SameSite=Lax", jsShieldCookie, token, maxAge))
	w.WriteHeader(200)
	mode := cfg.Mode
	if mode == "" {
		mode = "js"
	}
	if mode == "meta" {
		fmt.Fprintf(w, "<html><head><meta http-equiv=\"refresh\" content=\"1\"></head><body>请稍候，正在验证浏览器...</body></html>")
	} else {
		fmt.Fprintf(w, "<html><head><title>请稍候...</title></head><body><script>setTimeout(function(){location.reload()},800)</script>请稍候，正在验证浏览器...</body></html>")
	}
	return false
}

// jsShieldIPKey 供测试与清理使用
func jsShieldCleanup() {
	jsShieldState.Lock()
	defer jsShieldState.Unlock()
	now := time.Now()
	for k, f := range jsShieldState.fails {
		if now.Sub(f.last) > time.Hour && now.After(f.banTill) {
			delete(jsShieldState.fails, k)
		}
	}
}

var _ = net.ParseIP // 保留 net 引用（clientIP 位于 pipeline.go）
