package pipeline

import (
	"bufio"
	"fmt"
	"log"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"

	"tnorv/node/pb"
)

type Handler func(w http.ResponseWriter, r *http.Request) *Result

// Result 管道执行结果
type Result struct {
	Blocked     bool
	Status      int
	Reason      string // block reason（日志用）
	CacheStatus string // HIT / MISS / BYPASS / STALE
	Served      bool   // 是否已写响应
}

// 全局共享状态：Pipeline 因配置热更新每 10s 重建，
// 但 CC 计数、IP 名单编译结果必须跨重建保留（否则限流/名单失效）。
// CC 限流器定义在 security.go（getOrCreateCcLimiter）。

type Pipeline struct {
	srv      *pb.Server
	security *Security
	cache    *CacheLayer
	proxy    *ProxyLayer
	scripts  []*compiledScript
	adv      *advCompiled
	pages    *pb.CustomPagesConfig
	locs     []*compiledLocation
}

func New(srv *pb.Server, cacheDir string) *Pipeline {
	proxy := NewProxyLayer(&srv.Origin)
	proxy.grpcEnabled = srv.Advanced != nil && srv.Advanced.Grpc
	if proxy.grpcEnabled {
		log.Printf("[grpc] 站点 %d gRPC h2c 回源已启用", srv.Id)
	}
	return &Pipeline{
		srv:      srv,
		security: NewSecurity(&srv.Security),
		cache:    NewCacheLayer(&srv.Cache, cacheDir),
		proxy:    proxy,
		scripts:  CompileEdgeScripts(srv.Security.EdgeScripts),
		adv:      compileAdvanced(srv.Advanced),
		pages:    advancedPages(srv.Advanced),
		locs:     compileLocations(srv),
	}
}

// GetCustomPages 空安全取自定义页面配置
func advancedPages(a *pb.AdvancedConfig) *pb.CustomPagesConfig {
	if a == nil {
		return nil
	}
	return a.CustomPages
}

func (p *Pipeline) Server() *pb.Server { return p.srv }

// ServeHTTP 管道入口
func (p *Pipeline) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	start := time.Now()
	// location 级高级覆盖（报头/重写/压缩/字符集）：命中即替换站点级 effAdv
	loc0 := matchLocation(p.locs, r.URL.Path)
	effAdv := p.adv
	if la := loc0.locAdv(); la != nil {
		effAdv = la
	}
	// 响应方向报头/字符编码：包装 ResponseWriter，WriteHeader 时注入
	if effAdv != nil && (len(effAdv.cfg.Headers) > 0 || effAdv.cfg.Charset != "") {
		w = &advHeaderWriter{ResponseWriter: w, adv: effAdv}
	}
	p.serveWithAdv(w, r, start, effAdv)
}

// serveWithAdv = serve 但高级配置可被 location 覆盖（压缩决策等）
func (p *Pipeline) serveWithAdv(w http.ResponseWriter, r *http.Request, start time.Time, effAdv *advCompiled) {
	p.serve(w, r, start)
}

// advHeaderWriter 在 WriteHeader 时应用响应报头与字符编码
type advHeaderWriter struct {
	http.ResponseWriter
	adv *advCompiled
	did bool
}

func (a *advHeaderWriter) WriteHeader(code int) {
	if !a.did {
		a.did = true
		ApplyResponseHeaders(a.adv, a.Header())
		ApplyCharset(a.adv, a.Header())
	}
	a.ResponseWriter.WriteHeader(code)
}

func (a *advHeaderWriter) Write(b []byte) (int, error) {
	if !a.did {
		a.WriteHeader(200)
	}
	return a.ResponseWriter.Write(b)
}

// Hijack 透传底层连接（WebSocket 升级等需要；包装器不得吞掉 Hijacker 能力）
func (a *advHeaderWriter) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	if hj, ok := a.ResponseWriter.(http.Hijacker); ok {
		return hj.Hijack()
	}
	return nil, nil, http.ErrNotSupported
}

