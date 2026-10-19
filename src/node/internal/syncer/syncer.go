// Package syncer 周期任务：拉配置（热更新）、心跳、日志批量上报。
package syncer

import (
	"strconv"
	"sync"
	"time"

	"tnorv/node/internal/config"
	"tnorv/node/internal/master"
	"tnorv/node/internal/probe"
	"tnorv/node/pb"
)

type ConfigUpdateFn func(*pb.NodeConfig)

// CertStateSource 节点 TLS 管理器的证书状态批量导出（main 注入，心跳携带上报）
var CertStateSource func() []*pb.CertState

type Syncer struct {
	cfg     *config.NodeConfig
	cfgPath string
	client  *master.Client

 onUpdate ConfigUpdateFn
	mu       sync.Mutex

	logBuf   []*pb.AccessLog
	logMu    sync.Mutex
	stopCh   chan struct{}

	probeMu       sync.Mutex
	latestServers []*pb.Server // 最近一次配置的站点（区域探测用）
	latestPeers   []string     // 最近一次配置的兄弟节点 IP（拨测用）
}

func New(cfg *config.NodeConfig, cfgPath string, client *master.Client, onUpdate ConfigUpdateFn) *Syncer {
	return &Syncer{
		cfg:     cfg,
		cfgPath: cfgPath,
		client:  client,
		onUpdate: onUpdate,
		stopCh:  make(chan struct{}),
	}
}

// Start 启动三个循环：配置（10s）、心跳（15s）、日志（5s 批量）
func (s *Syncer) Start() {
	go s.configLoop()
	go s.heartbeatLoop()
	go s.logLoop()
}

func (s *Syncer) Stop() { close(s.stopCh) }

func (s *Syncer) configLoop() {
	ticker := time.NewTicker(10 * time.Second)
	defer ticker.Stop()
	s.pullOnce() // 启动即拉一次
	for {
		select {
		case <-ticker.C:
			s.pullOnce()
		case <-s.stopCh:
			return
		}
	}
}

func (s *Syncer) pullOnce() {
	resp, err := s.client.GetConfig()
	if err != nil {
		return // 网络异常静默重试
	}
	if resp.Changed && resp.Parsed != nil {
		s.cfg.ConfigVersion = resp.ConfigVersion
		_ = config.Save(s.cfgPath, s.cfg)
		s.mu.Lock()
		fn := s.onUpdate
		s.mu.Unlock()
		if fn != nil {
			fn(resp.Parsed) // 原子替换路由表（热更新）
		}
		s.probeMu.Lock()
		s.latestServers = resp.Parsed.Servers
		var peerIps []string
		for _, n := range resp.Parsed.SiblingNodes {
			if n.Id != s.cfg.NodeID {
				peerIps = append(peerIps, n.Ips...)
			}
		}
		s.latestPeers = peerIps
		s.probeMu.Unlock()
	}
}

// currentPeers 返回最近一次配置的兄弟节点 IP 列表（拨测用）
func (s *Syncer) currentPeers() []string {
	s.probeMu.Lock()
	defer s.probeMu.Unlock()
	return s.latestPeers
}

// currentServers 返回最近一次配置的站点列表（探测用）
func (s *Syncer) currentServers() []*pb.Server {
	s.probeMu.Lock()
	defer s.probeMu.Unlock()
	return s.latestServers
}

func (s *Syncer) heartbeatLoop() {
	ticker := time.NewTicker(15 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ticker.C:
			stats := collectStats()
			// 区域探测：对本集群站点做一轮 HTTP 探测并随心跳上报
			if servers := s.currentServers(); len(servers) > 0 {
				stats.Probes = probe.ProbeAll(servers)
				// 拨测（就近回源决策数据）：源站 TCP RTT + 兄弟节点 :3128 RTT
				originSet := map[string]bool{}
				for _, srv := range servers {
					for _, o := range srv.Origin.Primaries {
						if o.Enabled {
							port := o.Port
					if port == 0 { port = 80 }
					originSet[o.Host+":"+strconv.Itoa(port)] = true
						}
					}
				}
				var origins []string
				for a := range originSet {
					origins = append(origins, a)
				}
				stats.OriginProbes = probe.ProbeOrigin(origins)
				stats.PeerProbes = probe.ProbePeer(s.currentPeers())
			}
			if CertStateSource != nil {
				if cs := CertStateSource(); len(cs) > 0 {
					stats.CertStates = cs
				}
			}
			s.client.Heartbeat(stats)
		case <-s.stopCh:
			return
		}
	}
}

// EnqueueLog 缓冲一条日志（由 pipeline 调用）
func (s *Syncer) EnqueueLog(entry *pb.AccessLog) {
	s.logMu.Lock()
	defer s.logMu.Unlock()
	if len(s.logBuf) < s.cfg.LogBufferMax {
		s.logBuf = append(s.logBuf, entry)
	}
}

func (s *Syncer) logLoop() {
	ticker := time.NewTicker(5 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ticker.C:
			s.flushLogs()
		case <-s.stopCh:
			s.flushLogs()
			return
		}
	}
}

func (s *Syncer) flushLogs() {
	s.logMu.Lock()
	if len(s.logBuf) == 0 {
		s.logMu.Unlock()
		return
	}
	batch := s.logBuf
	s.logBuf = nil
	s.logMu.Unlock()

	if err := s.client.ReportLogs(batch); err != nil {
		// 上报失败回填缓冲（断网不丢，超上限丢弃最旧）
		s.logMu.Lock()
		if len(s.logBuf)+len(batch) <= s.cfg.LogBufferMax {
			s.logBuf = append(batch, s.logBuf...)
		}
		s.logMu.Unlock()
	}
}

// collectStats 采集节点指标（M1 简化：读 /proc）
func collectStats() *pb.NodeStats {
	stats := &pb.NodeStats{UptimeSeconds: uint32(time.Since(startTime).Seconds())}
	if load, err := osReadLoadavg(); err == nil {
		stats.Load = load
	}
	stats.CpuUsage = osReadCpuPercent()
	stats.MemUsage = osReadMemPercent()
	return stats
}

var startTime = time.Now()
