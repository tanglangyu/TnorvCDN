package pipeline

import (
	"encoding/json"
	"crypto/sha1"
	"encoding/hex"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"tnorv/node/pb"
)

// 全局缓存存储：跨配置热更新保留（Pipeline 每 10s 重建，缓存内容不能丢）。
// 单例结构，带 FIFO 淘汰与内存上限。
type globalCache struct {
	mu    sync.RWMutex
	mem   map[string]*cacheEntry
	bytes int64
	max   int64 // 内存上限 bytes（默认 128MB）
}

var cacheStore = &globalCache{
	mem: make(map[string]*cacheEntry),
	max: 128 << 20,
}

type cacheEntry struct {
	Status      int
	ContentType string
	Body        []byte
	StoredAt    time.Time
	TTL         time.Duration
	Host        string // 刷新定位用（小写）
	Path        string
}

// CacheLayer 每个网站管道的缓存规则视图（轻量，可随配置重建）。
// 实际数据存全局 cacheStore；文件二级缓存 P1 接入（dir 预留）。
type CacheLayer struct {
	enabled bool
	rules   []*pb.CacheRule
	dir     string
}

func NewCacheLayer(cfg *pb.CacheConfig, dir string) *CacheLayer {
	if cfg == nil || !cfg.Enabled || len(cfg.Rules) == 0 {
		return nil
	}
	if dir == "" {
		dir = "/opt/tnorv/cache"
	}
	os.MkdirAll(dir, 0o755)
	layer := &CacheLayer{
		enabled: true,
		rules:   cfg.Rules,
		dir:     dir,
	}
	diskSink = layer.diskStore
	return layer
}

// Matchable 快速判断请求是否可能匹配缓存规则（不查缓存，仅规则匹配）。
// 用于决定是否启用响应捕获（性能：不可缓存请求零拷贝直通）。
func (c *CacheLayer) Matchable(r *http.Request) bool {
	if !c.enabled || (r.Method != http.MethodGet && r.Method != http.MethodHead) {
		return false
	}
	return c.matchRule(r) != nil
}

// ClientTtl 返回命中的缓存规则的浏览器端 TTL（秒，0=不设置客户端缓存头）。
// ClientTtlSeconds>0 时缓存命中响应带 Cache-Control: max-age=<clientTtl>，
// 让浏览器端也缓存一跳，进一步减少回节点流量。
func (c *CacheLayer) ClientTtl(r *http.Request) int {
	if !c.enabled {
		return 0
	}
	rule := c.matchRule(r)
	if rule == nil {
		return 0
	}
	return rule.ClientTtlSeconds
}

// Lookup 缓存查询：先判断规则是否适用，再查全局存储。
// 过期条目保留在内存（不删除），供源站故障时 stale 兜底（stale-if-error 语义）。
// Range 请求命中缓存：整对象缓存 + 206 分片响应（Content-Range 裁剪），支持断点续传。
func (c *CacheLayer) Lookup(r *http.Request) (*cacheEntry, bool) {
	if !c.enabled || r.Method != http.MethodGet && r.Method != http.MethodHead {
		return nil, false
	}
	rule := c.matchRule(r)
	if rule == nil {
		return nil, false
	}

	key := c.key(r, rule)
	cacheStore.mu.RLock()
	entry, ok := cacheStore.mem[key]
	cacheStore.mu.RUnlock()
	if !ok {
		// 文件二级缓存：内存 miss 后回查磁盘
		if de, dok := c.diskLookup(key); dok {
			return de, true
		}
		return nil, false
	}
	if time.Since(entry.StoredAt) > entry.TTL {
		return entry, false // 过期：返回条目但标记未命中（供 stale 兜底）
	}
	return entry, true
}

// LookupStale 返回过期缓存条目（stale-if-error 兜底：源站 5xx 时救急）
func (c *CacheLayer) LookupStale(r *http.Request) (*cacheEntry, bool) {
	if !c.enabled {
		return nil, false
	}
	rule := c.matchRule(r)
	if rule == nil {
		return nil, false
	}
	key := c.key(r, rule)
	cacheStore.mu.RLock()
	entry, ok := cacheStore.mem[key]
	cacheStore.mu.RUnlock()
	return entry, ok // 不检查 TTL：专门要过期内容
}

// Store 写缓存
func (c *CacheLayer) Store(r *http.Request, status int, contentType string, body []byte) {
	if !c.enabled || status != 200 || len(body) == 0 {
		return
	}
	rule := c.matchRule(r)
	if rule == nil {
		return
	}
	entry := &cacheEntry{
		Status:      status,
		ContentType: contentType,
		Body:        body,
		StoredAt:    time.Now(),
		TTL:         time.Duration(rule.TtlSeconds) * time.Second,
		Host:        strings.ToLower(r.URL.Host),
		Path:        r.URL.Path,
	}
	if entry.TTL <= 0 {
		entry.TTL = time.Hour
	}

	key := c.key(r, rule)
	// 大对象（>8MB）直接落盘（文件二级缓存），不占内存
	if len(body) > 8<<20 {
		go c.diskStore(key, entry)
		return
	}
	cacheStore.mu.Lock()
	defer cacheStore.mu.Unlock()
	if _, exists := cacheStore.mem[key]; !exists {
		cacheStore.bytes += int64(len(body))
	}
	cacheStore.mem[key] = entry
	cacheStore.evictLocked()
}

