package pipeline

// urlauth.go URL 防盗链鉴权：A-D 四种时间戳签名方案通用设计。
//
//	A 方案：http://URL?auth_key=timestamp-rand-0-md5(uri-timestamp-rand-0-key)
//	B 方案：http://URL?auth_key=timestamp-hex(md5(key+timestamp+uri))
//	C 方案：http://URL?auth_key=md5(key+uri+timestamp)（时间戳为 hex 时间）
//	D 方案：http://URL?auth_key=md5(uri-timestamp-key)-timestamp
//
// 通过条件：auth_key 存在、签名匹配、timestamp 未过期（容许 ±ExpirySeconds 窗口）。
// 失败：403 Forbidden。

import (
	"crypto/md5"
	"crypto/subtle"
	"fmt"
	"net/http"
	"strconv"
	"strings"
	"time"
)

// checkUrlAuth 校验 URL 签名；返回 "" 表示通过，否则返回失败原因。
func checkUrlAuth(cfg *urlAuthView, r *http.Request) string {
	if cfg == nil || !cfg.Enabled || cfg.Key == "" {
		return ""
	}
	authKey := r.URL.Query().Get("auth_key")
	if authKey == "" {
		return "missing auth_key"
	}

	now := time.Now().Unix()
	uri := r.URL.Path // 鉴权 uri 不含 query通用设计

	switch cfg.Type {
	case "a":
		// auth_key = timestamp-rand-0-md5(uri-timestamp-rand-0-key)
		parts := strings.SplitN(authKey, "-", 4)
		if len(parts) != 4 {
			return "bad auth_key format"
		}
		ts, _ := strconv.ParseInt(parts[0], 10, 64)
		if abs64(now-ts) > int64(cfg.ExpirySeconds) {
			return "auth_key expired"
		}
		expect := md5hex(fmt.Sprintf("%s-%s-%s-%s", uri, parts[0], parts[1], cfg.Key))
		if subtle.ConstantTimeCompare([]byte(expect), []byte(parts[3])) != 1 {
			return "signature mismatch"
		}
	case "b":
		// auth_key = timestamp-hex(md5(key+timestamp+uri))，timestamp 为 hex Unix 时间
		parts := strings.SplitN(authKey, "-", 2)
		if len(parts) != 2 {
			return "bad auth_key format"
		}
		ts, err := strconv.ParseInt(parts[0], 16, 64)
		if err != nil {
			return "bad timestamp"
		}
		if abs64(now-ts) > int64(cfg.ExpirySeconds) {
			return "auth_key expired"
		}
		expect := md5hex(cfg.Key + parts[0] + uri)
		if subtle.ConstantTimeCompare([]byte(expect), []byte(strings.ToLower(parts[1]))) != 1 {
			return "signature mismatch"
		}
	case "c":
		// auth_key = md5(key+uri+hex_timestamp)
		if len(authKey) < 8 {
			return "bad auth_key format"
		}
		hexTs := authKey[len(authKey)-8:]
		sig := authKey[:len(authKey)-8]
		ts, err := strconv.ParseInt(hexTs, 16, 64)
		if err != nil {
			return "bad timestamp"
		}
		if abs64(now-ts) > int64(cfg.ExpirySeconds) {
			return "auth_key expired"
		}
		expect := md5hex(cfg.Key + uri + hexTs)
		if subtle.ConstantTimeCompare([]byte(expect), []byte(strings.ToLower(sig))) != 1 {
			return "signature mismatch"
		}
	case "d":
		// auth_key = md5(uri-timestamp-key)-timestamp
		parts := strings.SplitN(authKey, "-", 2)
		if len(parts) != 2 {
			return "bad auth_key format"
		}
		// 注意：md5 是 32 位 hex（不含 -），第二段为明文 timestamp
		idx := strings.LastIndex(authKey, "-")
		if idx < 0 {
			return "bad auth_key format"
		}
		sig := authKey[:idx]
		tsStr := authKey[idx+1:]
		ts, err := strconv.ParseInt(tsStr, 10, 64)
		if err != nil {
			return "bad timestamp"
		}
		if abs64(now-ts) > int64(cfg.ExpirySeconds) {
			return "auth_key expired"
		}
		expect := md5hex(fmt.Sprintf("%s-%s-%s", uri, tsStr, cfg.Key))
		if subtle.ConstantTimeCompare([]byte(expect), []byte(sig)) != 1 {
			return "signature mismatch"
		}
	default:
		return "unknown auth type"
	}
	return ""
}

func md5hex(s string) string {
	sum := md5.Sum([]byte(s))
	return fmt.Sprintf("%x", sum)
}

func abs64(v int64) int64 {
	if v < 0 {
		return -v
	}
	return v
}