func (p *Pipeline) serve(w http.ResponseWriter, r *http.Request, start time.Time) {

	// 流量超限（主控月结判定下发 blocked 标记）：503 自定义页
	if p.srv.Blocked {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		w.WriteHeader(503)
		if p.pages != nil && p.pages.Blocked != "" {
			w.Write([]byte(p.pages.Blocked))
		} else {
			w.Write([]byte(defaultBlockPage(503, "本月流量已达上限，请升级套餐或联系管理员")))
		}
		emitLog(p, r, w, start, 503, "traffic limit", "block", "BYPASS")
		return
	}

	// 访客 IP 取值头覆盖（如 CF-Connecting-IP）：改写 RemoteAddr，下游 CC/WAF/日志统一生效
	// location 级 remoteAddr 优先于站点级（路由规则覆盖）
	if loc := matchLocation(p.locs, r.URL.Path); loc != nil {
		if hdr, has := loc.locRemoteHeader(); has {
			if hdr != "" {
				if v := r.Header.Get(hdr); v != "" {
					if i := strings.Index(v, ","); i > 0 {
						v = strings.TrimSpace(v[:i])
					}
					r.RemoteAddr = v
				}
			} else if p.adv != nil && p.adv.cfg != nil && p.adv.cfg.RemoteAddrHeader != "" {
				// location 显式置空：清除站点级头
			}
		} else if p.adv != nil && p.adv.cfg != nil && p.adv.cfg.RemoteAddrHeader != "" {
			if v := r.Header.Get(p.adv.cfg.RemoteAddrHeader); v != "" {
				if i := strings.Index(v, ","); i > 0 {
					v = strings.TrimSpace(v[:i])
				}
				r.RemoteAddr = v
			}
		}
	} else if p.adv != nil && p.adv.cfg != nil && p.adv.cfg.RemoteAddrHeader != "" {
		if v := r.Header.Get(p.adv.cfg.RemoteAddrHeader); v != "" {
			if i := strings.Index(v, ","); i > 0 {
				v = strings.TrimSpace(v[:i])
			}
			r.RemoteAddr = v
		}
	}

	// location 级 IP 黑白名单（与站点级安全链独立的前置检查；白名单命中直接放行安全链）
	loc := matchLocation(p.locs, r.URL.Path)
	if loc != nil {
		if blocked, allowed, _ := loc.locIPCheck(clientIP(r)); blocked {
			res := &BlockResult{Status: 403, Reason: "location ip blacklist"}
			p.writeBlockCustom(w, res)
			emitLog(p, r, w, start, 403, res.Reason, "block", "BYPASS")
			return
		} else if allowed {
			r.Header.Set("X-Tnorv-Loc-Allow", "1") // 通知安全链跳过（白名单）
		}
	}

	// location 级请求限制（覆盖站点级 requestLimitPerMinute）
	if !loc.locAllowRequest(clientIP(r)) {
		w.Header().Set("Retry-After", "60")
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.WriteHeader(429)
		w.Write([]byte(defaultBlockPage(429, "location request limit")))
		emitLog(p, r, w, start, 429, "location request limit", "block", "BYPASS")
		return
	}

	// 0.1 AI 语义防护缓存表检查（O(1)，不阻塞）
	if res := AiGuardCheck(p.srv.Security.AiGuard, r); res != nil {
		p.writeBlockCustom(w, res)
		emitLog(p, r, w, start, res.Status, res.Reason, "block", "BYPASS")
		return
	}

	// 0.25 高级配置：请求限制/访问鉴权/URL 跳转
	if CheckAdvanced(p.adv, w, r) {
		emitLog(p, r, w, start, 200, "advanced", "advanced", "BYPASS")
		return
	}

	// 0.28 重写规则 + 请求方向报头（转发前）
	if len(p.adv.rewrites) > 0 {
		if np := ApplyRewrites(p.adv, r.URL.Path); np != r.URL.Path {
			r.URL.Path = np
		}
	}
	// WebP 自适应：Accept 支持 webp 时优先请求 .webp（404 时回退原路径，改写一次）
	origPath := r.URL.Path
	if wp := WebpRewrite(p.adv, r.Header.Get("Accept"), r.URL.Path); wp != "" {
		r.URL.Path = wp
		r.Header.Set("X-Tnorv-Webp-Fallback", origPath)
	}
	ApplyRequestHeaders(p.adv, r.Header)

	// 0.3 边缘脚本（expr-lang）：请求改写/短路响应
	if RunEdgeScripts(p.scripts, w, r) {
		emitLog(p, r, w, start, 200, "edge-script", "script", "BYPASS")
		return
	}

	// 0.5 5秒盾（JS 挑战）：在安全链之后、缓存之前
	if !CheckJsShield(w, r, &p.srv.Security.JsShield) {
		emitLog(p, r, w, start, 200, "js-shield", "challenge", "BYPASS")
		return
	}

	// location 级防盗链覆盖：命中路由规则且配置了 referers 时替换站点级（借安全链调用前临时改写头不可行，
	// 直接在 Security.Check 前独立检查）
	if locReferers := loc.locReferers(); locReferers != nil && locReferers.Enabled {
		if reason := checkReferer(locReferers, r); reason != "" {
			res := &BlockResult{Status: 403, Reason: "referer(loc): " + reason}
			p.writeBlockCustom(w, res)
			emitLog(p, r, w, start, 403, res.Reason, "block", "BYPASS")
			return
		}
	}

	// 0.9 WebSocket 升级透传（安全链之后）：空闲超时强制断开通用设计
	// 默认 300s（模型注释口径），Advanced.WebsocketTimeoutSeconds>0 时用站点值
	// Advanced.Websocket=false 时拒绝升级（站点显式关闭透传）
	if isWebSocketUpgrade(r) {
		if p.adv != nil && p.adv.cfg != nil && !p.adv.cfg.Websocket {
			http.Error(w, "Websocket 已关闭", http.StatusForbidden)
			emitLog(p, r, w, start, 403, "websocket", "disabled", "BLOCK")
			return
		}
		idle := 300 * time.Second
		if p.adv != nil && p.adv.cfg != nil && p.adv.cfg.WebsocketTimeoutSeconds > 0 {
			idle = time.Duration(p.adv.cfg.WebsocketTimeoutSeconds) * time.Second
		}
		if p.proxy.ServeWebSocket(w, r, idle) {
			emitLog(p, r, w, start, 101, "websocket", "passthrough", "BYPASS")
			return
		}
	}

	// 1. 安全检查链
	if res := p.security.Check(r, p.srv); res != nil {
		switch {
		case res.LogOnly: // log 动作：仅记录不拦截
			emitLog(p, r, w, start, 200, res.Reason, "log", "BYPASS")
		case res.Challenge: // captcha/js_cookie 动作：发挑战页（通过后 cookie 放行）
			ip := clientIP(r)
			token := jsShieldToken(nil, ip, r.UserAgent(), jsCurrentWindow())
			w.Header().Set("Content-Type", "text/html; charset=utf-8")
			w.Header().Set("Cache-Control", "no-store")
			w.Header().Set("Set-Cookie", "tnorv_js="+token+"; Path=/; Max-Age=3600; HttpOnly; SameSite=Lax")
			w.WriteHeader(200)
			w.Write([]byte("<html><head><title>请稍候...</title></head><body><script>setTimeout(function(){location.reload()},800)</script>请稍候，正在验证浏览器...</body></html>"))
			emitLog(p, r, w, start, 200, res.Reason, "challenge", "BYPASS")
			return
		case res.Redirect != "": // redirect 动作
			w.Header().Set("Location", res.Redirect)
			w.WriteHeader(res.Status)
			emitLog(p, r, w, start, res.Status, res.Reason, "redirect", "BYPASS")
		default:
			p.writeBlockCustom(w, res)
			emitLog(p, r, w, start, res.Status, res.Reason, "block", "BYPASS")
		}
		if !res.LogOnly {
			return
		}
		// log-only：放行继续走正常管道
	}

	// 2. 缓存查询（flv 直播流透传：绕过缓存不缓冲）
	// location 级缓存覆盖：命中路由规则且配置了缓存时整组替换站点级
	cacheLayer := p.cache
	if lc := loc.locCache(); lc != nil {
		cacheLayer = lc
	}
	cacheStatus := "BYPASS"
	flvLive := p.adv != nil && p.adv.cfg != nil && p.adv.cfg.Multimedia != nil && p.adv.cfg.Multimedia.FlvLive &&
		strings.HasSuffix(strings.ToLower(r.URL.Path), ".flv")
	canCache := cacheLayer != nil && cacheLayer.Matchable(r) && !flvLive
	if flvLive {
		w.Header().Set("Cache-Control", "no-store")
	}
	if canCache {
		if entry, ok := cacheLayer.Lookup(r); ok {
			hcw := &countWriter{ResponseWriter: w}
			w = hcw
			w.Header().Set("X-Cache", "HIT")
			// ClientTtlSeconds>0：命中响应带浏览器端缓存头（浏览器再缓存一跳）
			if ct := cacheLayer.ClientTtl(r); ct > 0 {
				w.Header().Set("Cache-Control", fmt.Sprintf("public, max-age=%d", ct))
			}
			if serveRange(w, r, entry.Status, entry.ContentType, entry.Body) {
				emitLogBytes(p, r, w, start, 206, "", "", "HIT", hcw.n)
				return
			}
			writeCompressed(w, r, entry.Status, entry.ContentType, entry.Body)
			emitLogBytes(p, r, w, start, entry.Status, "", "", "HIT", hcw.n)
			return
		}
		cacheStatus = "MISS"

		// 边缘缓存共享：L1 未命中先查同组兄弟节点缓存（P2P 拉取，省回源流量）
		if code, ct, body, hit := LookupPeerCache(r); hit && code == 200 {
			w.Header().Set("X-Cache", "PEER-HIT")
			writeCompressed(w, r, code, ct, body)
			emitLogBytes(p, r, w, start, code, "peer-cache", "", "PEER-HIT", int64(len(body)))
			return
		}

		// 回源合并（singleflight）：并发同键未命中只放行一个回源，其余共享结果
		if key := cacheLayer.Key(r); key != "" {
			fcw := &countWriter{ResponseWriter: w}
			p.serveWithFlight(fcw, r, key)
			emitLogBytes(p, r, w, start, 200, "", "", cacheStatus, fcw.n)
			return
		}
	}

	// 3. 反代回源
	// 性能：仅当请求可被缓存时才用 recorder 捕获响应（避免大文件双倍内存拷贝）
	var recorder *responseRecorder
	cw := &countWriter{ResponseWriter: w}
	pw := &pageWriter{ResponseWriter: cw, pages: p.pages}
	var rw http.ResponseWriter = pw
	if canCache {
		w.Header().Set("X-Cache", "MISS") // 必须在 WriteHeader 前设置
		recorder = &responseRecorder{ResponseWriter: pw, status: 200}
		rw = recorder
	} else if compressionAllowed(p, r) && (clientAcceptsGzip(r) || acceptsBrotli(r)) && r.Header.Get("Range") == "" {
		// 不可缓存直通路径：节点压缩兜底（br 优先，源站已压缩时包装器自动跳过）
		enc := "gzip"
		if acceptsBrotli(r) {
			enc = "br"
		}
		gcw := &countWriter{ResponseWriter: w}
		gr := &gzipResponse{ResponseWriter: &pageWriter{ResponseWriter: gcw, pages: p.pages}, encoding: enc}
		defer gr.close()
		p.proxy.ServeHTTP(gr, r)
		emitLogBytes(p, r, w, start, 200, "", "", cacheStatus, gcw.n)
		return
	}
	p.proxy.ServeHTTP(rw, r)

	// 3.5 源站故障 → stale 兜底（stale-if-error：过期缓存救急并标记 X-Cache: STALE）
	// 注意：recorder 已把源站 5xx 响应头写出（pageWriter→countWriter 直通），此处不能再改写状态码；
	// 仅在缓存有可用条目时以 200 重发会触发 superfluous WriteHeader。改为：头未写出时才走 stale 分支
	// —— 通过检查 pageWriter 状态实现（did 存在于 countWriter，recorder 直通意味着头已发出）。
	if recorder != nil && recorder.status >= 500 && !headerSent(rw) {
		if stale, ok := cacheLayer.LookupStale(r); ok {
			w.Header().Set("X-Cache", "STALE")
			w.Header().Set("Content-Type", stale.ContentType)
			w.WriteHeader(200)
			w.Write(stale.Body)
			emitLogBytes(p, r, w, start, 200, "stale-if-error", "", "STALE", int64(len(stale.Body)))
			return
		}
	}

	// 3.8 WebP 404 回退：改写的 .webp 路径源站没有 → 回退原扩展名重发一次
	if recorder != nil && recorder.status == 404 {
		if orig := r.Header.Get("X-Tnorv-Webp-Fallback"); orig != "" {
			r.Header.Del("X-Tnorv-Webp-Fallback")
			r.URL.Path = orig
			recorder2 := &responseRecorder{ResponseWriter: w, status: 200}
			p.proxy.ServeHTTP(recorder2, r)
			if recorder2.status == 200 {
				cacheLayer.Store(r, 200, recorder2.Header().Get("Content-Type"), recorder2.bodySnapshot())
			}
			emitLog(p, r, w, start, recorder2.status, "", "", cacheStatus)
			return
		}
	}

	// 4. 回写缓存（仅 200 且可缓存条件命中）
	if recorder != nil && recorder.status == 200 {
		cacheLayer.Store(r, recorder.status, recorder.Header().Get("Content-Type"), recorder.bodySnapshot())
	}
	emitLogBytes(p, r, w, start, statusOf(rw, recorder), "", "", cacheStatus, cw.n)
}

