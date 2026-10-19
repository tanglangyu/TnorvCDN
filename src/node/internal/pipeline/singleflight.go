package pipeline

// singleflight.go 回源合并：同一缓存键的并发未命中请求只放行一个回源，
// 其余等待结果共享通用设计。
//
// 设计：等待者共享响应字节快照（缓存路径本就要求 body 进内存才能 Store，
// 因此合并等待者不引入额外内存峰值）；不可缓存请求不参与合并（零拷贝直通）。

import (
	"net/http"
	"sync"
)

type flightCall struct {
	wg   sync.WaitGroup
	body []byte
	ct   string
	code int
}

// globalFlights 跨配置热更新保留（Pipeline 每 10s 重建，合并状态必须是全局的）
var globalFlights = struct {
	sync.Mutex
	m map[string]*flightCall
}{m: make(map[string]*flightCall)}

// doFlight 对 key 执行回源合并。
//   - key==""：不可缓存，直接执行 fn（不合并）
//   - 首个请求：执行 fn（回源），结果广播给所有等待者
//   - 后续请求：阻塞等待首个结果，共享响应
func doFlight(key string, write func(code int, ct string, body []byte), fn func() (int, string, []byte)) {
	if key == "" {
		code, ct, body := fn()
		write(code, ct, body)
		return
	}

	globalFlights.Lock()
	if c, ok := globalFlights.m[key]; ok {
		globalFlights.Unlock()
		c.wg.Wait()
		write(c.code, c.ct, c.body)
		return
	}
	c := &flightCall{}
	c.wg.Add(1)
	globalFlights.m[key] = c
	globalFlights.Unlock()

	// 首个请求：真正回源（结果同时是缓存写回的数据）
	c.code, c.ct, c.body = fn()
	c.wg.Done()

	globalFlights.Lock()
	delete(globalFlights.m, key)
	globalFlights.Unlock()

	write(c.code, c.ct, c.body)
}

// ---- 回源合并的 HTTP 集成 ----

// serveWithFlight 缓存未命中路径的合并回源：
// 与 pipeline.serve 的 recorder 流程等价，但并发同键请求共享一次回源。
// cacheLayer 允许 location 级缓存覆盖（nil 时回退站点级）。
func (p *Pipeline) serveWithFlight(w http.ResponseWriter, r *http.Request, key string) {
	cacheLayer := p.cache
	if lc := matchLocation(p.locs, r.URL.Path).locCache(); lc != nil {
		cacheLayer = lc
	}
	write := func(code int, ct string, body []byte) {
		writeCompressed(w, r, code, ct, body)
	}
	// 回源执行体：捕获响应（供缓存 Store 与等待者共享）。
	// discard=true：只捕获不透传 —— leader 的响应由下方 write() 统一写出，
	// 否则 recorder 直通 + write() 重放会把响应头/体写两遍（superfluous WriteHeader + 重复 body）。
	fn := func() (int, string, []byte) {
		w.Header().Set("X-Cache", "MISS")
		recorder := &responseRecorder{ResponseWriter: w, status: 200, discard: true}
		p.proxy.ServeHTTP(recorder, r)
		code := recorder.status
		ct := recorder.Header().Get("Content-Type")
		body := recorder.bodySnapshot()
		// 自定义错误页：源站 404/5xx 用站点配置页面替换（仅替换应答，缓存仍只存 200）
		if code == 404 && p.pages != nil && p.pages.NotFound != "" {
			body, ct = []byte(p.pages.NotFound), "text/html; charset=utf-8"
		} else if code >= 500 && p.pages != nil && p.pages.ServerError != "" {
			body, ct = []byte(p.pages.ServerError), "text/html; charset=utf-8"
		}
		if code == 200 && cacheLayer != nil {
			cacheLayer.Store(r, code, ct, body)
		}
		return code, ct, body
	}
	doFlight(key, write, fn)
}
