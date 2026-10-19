// tnorv-node：TnorvCDN 边缘节点。
//
// 启动流程通用设计：
//  1. 加载 configs/tnorv-node.json（不存在则用默认值）
//  2. 若有 installToken 且无 nodeToken → 向主控注册
//  3. 拉取配置，构建路由表（域名 → 网站管道）
//  4. 启动 HTTP/HTTPS/L4 服务；周期同步配置/心跳/日志
package main

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"encoding/pem"
	"flag"
	"fmt"
	"github.com/quic-go/quic-go/http3"
	"io"
	"log"
	"math/big"
	"net"
	"net/http"
	"os"
	"strings"
	"sync"
	"time"

	"tnorv/node/internal/config"
	"tnorv/node/internal/dnsserver"
	"tnorv/node/internal/ddos"
	"tnorv/node/internal/l4"
	"tnorv/node/internal/master"
	"tnorv/node/internal/pipeline"
	"tnorv/node/internal/probe"
	"tnorv/node/internal/syncer"
	"tnorv/node/internal/tlsx"
	"tnorv/node/internal/upgrade"
	"tnorv/node/pb"
)

const defaultCfgPath = "configs/tnorv-node.json"

// router 域名路由表（热更新时原子替换）
type router struct {
	mu         sync.RWMutex
	entries    map[string]*pipeline.Pipeline // 小写域名（不含端口）→ 管道
	forceHttps map[string]bool
	tls        *tlsx.Manager
}

func (rt *router) Update(cfg *pb.NodeConfig) {
	entries := make(map[string]*pipeline.Pipeline, len(cfg.Servers))
	forceHttpsSet := make(map[string]bool)
	for _, srv := range cfg.Servers {
		p := pipeline.New(srv, "")
		for _, d := range srv.Domains {
			entries[strings.ToLower(d)] = p
			if srv.ForceHttps {
				forceHttpsSet[strings.ToLower(d)] = true
			}
		}
	}
	rt.mu.Lock()
	rt.entries = entries
	rt.forceHttps = forceHttpsSet
	rt.mu.Unlock()
	if rt.tls != nil {
		rt.tls.SyncFromConfig(cfg)
	}
	log.Printf("[router] 已加载 %d 个网站 / %d 个域名", len(cfg.Servers), len(entries))
}

func (rt *router) Lookup(host string) *pipeline.Pipeline {
	host = strings.ToLower(host)
	if i := strings.LastIndex(host, ":"); i >= 0 {
		host = host[:i]
	}
	host = strings.Trim(host, "[]")
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	if p, ok := rt.entries[host]; ok {
		return p
	}
	// 通配符匹配 *.example.com
	if idx := strings.Index(host, "."); idx > 0 {
		if p, ok := rt.entries["*"+host[idx:]]; ok {
			return p
		}
	}
	return nil
}

func (rt *router) IsForceHttps(host string) bool {
	host = strings.ToLower(host)
	if i := strings.LastIndex(host, ":"); i >= 0 {
		host = host[:i]
	}
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	if v, ok := rt.forceHttps[host]; ok {
		return v
	}
	if idx := strings.Index(host, "."); idx > 0 {
		return rt.forceHttps["*"+host[idx:]]
	}
	return false
}

type proxyHandler struct {
	rt *router
}

func (h *proxyHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	// HTTP→HTTPS 强制跳转
	if r.TLS == nil && h.rt.IsForceHttps(r.Host) {
		http.Redirect(w, r, "https://"+r.Host+r.RequestURI, http.StatusMovedPermanently)
		return
	}
	p := h.rt.Lookup(r.Host)
	if p == nil {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.WriteHeader(http.StatusNotFound)
		fmt.Fprint(w, "<html><body><h1>404</h1><p>域名未绑定到本节点</p></body></html>")
		return
	}
	p.ServeHTTP(w, r)
}