// compressionAllowed location 级压缩覆盖：compression=false 的路径返回 false
func compressionAllowed(p *Pipeline, r *http.Request) bool {
	if c := matchLocation(p.locs, r.URL.Path).locCompression(); c != nil && !*c {
		return false
	}
	return true
}

func statusOf(w http.ResponseWriter, recorder *responseRecorder) int {
	if recorder != nil {
		return recorder.status
	}
	// 非 recorder 路径无法准确得知状态码；M1 记 0（P1 用 WrappingResponseWriter 普遍捕获）
	return 0
}

func writeBlock(w http.ResponseWriter, res *BlockResult) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.WriteHeader(res.Status)
	w.Write([]byte(defaultBlockPage(res.Status, res.Reason)))
}

// (p) 带自定义页面的封禁响应：WAF/CC 拦截优先使用站点配置的 Blocked HTML
func (p *Pipeline) writeBlockCustom(w http.ResponseWriter, res *BlockResult) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.WriteHeader(res.Status)
	if p.pages != nil && res.Status >= 400 {
		if res.Status == 403 && p.pages.Blocked != "" {
			w.Write([]byte(p.pages.Blocked))
			return
		}
	}
	w.Write([]byte(defaultBlockPage(res.Status, res.Reason)))
}

func defaultBlockPage(status int, reason string) string {
	msg := map[int]string{
		403: "禁止访问 (Forbidden)",
		429: "请求过于频繁，请稍后再试 (Too Many Requests)",
	}[status]
	if msg == "" {
		msg = "请求被拦截"
	}
	return "<html><body><h1>" + itoa(status) + "</h1><p>" + msg + "</p><p>" + reason + "</p></body></html>"
}

