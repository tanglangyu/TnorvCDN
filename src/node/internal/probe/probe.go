package probe

// probe.go 区域探测（节点侧）：周期对本集群全部启用网站发起 HTTP 探测（经本节点自身回环访问
// 对外监听端口，模拟真实用户路径），结果随心跳上报主控，主控聚合展示各区域可用性。
// 设计：每次心跳周期（默认 30s）探测一轮；单站点超时 3s；探测失败不影响其它探测。

import (
	"context"
	"fmt"
	"net"
	"net/http"
	"sync"
	"time"

	"tnorv/node/pb"
)

var client = &http.Client{Timeout: 3 * time.Second}

// ProbeAll 探测全部站点（并发，超时安全），返回结果列表
func ProbeAll(servers []*pb.Server) []*pb.ProbeResult {
	var mu sync.Mutex
	var out []*pb.ProbeResult
	var wg sync.WaitGroup
	for _, srv := range servers {
		if len(srv.Domains) == 0 || (srv.ServerType != "http" && srv.ServerType != "") {
			continue
		}
		host := srv.Domains[0]
		port := 80
		if len(srv.HttpPorts) > 0 {
			port = srv.HttpPorts[0]
		}
		url := fmt.Sprintf("http://127.0.0.1:%d/", port)
		wg.Add(1)
		go func(serverId int64, host, url string) {
			defer wg.Done()
			r := probeOne(serverId, host, url)
			mu.Lock()
			out = append(out, r)
			mu.Unlock()
		}(srv.Id, host, url)
	}
	wg.Wait()
	return out
}

// ProbeOrigin 拨测源站（直连源站 host:port 的 TCP 握手 RTT，用于就近回源决策）
func ProbeOrigin(origins []string) []*pb.OriginProbe {
	var out []*pb.OriginProbe
	var mu sync.Mutex
	var wg sync.WaitGroup
	for _, o := range origins {
		wg.Add(1)
		go func(addr string) {
			defer wg.Done()
			start := time.Now()
			conn, err := net.DialTimeout("tcp", addr, 3*time.Second)
			latency := float64(time.Since(start).Microseconds()) / 1000.0
			if conn != nil {
				conn.Close()
			}
			mu.Lock()
			out = append(out, &pb.OriginProbe{Origin: addr, Reachable: err == nil, LatencyMs: latency})
			mu.Unlock()
		}(o)
	}
	wg.Wait()
	return out
}

// ProbePeer 拨测兄弟节点缓存端口（:3128）RTT
func ProbePeer(peers []string) []*pb.PeerProbe {
	var out []*pb.PeerProbe
	var mu sync.Mutex
	var wg sync.WaitGroup
	for _, p := range peers {
		wg.Add(1)
		go func(addr string) {
			defer wg.Done()
			start := time.Now()
			conn, err := net.DialTimeout("tcp", addr, 2*time.Second)
			latency := float64(time.Since(start).Microseconds()) / 1000.0
			if conn != nil {
				conn.Close()
			}
			mu.Lock()
			out = append(out, &pb.PeerProbe{Peer: addr, Reachable: err == nil, LatencyMs: latency})
			mu.Unlock()
		}(p)
	}
	wg.Wait()
	return out
}

// 集群健康检查参数（main 在 onConfig 中随 Ops 更新；零值用默认）
var (
	probeMu        sync.Mutex
	timeoutMs      = 5000
	intervalHint   = 15 // 节点当前 15s 心跳即一轮探测，interval 仅作展示口径
)

// SetParams 更新健康检查参数（集群设置下发）
func SetParams(intervalSeconds, timeoutMsIn, failThreshold int) {
	probeMu.Lock()
	defer probeMu.Unlock()
	if timeoutMsIn > 0 {
		timeoutMs = timeoutMsIn
	}
	if intervalSeconds > 0 {
		intervalHint = intervalSeconds
	}
	_ = failThreshold
}

func probeOne(serverId int64, host, url string) *pb.ProbeResult {
	req, err := http.NewRequest(http.MethodGet, url, nil)
	if err != nil {
		return &pb.ProbeResult{ServerId: serverId, Url: url, Ok: false, Error: err.Error()}
	}
	req.Host = host
	req.Header.Set("User-Agent", "TnorvProbe/1.0")
	probeMu.Lock()
	tmo := time.Duration(timeoutMs) * time.Millisecond
	probeMu.Unlock()
	ctx, cancel := context.WithTimeout(req.Context(), tmo)
	defer cancel()
	req = req.WithContext(ctx)
	start := time.Now()
	resp, err := client.Do(req)
	latency := float64(time.Since(start).Microseconds()) / 1000.0
	if err != nil {
		return &pb.ProbeResult{ServerId: serverId, Url: url, Ok: false, LatencyMs: latency, Error: err.Error()}
	}
	defer resp.Body.Close()
	ok := resp.StatusCode < 500
	errMsg := ""
	if !ok {
		errMsg = fmt.Sprintf("status %d", resp.StatusCode)
	}
	return &pb.ProbeResult{ServerId: serverId, Url: url, Ok: ok, Status: resp.StatusCode, LatencyMs: latency, Error: errMsg}
}