func main() {
	// 隐藏自测模式：--h3-selftest（验证 QUIC/HTTP-3 栈，不进入主流程）
	for _, a := range os.Args[1:] {
		if a == "--h3-selftest" {
			h3Selftest()
			return
		}
	}
	cfgPath := flag.String("c", defaultCfgPath, "配置文件路径")
	flag.Parse()

	cfg, err := config.Load(*cfgPath)
	if err != nil {
		log.Fatalf("加载配置失败: %v", err)
	}

	client := master.NewClient(cfg, *cfgPath)

	// 首次注册（installToken 一次性）
	if cfg.NodeToken == "" && cfg.InstallToken != "" {
		for attempt := 1; attempt <= 30; attempt++ {
			if err := client.Register(); err == nil {
				log.Printf("[master] 注册成功 nodeId=%d", cfg.NodeID)
				break
			} else {
				log.Printf("[master] 注册失败（第 %d 次）: %v", attempt, err)
				time.Sleep(10 * time.Second)
			}
		}
	}

	rt := &router{}
	rt.tls = tlsx.NewManager("/opt/tnorv/certs", "", os.Getenv("ACME_STAGING"))
	// 注册成功后主控已保存节点公网 IP；ACME DNS 预校验需要本节点 IP 集合，
	// 这里直接用探测到的本机公网 IP + 已保存节点 IP（若注册过则从主控回读，简化为本机探测）
	issuer := tlsx.NewControlledIssuer(rt.tls, cfg.MasterURL, cfg.NodeToken) // 受控签发器：单证书多节点共享
	rt.tls.SetPublicIPs(master.DetectPublicIPs())
	// NAT 节点内网探测不到公网 IP：主控在配置响应里回传连接来源 IP，用于 ACME DNS 预校验
	master.PublicIpSink = func(ip string) {
		if ip != "" {
			rt.tls.SetPublicIPs(append(master.DetectPublicIPs(), ip))
		}
	}
	// 证书签发状态随心跳上报主控（用户侧只读展示：签发中/已签发/未解析/失败）
	syncer.CertStateSource = func() []*pb.CertState {
		states := rt.tls.DrainStates()
		out := make([]*pb.CertState, 0, len(states))
		for _, s := range states {
			exp := ""
			if !s.ExpireAt.IsZero() {
				exp = s.ExpireAt.Format(time.RFC3339)
			}
			out = append(out, &pb.CertState{Domain: s.Domain, Status: s.Status, Error: s.Error, ExpiresAt: exp})
		}
		return out
	}

	// L4 TCP/UDP 转发
	l4mgr := l4.NewManager()
	l4mgr.Start()

	// 配置更新回调：七层路由 + TLS + L4 一起热更新，并执行缓存刷新指令
	onConfig := func(c *pb.NodeConfig) {
		rt.Update(c)
		l4mgr.Update(c)
		execPurges(c)
		// L2 就近回源：收集同集群二级汇聚节点 IP
		var l2ips []string
		for _, n := range c.L2Nodes {
			l2ips = append(l2ips, n.Ips...)
		}
		pipeline.SetL2Nodes(l2ips)
		// 边缘缓存共享：同组边缘节点 IP（排除自身），供 P2P 缓存拉取
		var peerIps []string
		for _, n := range c.SiblingNodes {
			if n.Id != cfg.NodeID {
				peerIps = append(peerIps, n.Ips...)
			}
		}
		pipeline.SetSharePeers(peerIps)
		rt.tls.SetSiblingIPs(peerIps)      // 兄弟节点 IP：多节点场景 pending_dns 抑制
		rt.tls.SetChallenges(c.Challenges) // 受控签发：主控下发的 HTTP-01 挑战（全节点监听应答）
		// ACME 账户邮箱变更 → 轮换受控签发账户（新邮箱注册新账户，绕开 LE 按账户计的配额限流）
		if c.Acme != nil && c.Acme.Email != "" {
			issuer.OnEmailChanged(c.Acme.Email)
		}
		// 挑战就绪回执：向主控确认本节点已监听这些路径（执行节点据此判断"任意节点可应答"后再 Accept）
		for _, ch := range c.Challenges {
			if ch != nil && ch.Path != "" {
				go issuer.ChallengeReady(ch.Path)
			}
		}
		// 动态就近回源：应用主控拨测调度结果
		pipeline.SetRoutingPlan(c.Routing)
		// 智能 DNS：应用主控下发的权威记录快照（节点内建 DNS :53 应答）
		dnsserver.Update(c.DnsRecords)
		// 健康检查参数（集群设置下发）
		if c.Ops != nil {
			probe.SetParams(c.Ops.ProbeIntervalSeconds, c.Ops.ProbeTimeoutMs, c.Ops.ProbeFailThreshold)
		}
		// 自升级指令：版本与当前不同则下载替换重启（幂等：版本文件防重复）
		if c.Upgrade != nil && c.Upgrade.Version != "" && c.Upgrade.Version != master.Version {
			marker := "/opt/tnorv/.upgraded-" + c.Upgrade.Version
			if _, err := os.Stat(marker); err != nil {
				log.Printf("[upgrade] 配置下发升级: %s -> %s", master.Version, c.Upgrade.Version)
				os.WriteFile(marker, []byte(time.Now().Format(time.RFC3339)), 0o644)
				go func() {
					if err := upgrade.Self(c.Upgrade.URL, c.Upgrade.Version, c.Upgrade.SHA256); err != nil {
						log.Printf("[upgrade] 失败: %v", err)
						os.Remove(marker)
					}
				}()
			}
		}

		// DDoS 防护：nftables 规则热更新（集群级）
		if applied, err := ddos.ApplyDdos(c.Ddos); err != nil {
			log.Printf("[ddos] 规则应用失败: %v", err)
		} else if applied {
			log.Printf("[ddos] nftables 防护规则已更新")
		}
		for _, mc := range c.ManualCerts {
			for _, d := range strings.Split(mc.Domains, ",") {
				if d = strings.TrimSpace(d); d != "" {
					if err := rt.tls.AddManualCert(d, mc.CertPem, mc.KeyPem); err != nil {
						log.Printf("[tls] 手动证书加载失败 %s: %v", d, err)
					} else {
						log.Printf("[tls] 手动证书已加载 %s", d)
					}
				}
			}
		}
	}
	rt.tls.Issuer = issuer
	rt.tls.StartRetryLoop() // 证书主动签发/重试循环（不依赖 TLS 握手触发）
	sy := syncer.New(cfg, *cfgPath, client, onConfig)
	pipeline.SetLogEmitter(sy.EnqueueLog)
	sy.Start()

	// 内建权威 DNS（:53 udp/tcp）：应答主控下发的智能 DNS 记录；失败仅记日志（端口占用不阻塞业务）
	if err := dnsserver.Start(); err != nil {
		log.Printf("[dns] 内建权威 DNS 启动失败: %v", err)
	} else {
		log.Printf("[dns] 内建权威 DNS 已监听 :53 (udp/tcp)")
	}

	// 首次拉配置
	onConfig(mustInitialConfig(client, cfg))

	handler := &proxyHandler{rt: rt}
	httpMux := rt.tls.ACMEHandler(handler)

	// 本地管理端点（127.0.0.1:8081）：健康检查 + 自升级
	go func() {
		mux := http.NewServeMux()
		mux.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
			w.Header().Set("Content-Type", "application/json")
			fmt.Fprintf(w, `{"status":"ok","version":%q}`, master.Version)
		})
		mux.HandleFunc("/admin/upgrade", func(w http.ResponseWriter, r *http.Request) {
			if r.Method != http.MethodPost {
				http.Error(w, "method not allowed", 405)
				return
			}
			var req struct {
				URL     string `json:"url"`
				Version string `json:"version"`
				SHA256  string `json:"sha256"`
			}
			if err := json.NewDecoder(io.LimitReader(r.Body, 1<<20)).Decode(&req); err != nil {
				http.Error(w, "bad request", 400)
				return
			}
			log.Printf("[upgrade] 收到升级指令: version=%s url=%s", req.Version, req.URL)
			w.Header().Set("Content-Type", "application/json")
			fmt.Fprintln(w, `{"status":"upgrading"}`)
			// 给响应刷出去的时间，然后升级（失败会带着旧二进制继续跑）
			go func() {
				time.Sleep(500 * time.Millisecond)
				if err := upgrade.Self(req.URL, req.Version, req.SHA256); err != nil {
					log.Printf("[upgrade] 升级失败: %v", err)
				}
			}()
		})
		ln, err := net.Listen("tcp", cfg.AdminHTTPAddr)
		if err != nil {
			log.Printf("[admin] 本地管理端口监听失败: %v", err)
			return
		}
		log.Printf("[admin] 本地管理端点 %s（/health /admin/upgrade）", cfg.AdminHTTPAddr)
		srv := &http.Server{Handler: mux, ReadHeaderTimeout: 5 * time.Second}
		srv.Serve(ln)
	}()

	// 缓存共享/L2 代理监听（:3128）：兄弟节点 P2P 缓存拉取 + L2 就近回源转发
	shareMux := http.NewServeMux()
	shareMux.HandleFunc("/__tnorv_cache", pipeline.ServeCacheShare)
	shareMux.Handle("/acme-challenge", rt.tls.ChallengeHandler()) // 兄弟节点 ACME 挑战查询（分线路 DNS 场景）
	shareMux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		// 其余路径按 L2 转发语义处理（X-Tnorv-L2 标记的回源请求）
		handler.ServeHTTP(w, r)
	})
	go func() {
		l2Srv := &http.Server{
			Handler:           shareMux,
			ReadHeaderTimeout: 5 * time.Second,
			IdleTimeout:       60 * time.Second,
		}
		ln2, err := net.Listen("tcp", ":3128")
		if err != nil {
			log.Printf("[cache-share] 3128 监听失败（不影响其它功能）: %v", err)
			return
		}
		log.Printf("[cache-share] 缓存共享服务监听 :3128")
		if err := l2Srv.Serve(ln2); err != nil {
			log.Printf("[cache-share] 服务退出: %v", err)
		}
	}()

	// HTTP 监听（80）
	addr := ":80"
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		log.Fatalf("监听 %s 失败（端口被占用？请停用 nginx 等）: %v", addr, err)
	}
	log.Printf("[node] tnorv-node %s 启动，监听 %s，主控 %s", master.Version, addr, cfg.MasterURL)

	httpSrv := &http.Server{
		Handler:           httpMux,
		ReadHeaderTimeout: 10 * time.Second,
		IdleTimeout:       120 * time.Second,
	}
	go func() {
		if err := httpSrv.Serve(ln); err != nil && err != http.ErrServerClosed {
			log.Printf("HTTP 服务退出: %v", err)
		}
	}()

	// HTTPS 监听动态管理（ACME 域名就绪时自动开 443）
	go func() {
		var httpsSrv *http.Server
		for {
			time.Sleep(5 * time.Second)
			tlsCfg := rt.tls.TLSConfig()
			if tlsCfg != nil && httpsSrv == nil {
				l, err := net.Listen("tcp", ":443")
				if err != nil {
					log.Printf("[tls] 监听 443 失败: %v", err)
					continue
				}
				httpsSrv = &http.Server{
					Handler:           handler,
					TLSConfig:         tlsCfg,
					ReadHeaderTimeout: 10 * time.Second,
					IdleTimeout:       120 * time.Second,
				}
				log.Printf("[tls] HTTPS 监听 :443 已开启")
				go func(s *http.Server, l net.Listener) {
					if err := s.ServeTLS(l, "", ""); err != nil && err != http.ErrServerClosed {
						log.Printf("HTTPS 服务退出: %v", err)
					}
				}(httpsSrv, l)
				// HTTP/3（QUIC over UDP 443）：与 HTTPS 共用同一 ACME 证书配置与请求管道
				go func(tlsC *tls.Config) {
					defer func() {
						if r := recover(); r != nil {
							log.Printf("[h3] 启动失败(降级为 HTTP/2): %v", r)
						}
					}()
					h3Srv := &http3.Server{
						Addr:      ":443",
						TLSConfig: tlsC.Clone(),
						Handler:   handler,
					}
					log.Printf("[h3] HTTP/3 (QUIC) 监听 :443/udp 已开启")
					if err := h3Srv.ListenAndServe(); err != nil {
						log.Printf("[h3] HTTP/3 服务退出(不影响 HTTP/2): %v", err)
					}
				}(tlsCfg)
			}
		}
	}()

	select {} // 常驻
}

