package pipeline

// compress.go 响应压缩通用设计。
// 性能策略：
//   - 仅压缩可缓存命中路径与回源响应中的文本类 Content-Type
//   - 回源时透传客户端 Accept-Encoding（源站支持则用源站的压缩，节点零开销）
//   - 仅当源站未压缩（identity）且客户端支持时，节点做 gzip（level 1 速度优先）
//   - <256B 或已是压缩类型（图片/视频/woff2 等）跳过

import (
	"bytes"
	"compress/gzip"
	"io"
	"net/http"
	"strings"
	"sync"

	"github.com/andybalholm/brotli"
)

var gzipWriters = sync.Pool{New: func() any { return gzip.NewWriter(io.Discard) }}

// compressible 判断 Content-Type 是否值得压缩
func compressible(ct string) bool {
	if ct == "" {
		return false
	}
	ct = strings.ToLower(strings.TrimSpace(strings.Split(ct, ";")[0]))
	if strings.Contains(ct, "gzip") || strings.Contains(ct, "br") || strings.Contains(ct, "zip") {
		return false // 已压缩
	}
	for _, p := range []string{"text/", "application/json", "application/javascript", "application/xml", "image/svg+xml", "application/wasm"} {
		if strings.HasPrefix(ct, p) {
			return true
		}
	}
	return false
}

// clientAcceptsGzip 客户端是否接受 gzip
func clientAcceptsGzip(r *http.Request) bool {
	return strings.Contains(r.Header.Get("Accept-Encoding"), "gzip")
}

// acceptsBrotli 客户端是否接受 br（优先于 gzip：压缩率更高）
func acceptsBrotli(r *http.Request) bool {
	return strings.Contains(r.Header.Get("Accept-Encoding"), "br")
}

// gzipResponse 包装 ResponseWriter：写入时压缩（仅在首字节后按 Content-Type 判定启用）
type gzipResponse struct {
	http.ResponseWriter
	gz          *gzip.Writer
	br          *brotli.Writer
	encoding    string // gzip / br
	enabled     bool
	decided     bool
	wroteHeader bool
}

func (g *gzipResponse) WriteHeader(code int) {
	if !g.wroteHeader {
		g.wroteHeader = true
		enc := g.Header().Get("Content-Encoding")
		ct := g.Header().Get("Content-Type")
		// 源站未压缩（或 identity）且类型可压时，节点压缩兜底
		if (enc == "" || enc == "identity") && compressible(ct) {
			g.enabled = true
			g.Header().Del("Content-Length")
			if g.encoding == "" {
				g.encoding = "gzip"
			}
			g.Header().Set("Content-Encoding", g.encoding)
			g.Header().Add("Vary", "Accept-Encoding")
		}
	}
	g.ResponseWriter.WriteHeader(code)
}

func (g *gzipResponse) Write(b []byte) (int, error) {
	if !g.wroteHeader {
		g.WriteHeader(200)
	}
	// 小响应不压缩（压缩后可能反而变大）
	if !g.enabled || len(b) < 256 && !g.decided {
		g.decided = true
		if !g.enabled {
			return g.ResponseWriter.Write(b)
		}
	}
	if g.gz == nil && g.br == nil {
		if g.encoding == "br" {
			g.br = brotli.NewWriter(g.ResponseWriter)
		} else {
			g.gz = gzipWriters.Get().(*gzip.Writer)
			g.gz.Reset(g.ResponseWriter)
		}
	}
	if g.br != nil {
		return g.br.Write(b)
	}
	return g.gz.Write(b)
}

func (g *gzipResponse) close() {
	if g.gz != nil {
		g.gz.Close()
		gzipWriters.Put(g.gz)
		g.gz = nil
	}
	if g.br != nil {
		g.br.Close()
		g.br = nil
	}
}

// maybeCompress 若客户端接受 gzip 且源站未压缩，用压缩包装器处理请求。
// 返回 true 表示已接管（调用方无需再处理）。
func maybeCompress(w http.ResponseWriter, r *http.Request, next http.Handler) bool {
	// 回源场景：优先透传 Accept-Encoding 让源站压缩（CPU 零开销）。
	// 源站一般会按 AE 压缩；仅当源站返回 identity 时由节点压缩兜底，
	// 兜底判定在 gzipResponse.WriteHeader 内按响应 Content-Encoding 处理：
	// 此处仅当请求不带 Range（Range+gzip 不兼容）时启用包装器。
	if !clientAcceptsGzip(r) || r.Header.Get("Range") != "" {
		next.ServeHTTP(w, r)
		return false
	}
	// 记录源站是否已压缩：代理后如果 Content-Encoding 已设置则不该再压。
	enc := "gzip"
	if acceptsBrotli(r) {
		enc = "br"
	}
	gr := &gzipResponse{ResponseWriter: w, encoding: enc}
	// WriteHeader 逻辑内联在方法里检查 Content-Encoding（见下方重写）
	defer gr.close()
	next.ServeHTTP(gr, r)
	return true
}

// writeCompressed 统一响应写出：按客户端 Accept-Encoding 协商 br/gzip/identity。
// 用于缓存 HIT / PEER-HIT / singleflight 共享体（缓存中存的是未压缩原文）。
func writeCompressed(w http.ResponseWriter, r *http.Request, status int, contentType string, body []byte) {
	w.Header().Set("Content-Type", contentType)
	if len(body) >= 256 && compressible(contentType) && r.Header.Get("Range") == "" {
		var buf bytes.Buffer
		if acceptsBrotli(r) {
			bw := brotli.NewWriterLevel(&buf, brotli.BestSpeed)
			bw.Write(body)
			bw.Close()
			w.Header().Set("Content-Encoding", "br")
		} else if clientAcceptsGzip(r) {
			gz := gzip.NewWriter(&buf)
			gz.Write(body)
			gz.Close()
			w.Header().Set("Content-Encoding", "gzip")
		}
		if buf.Len() > 0 && buf.Len() < len(body) {
			w.Header().Add("Vary", "Accept-Encoding")
			w.Header().Del("Content-Length")
			w.WriteHeader(status)
			w.Write(buf.Bytes())
			return
		}
	}
	w.WriteHeader(status)
	w.Write(body)
}
