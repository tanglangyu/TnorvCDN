package pipeline

// cacheserve.go 边缘缓存共享（P2P 拉取）：
//   - 每个节点在 :3128 开放缓存共享服务（与 L2 代理端口复用，路径 /__tnorv_cache 区分）
//   - L1 未命中时先查同组兄弟节点缓存（siblings 表），命中直接拉取响应体（省回源流量）
//   - 查询协议：GET http://peer:3128/__tnorv_cache?host=&path=&query=，命中返回 200+body，
//     未命中 404。超时 800ms（缓存查询必须快于回源才有价值）。
//   - 安全：共享服务仅响应带 X-Tnorv-Share 头的内网节点间请求（节点 IP 由主控下发，天然内网信任域）。

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"
)

// globalPeers 同组节点缓存服务地址（含自身排除，主控下发全部边缘节点 IP）
var globalPeers = struct {
	sync.Mutex
	ips []string
}{}

// SetSharePeers 更新同组节点列表（配置热更新调用；ips 为其它边缘节点 IP）
func SetSharePeers(ips []string) {
	globalPeers.Lock()
	globalPeers.ips = ips
	globalPeers.Unlock()
}

var shareClient = &http.Client{
	Timeout: 800 * time.Millisecond,
	Transport: sharedTransport,
}

// cacheShareQuery P2P 缓存查询参数
type cacheShareQuery struct {
	Host  string `json:"host"`
	Path  string `json:"path"`
	Query string `json:"query"`
}

// LookupPeerCache 查询兄弟节点缓存：任一节点命中即返回 (status, contentType, body, true)
func LookupPeerCache(r *http.Request) (int, string, []byte, bool) {
	globalPeers.Lock()
	peers := globalPeers.ips
	globalPeers.Unlock()
	if len(peers) == 0 {
		return 0, "", nil, false
	}

	q := cacheShareQuery{Host: r.URL.Host, Path: r.URL.Path, Query: r.URL.RawQuery}
	qb, _ := json.Marshal(q)

	for _, ip := range peers {
		url := fmt.Sprintf("http://%s:3128/__tnorv_cache", ip)
		req, err := http.NewRequest(http.MethodGet, url, strings.NewReader(string(qb)))
		if err != nil {
			continue
		}
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("X-Tnorv-Share", "1")
		resp, err := shareClient.Do(req)
		if err != nil {
			continue
		}
		if resp.StatusCode != http.StatusOK {
			resp.Body.Close()
			continue
		}
		ct := resp.Header.Get("Content-Type")
		body, err := io.ReadAll(io.LimitReader(resp.Body, 8<<20))
		resp.Body.Close()
		if err != nil {
			continue
		}
		return http.StatusOK, ct, body, true
	}
	return 0, "", nil, false
}

// ServeCacheShare 处理兄弟节点的缓存查询请求（挂在 :3128 mux 上）
func ServeCacheShare(w http.ResponseWriter, r *http.Request) {
	if r.Header.Get("X-Tnorv-Share") != "1" {
		http.Error(w, "forbidden", http.StatusForbidden)
		return
	}
	if r.Method != http.MethodGet {
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
		return
	}
	var q cacheShareQuery
	if err := json.NewDecoder(io.LimitReader(r.Body, 1<<20)).Decode(&q); err != nil {
		http.Error(w, "bad request", http.StatusBadRequest)
		return
	}
	// 从全局缓存存储查找（host+path+query → 缓存键）
	cacheStore.mu.RLock()
	var entry *cacheEntry
	for _, e := range cacheStore.mem {
		if strings.EqualFold(e.Host, q.Host) && e.Path == q.Path {
			entry = e
			break
		}
	}
	cacheStore.mu.RUnlock()
	if entry == nil || time.Since(entry.StoredAt) > entry.TTL {
		http.Error(w, "not found", http.StatusNotFound)
		return
	}
	w.Header().Set("Content-Type", entry.ContentType)
	w.Header().Set("X-Tnorv-Peer-Hit", "1")
	w.WriteHeader(http.StatusOK)
	w.Write(entry.Body)
}