// h3Selftest HTTP/3 环回自测：自签证书 + QUIC 127.0.0.1:19443 请求，验证节点构建的 QUIC 栈可用。
// 用法：tnorv-node --h3-selftest
func h3Selftest() {
	ca := &x509.Certificate{
		SerialNumber: big.NewInt(1),
		NotBefore:    time.Now(), NotAfter: time.Now().Add(time.Hour),
		IsCA: true, KeyUsage: x509.KeyUsageCertSign | x509.KeyUsageDigitalSignature,
		BasicConstraintsValid: true, DNSNames: []string{"localhost"},
		IPAddresses: []net.IP{net.ParseIP("127.0.0.1")},
	}
	key, _ := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	der, _ := x509.CreateCertificate(rand.Reader, ca, ca, &key.PublicKey, key)
	certPEM := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})
	keyDER, _ := x509.MarshalECPrivateKey(key)
	keyPEM := pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: keyDER})
	cert, _ := tls.X509KeyPair(certPEM, keyPEM)

	h3Srv := &http3.Server{
		Addr:      "127.0.0.1:19443",
		TLSConfig: &tls.Config{Certificates: []tls.Certificate{cert}, NextProtos: []string{"h3"}},
		Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			w.Header().Set("X-Test", "h3-ok")
			fmt.Fprint(w, "h3-selftest-pass")
		}),
	}
	go h3Srv.ListenAndServe()
	time.Sleep(500 * time.Millisecond)

	pool := x509.NewCertPool()
	pool.AddCert(ca)
	rt := &http3.Transport{TLSClientConfig: &tls.Config{RootCAs: pool, ServerName: "localhost", InsecureSkipVerify: true}} // 自签环回：关闭校验（仅 selftest 路径）
	defer rt.Close()
	client := &http.Client{Transport: rt, Timeout: 8 * time.Second}
	resp, err := client.Get("https://127.0.0.1:19443/selftest")
	if err != nil {
		fmt.Printf("H3-SELFTEST-FAIL: %v\n", err)
		os.Exit(1)
	}
	body, _ := io.ReadAll(resp.Body)
	fmt.Printf("H3-SELFTEST-PASS status=%d x-test=%s body=%s proto=%s\n", resp.StatusCode, resp.Header.Get("X-Test"), string(body), resp.Proto)
	os.Exit(0)
}

