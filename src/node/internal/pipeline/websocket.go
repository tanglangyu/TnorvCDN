package pipeline

// websocket.go WebSocket 升级请求的透传 + 空闲超时强制断开：
// ReverseProxy 自动处理 101 升级但无法设置空闲超时；这里手工完成：
//   RoundTrip(101) → hijack 客户端连接 → 双向 io.Copy + 每次读前刷新读截止时间
// 超时窗口内无任何数据流动即断开通用设计。

import (
	"bufio"
	"io"
	"log"
	"net"
	"net/http"
	"strings"
	"time"
)

// isWebSocketUpgrade 判断请求是否为 WebSocket 升级
func isWebSocketUpgrade(r *http.Request) bool {
	return strings.EqualFold(r.Header.Get("Upgrade"), "websocket") &&
		strings.Contains(strings.ToLower(r.Header.Get("Connection")), "upgrade")
}

// ServeWebSocket 透传升级请求；返回 false 表示非升级请求或后端拒绝升级（调用方回退常规代理）。
// idle<=0 表示不设空闲超时（长连接保持）。
func (p *ProxyLayer) ServeWebSocket(w http.ResponseWriter, r *http.Request, idle time.Duration) bool {
	if !isWebSocketUpgrade(r) {
		return false
	}
	// 后端地址与 Host 策略与常规代理一致
	origin, prule := p.pick(r)
	if origin == nil {
		http.Error(w, "502 Bad Gateway: no healthy upstream", http.StatusBadGateway)
		return true
	}
	targetScheme, targetHostPort := originSchemeHost(origin)
	upgradeReq := r.Clone(r.Context())
	upgradeReq.URL.Scheme = targetScheme
	upgradeReq.URL.Host = targetHostPort
	upgradeReq.Host = hostFor(origin, prule, p.cfg, r)
	upgradeReq.Header.Set("X-Real-IP", clientIP(r))
	if xff := r.Header.Get("X-Forwarded-For"); xff != "" {
		upgradeReq.Header.Set("X-Forwarded-For", xff+", "+clientIP(r))
	} else {
		upgradeReq.Header.Set("X-Forwarded-For", clientIP(r))
	}

	resp, err := sharedTransport.RoundTrip(upgradeReq)
	if err != nil {
		globalHealth.Store(originKey(origin), false)
		http.Error(w, "502 Bad Gateway: websocket upstream unreachable", http.StatusBadGateway)
		return true
	}
	if resp.StatusCode != http.StatusSwitchingProtocols {
		// 后端拒绝升级：把响应原样回给客户端（一般是非 101 错误页）
		copyHeader(w.Header(), resp.Header)
		w.WriteHeader(resp.StatusCode)
		if resp.Body != nil {
			io.Copy(w, resp.Body)
			resp.Body.Close()
		}
		return true
	}

	// 拿到后端活连接（101 时 Transport 返回可读写体）
	backend, ok := resp.Body.(io.ReadWriteCloser)
	if !ok {
		http.Error(w, "502 Bad Gateway: websocket hijack unavailable", http.StatusBadGateway)
		return true
	}
	globalHealth.Store(originKey(origin), true)

	// 劫持客户端连接
	hj, ok := w.(http.Hijacker)
	if !ok {
		log.Printf("[ws] hijack 不可用: w 类型 %T", w)
		http.Error(w, "502 Bad Gateway: websocket not supported", http.StatusBadGateway)
		backend.Close()
		resp.Body.Close()
		return true
	}
	clientConn, clientBuf, err := hj.Hijack()
	if err != nil {
		http.Error(w, "502 Bad Gateway: websocket hijack failed", http.StatusBadGateway)
		backend.Close()
		resp.Body.Close()
		return true
	}

	// 把 101 响应头原样写给客户端
	clientConn.Write([]byte("HTTP/1.1 101 Switching Protocols\r\n"))
	for k, vv := range resp.Header {
		for _, v := range vv {
			clientConn.Write([]byte(k + ": " + v + "\r\n"))
		}
	}
	clientConn.Write([]byte("\r\n"))
	if clientBuf != nil {
		clientBuf.Flush()
	}

	if idle > 0 {
		log.Printf("[ws] 升级透传 idle=%s host=%s origin=%s", idle, r.Host, originKey(origin))
	}

	// 双向搬运：每次读前刷新读截止（空闲超时语义）。
	// 后端连接来自 Transport 101 响应（io.ReadWriteCloser，通常底层即 net.Conn），无法设置 Deadline
	// 时退化为纯透传（后端方向无空闲超时，客户端方向仍生效）。
	go pumpBackend(backend, clientConn, idle)
	pumpClient(clientConn, clientBuf.Reader, backend, idle)
	return true
}

// pumpClient 客户端 → 后端：客户端是真 net.Conn，可设空闲截止
func pumpClient(src net.Conn, srcBuf *bufio.Reader, dst io.ReadWriteCloser, idle time.Duration) {
	defer func() { _ = src.Close(); _ = dst.Close() }()
	buf := make([]byte, 32*1024)
	for {
		if idle > 0 {
			_ = src.SetReadDeadline(time.Now().Add(idle))
		}
		n, err := srcBuf.Read(buf)
		if err != nil {
			log.Printf("[ws] pumpClient 结束: err=%v idle=%s", err, idle)
			// 空闲超时：先对后端发 WS CLOSE(1000)，再立即断客户端（ Close 已在 defer，这里提前显式执行）
			if idle > 0 && isNetTimeout(err) {
				if w, ok := dst.(interface{ SetWriteDeadline(time.Time) error }); ok {
					_ = w.SetWriteDeadline(time.Now().Add(2 * time.Second))
				}
				_, _ = dst.Write([]byte{0x88, 0x02, 0x03, 0xe8})
			}
			if cerr := src.Close(); cerr != nil {
				log.Printf("[ws] pumpClient src.Close err=%v", cerr)
			}
			return
		}
		if w, ok := dst.(interface{ SetWriteDeadline(time.Time) error }); ok {
			_ = w.SetWriteDeadline(time.Now().Add(30 * time.Second))
		}
		if _, err := dst.Write(buf[:n]); err != nil {
			return
		}
	}
}

// pumpBackend 后端 → 客户端
func pumpBackend(src io.ReadWriteCloser, dst net.Conn, idle time.Duration) {
	defer func() { _ = src.Close(); _ = dst.Close() }()
	buf := make([]byte, 32*1024)
	for {
		if idle > 0 {
			if c, ok := src.(interface{ SetReadDeadline(time.Time) error }); ok {
				_ = c.SetReadDeadline(time.Now().Add(idle))
			}
		}
		n, err := src.Read(buf)
		if err != nil {
			return
		}
		_ = dst.SetWriteDeadline(time.Now().Add(30 * time.Second))
		if _, err := dst.Write(buf[:n]); err != nil {
			return
		}
	}
}

func isNetTimeout(err error) bool {
	if ne, ok := err.(net.Error); ok {
		return ne.Timeout()
	}
	return false
}
