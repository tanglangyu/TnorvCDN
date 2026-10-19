// Package l4 四层转发：TCP 反向代理与负载均衡通用设计。
//   - TCP：端口监听 → 后端池轮询（含备用后端切换）→ 透传字节流
//   - 健康检查：TCP 拨号探测，失败摘除、恢复自动加回；全局状态跨热更新保留
//   - UDP 转发（P1）：会话表映射
package l4

import (
	"encoding/binary"
	"io"
	"log"
	"net"
	"strings"
	"sync"
	"time"

	"tnorv/node/pb"
)

// 全局健康状态（跨配置热更新保留，与七层同构设计）
var globalHealth sync.Map // "host:port" -> bool

type Manager struct {
	mu        sync.RWMutex
	listeners     map[string]net.Listener // "tcp:8081" → listener
	udpListeners  map[int]*net.UDPConn    // 端口 → UDP listener
	services      []*pb.Server            // 当前 L4 服务
	stopped       bool
}

func NewManager() *Manager {
	return &Manager{listeners: make(map[string]net.Listener), udpListeners: make(map[int]*net.UDPConn)}
}

// Update 从节点配置热更新 L4 服务（对比 serverType）
func (m *Manager) Update(cfg *pb.NodeConfig) {
	var l4Servers []*pb.Server
	for _, srv := range cfg.Servers {
		if srv.ServerType == "tcp" || srv.ServerType == "tls" {
			l4Servers = append(l4Servers, srv)
		}
	}
	// UDP 服务
	m.updateUdp(cfg.Servers)

	m.mu.Lock()
	defer m.mu.Unlock()
	if m.stopped {
		return
	}

	// 差异化重载：关闭不再存在的监听，打开新监听
	wanted := make(map[string]*pb.Server)
	for _, srv := range l4Servers {
		for _, port := range srv.L4Ports {
			key := "tcp:" + itoa(port)
			wanted[key] = srv
		}
	}

	// 关闭多余的
	for key, ln := range m.listeners {
		if _, ok := wanted[key]; !ok {
			ln.Close()
			delete(m.listeners, key)
			log.Printf("[l4] 已关闭监听 %s", key)
		}
	}

	// 打开新增的
	for key, srv := range wanted {
		if _, ok := m.listeners[key]; ok {
			continue
		}
		parts := strings.SplitN(key, ":", 2)
		ln, err := net.Listen(parts[0], ":"+parts[1])
		if err != nil {
			log.Printf("[l4] 监听 %s 失败: %v", key, err)
			continue
		}
		m.listeners[key] = ln
		m.services = append(m.services, srv)
		log.Printf("[l4] TCP 负载均衡监听 %s（服务 %d，源站 %d 个）", key, srv.Id, len(srv.Origin.Primaries))
		go m.serveTCP(ln, srv)
	}
	m.services = l4Servers
}

func (m *Manager) serveTCP(ln net.Listener, srv *pb.Server) {
	for {
		conn, err := ln.Accept()
		if err != nil {
			m.mu.RLock()
			stopped := m.stopped
			m.mu.RUnlock()
			if stopped {
				return
			}
			continue
		}
		// 端口未变化时监听被保留，但配置可能已更新（如 L4ProxyProtocol 开关）——
		// 每次 accept 取 m.services 中同 Id 的最新配置
		cur := srv
		m.mu.RLock()
		for _, s := range m.services {
			if s.Id == srv.Id {
				cur = s
				break
			}
		}
		m.mu.RUnlock()
		go m.handleConn(conn, cur)
	}
}

func (m *Manager) handleConn(client net.Conn, srv *pb.Server) {
	defer client.Close()

	backend := m.pick(srv)
	if backend == nil {
		client.Close()
		return
	}

	upstream, err := net.DialTimeout("tcp", addrOf(backend), 5*time.Second)
	if err != nil {
		globalHealth.Store(addrOf(backend), false)
		// 尝试备用后端
		for _, b := range srv.Origin.Backups {
			if !b.Enabled {
				continue
			}
			if h, ok := globalHealth.Load(addrOf(b)); ok && !h.(bool) {
				continue
			}
			upstream, err = net.DialTimeout("tcp", addrOf(b), 5*time.Second)
			if err == nil {
				globalHealth.Store(addrOf(b), true)
				break
			}
		}
		if err != nil {
			return
		}
	} else {
		globalHealth.Store(addrOf(backend), true)
	}
	defer upstream.Close()

	// PROXY protocol v2 前置头（TOA 的用户态等价：向源站传递真实客户端 IP/端口/协议）
	// 通用设计
	if srv.L4ProxyProtocol {
		if _, err := upstream.Write(proxyV2Header(client, upstream)); err != nil {
			return
		}
	}

	// 双向透传
	done := make(chan struct{}, 2)
	cp := func(dst, src net.Conn) {
		io.Copy(dst, src)
		done <- struct{}{}
	}
	go cp(upstream, client)
	go cp(client, upstream)
	<-done
}

// pick 轮询选择健康后端（主池全不健康 → 备用池）
func (m *Manager) pick(srv *pb.Server) *pb.Origin {
	if b := pickHealthy(srv.Origin.Primaries); b != nil {
		return b
	}
	return pickHealthy(srv.Origin.Backups)
}

func pickHealthy(pool []*pb.Origin) *pb.Origin {
	var candidates []*pb.Origin
	for _, o := range pool {
		if !o.Enabled {
			continue
		}
		if h, ok := globalHealth.Load(addrOf(o)); ok && !h.(bool) {
			continue
		}
		candidates = append(candidates, o)
	}
	if len(candidates) == 0 {
		return nil
	}
	// 轮询（简化：时间戳取模）
	idx := time.Now().UnixNano() % int64(len(candidates))
	return candidates[idx]
}