func itoa(i int) string {
	if i == 0 {
		return "0"
	}
	digits := ""
	for i > 0 {
		digits = string(rune('0'+i%10)) + digits
		i /= 10
	}
	return digits
}

// ---- 日志上报（有界队列，防止高 QPS 下 goroutine 爆炸）----
// 性能设计：单 worker 消费有界 channel（容量 65536），
// 队列满时直接丢弃（保业务流量优先，通用设计）

var logEmitter func(entry *pb.AccessLog)

var logQueue chan *pb.AccessLog

func SetLogEmitter(fn func(*pb.AccessLog)) {
	logQueue = make(chan *pb.AccessLog, 65536)
	// 单 worker 批量消费：攒批交给 emitter（emitter 侧 EnqueueLog 只做内存 append）
	go func() {
		for entry := range logQueue {
			fn(entry)
		}
	}()
	logEmitter = func(entry *pb.AccessLog) {
		select {
		case logQueue <- entry:
		default: // 队列满：丢弃日志，绝不阻塞请求路径
		}
	}
}

func emitLog(p *Pipeline, r *http.Request, w http.ResponseWriter, start time.Time,
	status int, reason, action, cacheStatus string) {
	emitLogBytes(p, r, w, start, status, reason, action, cacheStatus, 0)
}