func mustInitialConfig(client *master.Client, cfg *config.NodeConfig) *pb.NodeConfig {
	// 启动首次拉取强制带版本 0：节点重启后路由表为空，若本地已保存的版本号与主控相同会拿到 changed=false
	// 导致路由表一直为空。首次必须全量拉取。
	v := cfg.ConfigVersion
	cfg.ConfigVersion = 0
	resp, err := client.GetConfig()
	cfg.ConfigVersion = v
	if err == nil && resp.Parsed != nil {
		cfg.ConfigVersion = resp.ConfigVersion
		_ = config.Save(defaultCfgPath, cfg)
		return resp.Parsed
	}
	log.Printf("[master] 首次拉取配置失败（将用空路由表启动，等待下轮同步）: %v", err)
	return &pb.NodeConfig{}
}

// execPurges 执行配置快照携带的缓存刷新指令通用设计。
// matchType=cc_unban 为 CC 解封指令：清除该 IP 的 CC 封禁状态，value=客户端 IP。
func execPurges(c *pb.NodeConfig) {
	for _, cmd := range c.Purges {
		if cmd.MatchType == "cc_unban" {
			pipeline.UnbanCc(cmd.Value)
			log.Printf("[cc] 解封指令 #%d ip=%s", cmd.Id, cmd.Value)
			continue
		}
		n := pipeline.PurgeCache(cmd.MatchType, cmd.Host, cmd.Value)
		log.Printf("[cache] 刷新指令 #%d type=%s host=%s value=%s 清除 %d 条", cmd.Id, cmd.MatchType, cmd.Host, cmd.Value, n)
	}
}
