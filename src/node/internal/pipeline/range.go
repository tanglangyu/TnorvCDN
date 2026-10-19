package pipeline

// range.go Range 分片响应：整对象缓存 + 按 Range 头裁剪 206 分片。
// 设计：缓存条目始终存完整对象（分片缓存碎片化严重，收益低）；
// 命中后按 Range 解析并回 206/Content-Range，支持断点续传（多段用第一个范围，
// 与 nginx strip_range 兼容；不合法/超界回 416 或整对象）。

import (
	"fmt"
	"net/http"
	"strconv"
	"strings"
)

// serveRange 尝试按 Range 头服务缓存条目。返回 false 表示按整对象处理。
func serveRange(w http.ResponseWriter, r *http.Request, status int, contentType string, body []byte) bool {
	spec := r.Header.Get("Range")
	if spec == "" || status != 200 || len(body) == 0 {
		return false
	}
	start, end, ok := parseRange(spec, int64(len(body)))
	if !ok {
		w.Header().Set("Content-Range", fmt.Sprintf("bytes */%d", len(body)))
		w.WriteHeader(http.StatusRequestedRangeNotSatisfiable)
		return true
	}
	part := body[start : end+1]
	w.Header().Set("Content-Range", fmt.Sprintf("bytes %d-%d/%d", start, end, len(body)))
	w.Header().Set("Accept-Ranges", "bytes")
	w.Header().Set("Content-Length", strconv.Itoa(len(part)))
	w.WriteHeader(http.StatusPartialContent)
	w.Write(part)
	return true
}

// parseRange 解析 "bytes=a-b"（单段；suffix "bytes=-n" 也支持）
func parseRange(spec string, size int64) (start, end int64, ok bool) {
	spec = strings.TrimSpace(spec)
	if !strings.HasPrefix(spec, "bytes=") {
		return 0, 0, false
	}
	spec = strings.TrimPrefix(spec, "bytes=")
	if i := strings.Index(spec, ","); i >= 0 {
		spec = spec[:i] // 多段只取第一个范围
	}
	spec = strings.TrimSpace(spec)
	if spec == "" {
		return 0, 0, false
	}
	if strings.HasPrefix(spec, "-") { // suffix range：最后 n 字节
		n, err := strconv.ParseInt(spec[1:], 10, 64)
		if err != nil || n <= 0 {
			return 0, 0, false
		}
		if n > size {
			n = size
		}
		return size - n, size - 1, true
	}
	parts := strings.SplitN(spec, "-", 2)
	s, err1 := strconv.ParseInt(parts[0], 10, 64)
	var e int64
	var err2 error
	if len(parts) == 2 && parts[1] != "" {
		e, err2 = strconv.ParseInt(parts[1], 10, 64)
	} else {
		e, err2 = size-1, nil
	}
	if err1 != nil || err2 != nil || s < 0 || s >= size || e < s {
		return 0, 0, false
	}
	if e >= size {
		e = size - 1
	}
	return s, e, true
}