func emitLogBytes(p *Pipeline, r *http.Request, w http.ResponseWriter, start time.Time,
	status int, reason, action, cacheStatus string, bytesSent int64) {
	if logEmitter == nil {
		return
	}
	host := r.Host
	if i := strings.Index(host, ":"); i >= 0 {
		host = host[:i]
	}
	entry := &pb.AccessLog{
		ServerId:      p.srv.Id,
		RemoteAddr:    clientIP(r),
		Method:        r.Method,
		Scheme:        schemeOf(r),
		Host:          host,
		Uri:           r.RequestURI,
		Proto:         r.Proto,
		Status:        int32(status),
		BytesSent:     uint64(bytesSent),
		Referer:       r.Referer(),
		UserAgent:     r.UserAgent(),
		RequestTimeMs: float64(time.Since(start).Microseconds()) / 1000.0,
		CacheStatus:   cacheStatus,
		RequestId:     w.Header().Get("X-Request-Id"),
		Timestamp:     time.Now().Unix(),
		WafAction:     action,
		BlockReason:   reason,
	}
	logEmitter(entry) // 有界队列，满则丢弃（不阻塞）
}

func schemeOf(r *http.Request) string {
	if r.TLS != nil {
		return "https"
	}
	return "http"
}

func clientIP(r *http.Request) string {
	// XFF 第一个值（前端有反代时）；无则取直连地址
	if xff := r.Header.Get("X-Forwarded-For"); xff != "" {
		if i := strings.Index(xff, ","); i > 0 {
			return strings.TrimSpace(xff[:i])
		}
		return strings.TrimSpace(xff)
	}
	if ip := r.Header.Get("X-Real-IP"); ip != "" {
		return ip
	}
	host := r.RemoteAddr
	if i := strings.LastIndex(host, ":"); i >= 0 {
		host = host[:i]
	}
	return strings.Trim(host, "[]")
}