// ---- 文件二级缓存 ----
// 内存放不下或被淘汰的大对象落盘（c.dir），Lookup 内存 miss 后回查磁盘。
// 盘上文件名 = 缓存 key，内容为 cacheEntry 的 JSON（body base64）；TTL 过期同样生效。

// diskStore 大对象落盘（key 已知；异步不阻塞请求路径）
func (c *CacheLayer) diskStore(key string, e *cacheEntry) {
	if c.dir == "" {
		return
	}
	data, err := json.Marshal(e)
	if err != nil {
		return
	}
	path := filepath.Join(c.dir, key+".entry")
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil {
		return
	}
	os.Rename(tmp, path) // 原子替换
}

// diskLookup 磁盘回查（内存 miss 后调用）
func (c *CacheLayer) diskLookup(key string) (*cacheEntry, bool) {
	if c.dir == "" {
		return nil, false
	}
	data, err := os.ReadFile(filepath.Join(c.dir, key+".entry"))
	if err != nil {
		return nil, false
	}
	var e cacheEntry
	if err := json.Unmarshal(data, &e); err != nil {
		os.Remove(filepath.Join(c.dir, key+".entry"))
		return nil, false
	}
	if time.Since(e.StoredAt) > e.TTL {
		return nil, false // 过期：磁盘条目直接删
	}
	return &e, true
}

func (c *globalCache) evictLocked() {
	// FIFO 淘汰：超限时删除最早写入的条目（有 dir 时落盘二级缓存）
	for c.bytes > c.max {
		var oldestKey string
		var oldest time.Time
		for k, e := range c.mem {
			if oldestKey == "" || e.StoredAt.Before(oldest) {
				oldestKey, oldest = k, e.StoredAt
			}
		}
		if oldestKey == "" {
			break
		}
		e := c.mem[oldestKey]
		c.bytes -= int64(len(e.Body))
		delete(c.mem, oldestKey)
		if diskSink != nil {
			go diskSink(oldestKey, e) // 异步落盘，不锁内存表
		}
	}
}

// diskSink 由 CacheLayer 注入（包级回调，避免 globalCache 反向依赖 dir）
var diskSink func(key string, e *cacheEntry)

// matchRule 依据规则类型匹配（extension / path-prefix / homepage / all）
func (c *CacheLayer) matchRule(r *http.Request) *pb.CacheRule {
	path := r.URL.Path
	for _, rule := range c.rules {
		switch rule.MatchType {
		case "all":
			return rule
		case "homepage":
			if path == "/" || path == "/index.html" {
				return rule
			}
		case "extension":
			ext := strings.TrimPrefix(strings.ToLower(filepath.Ext(path)), ".")
			for _, e := range strings.Split(rule.Value, ",") {
				if strings.TrimSpace(e) == ext {
					return rule
				}
			}
		case "path-prefix":
			if strings.HasPrefix(path, rule.Value) {
				return rule
			}
		case "path-exact":
			if path == rule.Value {
				return rule
			}
		}
	}
	return nil
}

// key 缓存键：host+path(+query)
func (c *CacheLayer) key(r *http.Request, rule *pb.CacheRule) string {
	h := sha1.New()
	h.Write([]byte(r.URL.Host))
	h.Write([]byte(r.URL.Path))
	if !rule.IgnoreQueryString {
		h.Write([]byte(r.URL.RawQuery))
	}
	return hex.EncodeToString(h.Sum(nil))
}

// ---- 缓存刷新通用设计----
// cacheEntry 记录 Host/Path 元数据，刷新时线性扫描（缓存条目量级 ≤ 数千，可接受）。

type purgeMatch struct {
	host string // 精确 host（"" = 全部）
	path string // 精确路径或前缀（"" = 全部）
	dir  bool   // path 为前缀匹配
}

// PurgeCache 按条件清除全局缓存，返回清除条数。
// matchType: url（host+path 精确）/ directory（host+path 前缀）/ host / all
func PurgeCache(matchType, host, value string) int {
	m := purgeMatch{}
	switch matchType {
	case "url":
		m.host, m.path = strings.ToLower(host), value
	case "directory":
		m.host, m.path, m.dir = strings.ToLower(host), value, true
	case "host":
		m.host = strings.ToLower(host)
	case "all":
	}
	n := 0
	cacheStore.mu.Lock()
	for k, e := range cacheStore.mem {
		if purgeHit(e, m) {
			cacheStore.bytes -= int64(len(e.Body))
			delete(cacheStore.mem, k)
			n++
		}
	}
	cacheStore.mu.Unlock()
	return n
}

func purgeHit(e *cacheEntry, m purgeMatch) bool {
	if m.host != "" && e.Host != m.host {
		return false
	}
	if m.path == "" {
		return true
	}
	if m.dir {
		return strings.HasPrefix(e.Path, m.path)
	}
	return e.Path == m.path
}

// Key 计算请求的缓存键（用于回源合并 singleflight 与缓存定位）；不可缓存返回 ""。
func (c *CacheLayer) Key(r *http.Request) string {
	if !c.enabled {
		return ""
	}
	rule := c.matchRule(r)
	if rule == nil {
		return ""
	}
	return c.key(r, rule)
}
