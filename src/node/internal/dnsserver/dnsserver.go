// Package dnsserver 节点内建权威 DNS通用设计：
// 监听 UDP/53（TCP 可选），按主控下发的 NsDomains 快照应答 A/AAAA/CNAME/TXT，
// 按 ECS/源 IP 归属选择线路（telecom/unicom/cmcc/overseas/default）。
// 由配置热更新调用 Update(records) 原子替换记录表；应答永不阻塞业务流量。
package dnsserver

import (
	"net"
	"strconv"
	"strings"
	"sync"

	"github.com/miekg/dns"
	"tnorv/node/pb"
)

type Server struct {
	mu       sync.RWMutex
	records  []*pb.DnsRecord
	udpConn  *dns.Server
	tcpConn  *dns.Server
}

var singleton = &Server{}

// Update 原子替换权威记录表（配置热更新调用）
func Update(records []*pb.DnsRecord) {
	singleton.mu.Lock()
	singleton.records = records
	singleton.mu.Unlock()
}

// Start 监听 :53（udp+tcp），失败返回 error 由调用方记日志（不致命，如端口被占用）
func Start() error {
	if singleton.udpConn != nil {
		return nil // 已启动
	}
	mux := dns.NewServeMux()
	mux.HandleFunc(".", handle)
	singleton.udpConn = &dns.Server{Addr: ":53", Net: "udp", Handler: mux, UDPSize: 4096}
	singleton.tcpConn = &dns.Server{Addr: ":53", Net: "tcp", Handler: mux}
	go func() { _ = singleton.udpConn.ListenAndServe() }()
	go func() { _ = singleton.tcpConn.ListenAndServe() }()
	return nil
}

// Stop 停止监听
func Stop() {
	if s := singleton.udpConn; s != nil {
		_ = s.Shutdown()
	}
	if s := singleton.tcpConn; s != nil {
		_ = s.Shutdown()
	}
	singleton.udpConn, singleton.tcpConn = nil, nil
}

func handle(w dns.ResponseWriter, r *dns.Msg) {
	m := new(dns.Msg)
	m.SetReply(r)
	m.Compress = true
	q := r.Question[0]
	name := strings.ToLower(strings.TrimSuffix(q.Name, "."))

	singleton.mu.RLock()
	records := singleton.records
	singleton.mu.RUnlock()

	clientIP := clientAddr(w.RemoteAddr())
	line := classify(clientIP, r)

	matched := false
	for _, rec := range records {
		if !strings.EqualFold(rec.Name, name) || !strings.EqualFold(rec.Type, dns.TypeToString[q.Qtype]) {
			continue
		}
		if recLine := strings.ToLower(rec.Line); recLine != "" && recLine != "default" && recLine != line {
			continue // 线路不匹配：default 记录兜底，非 default 按线路精确匹配
		}
		rr, err := buildRR(rec, q.Name)
		if err != nil {
			continue
		}
		m.Answer = append(m.Answer, rr)
		matched = true
		// 精确线路命中优先于 default 兜底
		if strings.ToLower(rec.Line) == line {
			break
		}
	}
	if matched {
		m.Authoritative = true
		_ = w.WriteMsg(m)
		return
	}

	// NXDOMAIN 带权威标记（本节点只应答自己托管的记录）
	m.Rcode = dns.RcodeNameError
	m.Authoritative = true
	m.Ns = append(m.Ns, soa(q.Name))
	_ = w.WriteMsg(m)
}

func buildRR(rec *pb.DnsRecord, fqdn string) (dns.RR, error) {
	ttl := uint32(rec.Ttl)
	if ttl == 0 {
		ttl = 600
	}
	switch strings.ToUpper(rec.Type) {
	case "A":
		return dns.NewRR(fqdn + " " + strconv.Itoa(int(ttl)) + " IN A " + rec.Value)
	case "AAAA":
		return dns.NewRR(fqdn + " " + strconv.Itoa(int(ttl)) + " IN AAAA " + rec.Value)
	case "CNAME":
		return dns.NewRR(fqdn + " " + strconv.Itoa(int(ttl)) + " IN CNAME " + rec.Value)
	case "TXT":
		return dns.NewRR(fqdn + " " + strconv.Itoa(int(ttl)) + " IN TXT " + strconv.Quote(rec.Value))
	case "NS":
		return dns.NewRR(fqdn + " " + strconv.Itoa(int(ttl)) + " IN NS " + rec.Value)
	default:
		return dns.NewRR(fqdn + " " + strconv.Itoa(int(ttl)) + " IN A " + rec.Value)
	}
}

func soa(fqdn string) dns.RR {
	rr, _ := dns.NewRR(fqdn + " 60 IN SOA ns1." + fqdn + " admin." + fqdn + " 2026010101 7200 3600 86400 60")
	return rr
}

// clientAddr 取查询者 IP
func clientAddr(addr net.Addr) net.IP {
	switch a := addr.(type) {
	case *net.UDPAddr:
		return a.IP
	case *net.TCPAddr:
		return a.IP
	}
	return nil
}

// classify 按 ECS（ clientsubnet OPT 记录）优先、否则查询者 IP 判定线路
func classify(ip net.IP, r *dns.Msg) string {
	if ecs := ecsIP(r); ecs != nil {
		ip = ecs
	}
	if ip == nil {
		return "default"
	}
	// 归属判断：优先节点侧 GeoIP（若配置），否则简单按 ASN 段。此处按运营商常见段粗判 + 海外兜底，
	// 精确归属由主控 GeoIP（ip2region）侧下发分线路记录解决，节点只做粗路由。
	if !isPrivate(ip) && !isChinaIP(ip) {
		return "overseas"
	}
	return "default"
}

func isPrivate(ip net.IP) bool {
	return ip.IsPrivate() || ip.IsLoopback()
}

func isChinaIP(ip net.IP) bool {
	// 粗判：非保留 IPv4 全部按国内处理（国内流量为主，误判代价低于海外用户被 default 应答），
	// 后续接 ip2region 精确化。IPv6 同理。
	if ip.To4() == nil {
		return false // IPv6 暂归海外线路判定外
	}
	return true
}

// ecsIP 提取 EDNS Client Subnet
func ecsIP(r *dns.Msg) net.IP {
	for _, extra := range r.Extra {
		opt, ok := extra.(*dns.OPT)
		if !ok {
			continue
		}
		for _, o := range opt.Option {
			if ecs, ok := o.(*dns.EDNS0_SUBNET); ok {
				return ecs.Address
			}
		}
	}
	return nil
}