// responseRecorder 捕获响应以写缓存
type responseRecorder struct {
	http.ResponseWriter
	status      int
	mut         sync.Mutex
	buf         []byte
	wroteHeader bool
	discard     bool // true = 只捕获不透传（singleflight leader 用，避免响应写两遍）
}

func (rw *responseRecorder) bodySnapshot() []byte {
	rw.mut.Lock()
	defer rw.mut.Unlock()
	out := make([]byte, len(rw.buf))
	copy(out, rw.buf)
	return out
}

func (rw *responseRecorder) WriteHeader(code int) {
	if !rw.wroteHeader {
		rw.status = code
		rw.wroteHeader = true
	}
	if rw.discard {
		return // 只捕获：响应由 doFlight 统一 write() 写出（leader 二次写出会重复响应头/体）
	}
	rw.ResponseWriter.WriteHeader(code)
}

func (rw *responseRecorder) Write(b []byte) (int, error) {
	rw.mut.Lock()
	rw.buf = append(rw.buf, b...)
	rw.mut.Unlock()
	if rw.discard {
		return len(b), nil
	}
	return rw.ResponseWriter.Write(b)
}

// pageWriter 自定义错误页拦截器：源站 404/5xx 时用站点配置的自定义页面替换响应体。
// 在 WriteHeader 拦截（头未发出），替换后丢弃源站错误页体；正常响应零开销透传。
type pageWriter struct {
	http.ResponseWriter
	pages       *pb.CustomPagesConfig
	status      int
	replaced    bool
	headerSent  bool // WriteHeader 已调用过（含替换路径），上层不得再改写状态
}