func addrOf(o *pb.Origin) string {
	return net.JoinHostPort(o.Host, itoa(o.Port))
}

func itoa(i int) string {
	if i == 0 {
		return "0"
	}
	digits := ""
	for n := i; n > 0; n /= 10 {
		digits = string(rune('0'+n%10)) + digits
	}
	return digits
}

// healthLoop 周期探测 L4 后端（TCP 拨号）
func (m *Manager) healthLoop() {
	ticker := time.NewTicker(15 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		m.mu.RLock()
		services := m.services
		m.mu.RUnlock()
		for _, srv := range services {
			for _, pool := range [][]*pb.Origin{srv.Origin.Primaries, srv.Origin.Backups} {
				for _, o := range pool {
					if !o.Enabled {
						continue
					}
					conn, err := net.DialTimeout("tcp", addrOf(o), 3*time.Second)
					healthy := err == nil
					if conn != nil {
						conn.Close()
					}
					globalHealth.Store(addrOf(o), healthy)
				}
			}
		}
	}
}

// Start 启动健康检查循环
func (m *Manager) Start() {
	go m.healthLoop()
}

// Stop 关闭全部监听
func (m *Manager) Stop() {
	m.mu.Lock()
	m.stopped = true
	for _, ln := range m.listeners {
		ln.Close()
	}
	m.mu.Unlock()
}

var _ = io.Copy

// ---- UDP 四层转发 ----
// udpConns 活跃 UDP 会话（client addr → upstream conn），空闲 60s 回收
var udpConns = struct {
	sync.Mutex
	m map[string]net.Conn
}{m: make(map[string]net.Conn)}

func (m *Manager) updateUdp(servers []*pb.Server) {
	// 差异化：wanted 端口集
	wanted := make(map[int]*pb.Server)
	for _, srv := range servers {
		if srv.ServerType == "udp" {
			for _, port := range srv.L4Ports {
				wanted[port] = srv
			}
		}
	}
	// 关闭多余
	m.mu.Lock()
	for key := range m.udpListeners {
		port := key
		if _, ok := wanted[port]; !ok {
			m.udpListeners[port].Close()
			delete(m.udpListeners, port)
			log.Printf("[l4] 已关闭 UDP 监听 :%d", port)
		}
	}
	// 打开新增
	for port, srv := range wanted {
		if _, ok := m.udpListeners[port]; ok {
			continue
		}
		addr := &net.UDPAddr{Port: port}
		pc, err := net.ListenUDP("udp", addr)
		if err != nil {
			log.Printf("[l4] UDP 监听 :%d 失败: %v", port, err)
			continue
		}
		m.udpListeners[port] = pc
		backends := make([]string, 0)
		for _, o := range srv.Origin.Primaries {
			if o.Enabled {
				backends = append(backends, addrOf(o))
			}
		}
		if len(backends) == 0 {
			pc.Close()
			delete(m.udpListeners, port)
			continue
		}
		log.Printf("[l4] UDP 负载均衡监听 udp:%d（后端 %d 个）", port, len(backends))
		go udpServe(pc, backends)
	}
	m.mu.Unlock()
}

// udpServe 收包 → 轮询后端转发 → 回包送回客户端
func udpServe(pc *net.UDPConn, backends []string) {
	buf := make([]byte, 65535)
	idx := 0
	for {
		n, cli, err := pc.ReadFromUDP(buf)
		if err != nil {
			return
		}
		backend := backends[idx%len(backends)]
		idx++
		up, err := net.Dial("udp", backend)
		if err != nil {
			continue
		}
		// 会话复用：同一客户端 addr 固定一个 upstream（UDP 会话亲和）
		udpConns.Lock()
		key := cli.String()
		if existing, ok := udpConns.m[key]; ok {
			existing.Close()
		}
		udpConns.m[key] = up
		udpConns.Unlock()
		// 回包通道
		go func(cli *net.UDPAddr, up net.Conn) {
			tmp := make([]byte, 65535)
			for {
				up.SetReadDeadline(time.Now().Add(60 * time.Second))
				n, err := up.Read(tmp)
				if err != nil {
					return
				}
				pc.WriteToUDP(tmp[:n], cli)
			}
		}(cli, up)
		up.Write(buf[:n])
	}
}

// proxyV2Header 生成 PROXY protocol v2 二进制头（TCP4/TCP6 自动识别）
// 12 字节签名 + 1 ver/cmd + 1 fam/proto + 2 长度 + 地址
func proxyV2Header(client, upstream net.Conn) []byte {
	sig := []byte{0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A}
	local := client.LocalAddr().(*net.TCPAddr)
	remote := client.RemoteAddr().(*net.TCPAddr)
	dst := upstream.LocalAddr().(*net.TCPAddr)

	isV4 := remote.IP.To4() != nil
	var famProto byte
	var addrLen int
	var addr []byte
	if isV4 {
		famProto = 0x11 // TCP4
		addrLen = 12
		addr = make([]byte, 12)
		copy(addr[0:4], remote.IP.To4())
		copy(addr[4:8], dst.IP.To4())
		binary.BigEndian.PutUint16(addr[8:10], uint16(remote.Port))
		binary.BigEndian.PutUint16(addr[10:12], uint16(local.Port))
	} else {
		famProto = 0x21 // TCP6
		addrLen = 36
		addr = make([]byte, 36)
		copy(addr[0:16], remote.IP.To16())
		copy(addr[16:32], dst.IP.To16())
		binary.BigEndian.PutUint16(addr[32:34], uint16(remote.Port))
		binary.BigEndian.PutUint16(addr[34:36], uint16(local.Port))
	}
	head := append(sig, 0x20, famProto, byte(addrLen>>8), byte(addrLen))
	return append(head, addr...)
}