func (pw *pageWriter) WriteHeader(code int) {
	if pw.replaced {
		return
	}
	pw.status = code
	pw.headerSent = true
	page := ""
	if code == 404 && pw.pages != nil && pw.pages.NotFound != "" {
		page = pw.pages.NotFound
	} else if code >= 500 && pw.pages != nil && pw.pages.ServerError != "" {
		page = pw.pages.ServerError
	}
	if page == "" {
		pw.ResponseWriter.WriteHeader(code)
		return
	}
	pw.replaced = true
	h := pw.ResponseWriter.Header()
	h.Set("Content-Type", "text/html; charset=utf-8")
	h.Del("Content-Length")
	h.Del("Transfer-Encoding")
	h.Del("Content-Encoding")
	pw.ResponseWriter.WriteHeader(code)
	_, _ = pw.ResponseWriter.Write([]byte(page))
}

func (pw *pageWriter) Write(b []byte) (int, error) {
	if pw.replaced {
		return len(b), nil // 已替换：丢弃源站错误页体
	}
	if pw.status == 0 {
		pw.WriteHeader(200)
	}
	return pw.ResponseWriter.Write(b)
}

// headerSent 判断响应头是否已写出（pageWriter 包装链），用于 stale 兜底分支防止二次 WriteHeader。
func headerSent(w http.ResponseWriter) bool {
	if pw, ok := w.(*pageWriter); ok {
		return pw.headerSent
	}
	return false
}

func (pw *pageWriter) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	if hj, ok := pw.ResponseWriter.(http.Hijacker); ok {
		return hj.Hijack()
	}
	return nil, nil, http.ErrNotSupported
}

// countWriter 统计响应字节数（BytesSent 日志/计费用），透传 Flusher/Hijacker
type countWriter struct {	http.ResponseWriter
	n   int64
	did bool
}

func (c *countWriter) WriteHeader(code int) {
	if !c.did {
		c.did = true
	}
	c.ResponseWriter.WriteHeader(code)
}

func (c *countWriter) Write(b []byte) (int, error) {
	if !c.did {
		c.WriteHeader(200)
	}
	n, err := c.ResponseWriter.Write(b)
	c.n += int64(n)
	return n, err
}

func (c *countWriter) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	if hj, ok := c.ResponseWriter.(http.Hijacker); ok {
		return hj.Hijack()
	}
	return nil, nil, http.ErrNotSupported
}

// Hijack 透传（防御性：缓存路径不会出现升级请求）
func (rw *responseRecorder) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	if hj, ok := rw.ResponseWriter.(http.Hijacker); ok {
		return hj.Hijack()
	}
	return nil, nil, http.ErrNotSupported
}
