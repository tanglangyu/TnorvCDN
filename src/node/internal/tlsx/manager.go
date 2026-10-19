// Package tlsx TLS/HTTPS 与 ACME 证书自动化：
//   - ACME HTTP-01 自动申请（Let's Encrypt），证书缓存于本地目录
//   - 签发前 DNS 预校验：域名必须解析（含 CNAME 链）到本节点公网 IP，否则跳过（避免无效签发尝试）
//   - 证书状态上报（签发成功/失败/待 DNS），主控展示给用户
//   - SNI 多证书动态路由（按配置热更新）
//   - 未配置证书的域名走 TLS 拒绝
package tlsx

import (
	"bytes"
	"context"
	"crypto"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"encoding/base64"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"net/http/httptest"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"sync"
	"time"

	"golang.org/x/crypto/acme"
	"golang.org/x/crypto/acme/autocert"

	"tnorv/node/pb"
)

// CertState 单域名证书签发状态（随日志/心跳通道上报主控）
type CertState struct {
	Domain   string
	Status   string // pending_dns / issuing / issued / failed
	Error    string
	ExpireAt time.Time
}

type Manager struct {
	mu        sync.RWMutex
	m         *autocert.Manager
	certs     map[string]*tls.Certificate // 手动/内置证书（SNI 路由）
	cacheDir  string
	email     string
	acmeHost  string // ACME 目录 URL（空 = Let's Encrypt 生产）
	enabled   bool
	publicIPs []string        // 本节点公网 IP（DNS 校验基准）
	allowed   map[string]bool // 当前允许 ACME 签发的域名集合

	// 状态上报：域名 → 最近状态；由 syncer 心跳批量取走
	stateMu sync.RWMutex
	states  map[string]*CertState
	stateCh chan *CertState

	siblingIPs       []string      // 同集群其它节点公网 IP（多节点 pending_dns 抑制）
	accountKey       crypto.Signer // 受控签发 ACME 账户私钥
	accountKeyMu     sync.Mutex
	activeChallenges []*pb.AcmeChallenge  // 主控下发的 HTTP-01 挑战（本节点监听应答）
	Issuer           *ControlledIssuer    // 受控签发器（main 注入）
	backup           *autocert.Manager    // 备用 CA manager（主 CA 限流/失败时切换）
	caFailed         bool                 // 主 CA 已被判定不可用（限流），切到备用
	seen             map[string]bool      // 已进入托管的域名（新域名主动触发签发）
	inflight         map[string]bool      // 在途签发去重（域名 → true）
	retryNotBefore   map[string]time.Time // CA 限流明确指定的最早重试时间
	lastAttempt      map[string]time.Time // 上次主动尝试时间（退避用）
	fails            map[string]int       // 连续失败/未解析次数（退避与放弃阈值）
}

func NewManager(cacheDir, email, acmeStaging string) *Manager {
	m := &Manager{
		certs:          make(map[string]*tls.Certificate),
		cacheDir:       cacheDir,
		email:          email,
		states:         make(map[string]*CertState),
		stateCh:        make(chan *CertState, 256),
		seen:           make(map[string]bool),
		inflight:       make(map[string]bool),
		retryNotBefore: make(map[string]time.Time),
		lastAttempt:    make(map[string]time.Time),
		fails:          make(map[string]int),
	}
	if acmeStaging == "staging" {
		m.acmeHost = autocert.DefaultACMEDirectory // staging 由 LE_STAGING 环境变量切换
	}
	return m
}

// SetPublicIPs 设置本节点公网 IP 列表（注册成功后由 main 注入，DNS 预校验用）
func (tm *Manager) SetPublicIPs(ips []string) {
	tm.mu.Lock()
	tm.publicIPs = ips
	tm.mu.Unlock()
}

// setState 记录域名证书状态（供心跳上报；同域名覆盖旧状态）
func (tm *Manager) setState(domain, status, errMsg string, expireAt time.Time) {
	tm.stateMu.Lock()
	tm.states[domain] = &CertState{Domain: domain, Status: status, Error: errMsg, ExpireAt: expireAt}
	switch status {
	case "issued":
		tm.fails[domain] = -1 // 成功：退出自动重试
	case "failed", "pending_dns":
		if tm.fails[domain] >= 0 {
			tm.fails[domain]++
		} else {
			tm.fails[domain] = 1
		}
	}
	tm.stateMu.Unlock()
	// 异步推送到 channel（非阻塞；消费端由 syncer 提供）
	select {
	case tm.stateCh <- tm.states[domain]:
	default:
	}
}

// DrainStates 取走全部未上报的证书状态
func (tm *Manager) DrainStates() []*CertState {
	tm.stateMu.Lock()
	defer tm.stateMu.Unlock()
	out := make([]*CertState, 0, len(tm.states))
	for _, s := range tm.states {
		out = append(out, s)
	}
	tm.states = make(map[string]*CertState)
	return out
}

// StateChan 供 syncer 订阅状态事件（可选）
func (tm *Manager) StateChan() <-chan *CertState { return tm.stateCh }

// resolvesToNode 校验域名 DNS 解析（含 CNAME 链）是否指向本节点公网 IP。
// 直接向域名权威/递归 DNS 发起解析请求；任何一步失败视为未解析。
func (tm *Manager) resolvesToNode(domain string) bool {
	tm.mu.RLock()
	ips := tm.publicIPs
	tm.mu.RUnlock()
	if len(ips) == 0 {
		return false // 不知道自己的公网 IP，无法校验，保守跳过
	}
	host := strings.TrimSuffix(strings.ToLower(domain), ".")
	// 最多追 5 层 CNAME
	for hop := 0; hop < 5; hop++ {
		cnames, err := net.LookupCNAME(host)
		if err == nil && cnames != "" && strings.TrimSuffix(strings.ToLower(cnames), ".") != host {
			host = strings.TrimSuffix(strings.ToLower(cnames), ".")
			continue
		}
		break
	}
	addrs, err := net.LookupIP(host)
	if err != nil {
		return false
	}
	nodeSet := make(map[string]bool, len(ips))
	for _, ip := range ips {
		nodeSet[strings.TrimSpace(ip)] = true
	}
	for _, a := range addrs {
		if nodeSet[a.String()] {
			return true
		}
	}
	return false
}

// SyncFromConfig 从节点配置同步需要证书的域名（HTTPS 端口 > 0 的网站）。
// 签发前逐域名做 DNS 预校验：未解析到本节点的域名跳过并标记 pending_dns。
func (tm *Manager) SyncFromConfig(cfg *pb.NodeConfig) {
	domains := make([]string, 0)
	for _, srv := range cfg.Servers {
		// sslMode=manual（手动上传证书）或 off（关闭 HTTPS）的站点不参与 ACME 自动签发
		if srv.SslMode == "manual" || srv.SslMode == "off" {
			continue
		}
		if len(srv.HttpsPorts) > 0 {
			for _, d := range srv.Domains {
				if d != "" && !hasWildcard(d) {
					domains = append(domains, d)
				}
			}
		}
	}

	tm.mu.Lock()
	defer tm.mu.Unlock()

	// ACME 账户邮箱（运营配置下发）：首次创建 manager 时生效；
	// 邮箱变更时重建 manager（ACME 账户按邮箱区分，换邮箱=换账户）
	cfgEmail := ""
	caDir := ""
	backupDir := ""
	backupEabKid := ""
	backupEabHmac := ""
	if cfg.Acme != nil {
		cfgEmail = cfg.Acme.Email
		switch cfg.Acme.Provider {
		case "letsencrypt", "":
			caDir = autocert.DefaultACMEDirectory
		case "letsencrypt-staging":
			caDir = autocert.DefaultACMEDirectory // staging 由 LE_STAGING 环境变量控制（简化）
		default: // google / zerossl / custom
			caDir = cfg.Acme.DirectoryUrl
		}
		backupDir = cfg.Acme.BackupDirectoryUrl
		backupEabKid = cfg.Acme.EabKid
		backupEabHmac = cfg.Acme.EabHmacKey
	}
	if len(domains) == 0 && len(tm.certs) == 0 {
		// 无 ACME 托管域名且无手动证书 → 关闭 TLS；仅有手动证书的站点也需开启 443
		tm.enabled = false
		return
	}
	tm.enabled = true

	if tm.m == nil || (cfgEmail != "" && cfgEmail != tm.email) || (caDir != "" && caDir != tm.acmeHost) {
		emailChanged := cfgEmail != "" && cfgEmail != tm.email && tm.email != ""
		if cfgEmail != "" {
			tm.email = cfgEmail
		}
		if caDir != "" {
			tm.acmeHost = caDir
		}
		// 邮箱变更 = 换 ACME 账户：必须清除旧账户私钥（acme_account+key 存在缓存目录里，
		// 不清则 autocert 复用旧账户，邮箱修改无效，限流也无法绕开）
		if emailChanged {
			if err := os.RemoveAll(tm.cacheDir); err == nil {
				os.MkdirAll(tm.cacheDir, 0o700)
				log.Printf("[tls] 邮箱变更，已清空证书缓存目录重建 ACME 账户")
			}
			tm.m = nil // 强制重建 manager
		}
		os.MkdirAll(tm.cacheDir, 0o700)
		tm.m = &autocert.Manager{
			Prompt:     autocert.AcceptTOS,
			HostPolicy: tm.hostPolicy, // 自定义策略：白名单 + DNS 预校验
			Cache:      autocert.DirCache(tm.cacheDir),
			Email:      tm.email,
		}
		// 自定义目录（非 LE 生产）注入 ACME client；备用 CA（EAB）需要时由 Client 构造
		if tm.acmeHost != "" && tm.acmeHost != autocert.DefaultACMEDirectory {
			tm.m.Client = &acme.Client{DirectoryURL: tm.acmeHost}
		}
		// 备用 CA manager（独立缓存目录，主 CA 失败时切换）。
		// Google Trust Services / ZeroSSL 需要 EAB：用预注册账户（KID 注入）绕过 autocert 的无 EAB 注册。
		if backupDir != "" {
			os.MkdirAll(tm.cacheDir+"-backup", 0o700)
			bm := &autocert.Manager{
				Prompt:     autocert.AcceptTOS,
				HostPolicy: tm.hostPolicy,
				Cache:      autocert.DirCache(tm.cacheDir + "-backup"),
				Email:      tm.email,
			}
			client := &acme.Client{DirectoryURL: backupDir}
			if backupEabKid != "" && backupEabHmac != "" {
				if key, kid, err := registerEabAccount(backupDir, backupEabKid, backupEabHmac, tm.email); err == nil {
					client = &acme.Client{DirectoryURL: backupDir, Key: key, KID: kid}
					log.Printf("[tls] 备用 CA 账户注册成功（EAB kid=%s）", kid)
				} else {
					log.Printf("[tls] 备用 CA EAB 预注册失败（将无 EAB 重试）: %v", err)
				}
			}
			bm.Client = client
			tm.backup = bm
			log.Printf("[tls] 备用 CA 已配置: %s", backupDir)
		}
		// 新账户/新 CA = 限流计数重置（retryNotBefore 是旧账户的限流窗口）
		tm.retryNotBefore = make(map[string]time.Time)
		// 立即启用 HTTP-01 挑战：不调用 HTTPHandler 时 autocert 只用 tls-alpn-01，
		// 而 TLS-ALPN 需要 443 上的动态监听就绪且挑战期 TLS 配置正确——启用 http-01 双通道最稳
		_ = tm.m.HTTPHandler(nil)
		log.Printf("[tls] ACME 已启用（CA %s，邮箱 %s），托管域名 %v", tm.acmeHost, tm.email, domains)
	}
	tm.allowed = make(map[string]bool, len(domains))
	for _, d := range domains {
		tm.allowed[strings.ToLower(d)] = true
	}

	// 新进入托管的域名（此前未见过）主动触发一次签发尝试，不依赖 TLS 握手
	for _, d := range domains {
		d = strings.ToLower(d)
		if !tm.seen[d] {
			tm.seen[d] = true
			if tm.fails[d] == 0 {
				go tm.ensureCertificate(d)
			}
		}
	}

	// 管理员主动续期：删除本地缓存证书，下次 TLS 握手/HTTP-01 触发重新签发
	for _, rn := range cfg.Renews {
		if rn == nil {
			continue
		}
		// 找到该站点所有 ACME 托管域名并清缓存
		for _, srv := range cfg.Servers {
			if srv.Id != rn.ServerId {
				continue
			}
			for _, d := range srv.Domains {
				d = strings.ToLower(strings.TrimSpace(d))
				if d == "" || hasWildcard(d) {
					continue
				}
				if dc, ok := tm.m.Cache.(interface {
					Delete(ctx context.Context, key string) error
				}); ok {
					if err := dc.Delete(context.Background(), d); err != nil {
						log.Printf("[tls] 续期：清除 %s 缓存失败: %v", d, err)
					} else {
						log.Printf("[tls] 续期：已清除 %s 证书缓存，将重新签发", d)
					}
				}
				// 续期也做 DNS 预校验：未解析直接落 pending_dns（并主动后台重试），避免卡在"签发中"
				go tm.ensureCertificate(d)
			}
		}
	}
}

// ensureCertificate 主动为域名申请/续期证书（不依赖 TLS 握手触发）：
// DNS 预校验通过 → 尝试签发（成功 issued / 失败 failed）；未解析 → pending_dns。
// 每域名同时只有一个在途尝试（in-flight 去重），失败后带退避的周期重试由重试循环驱动。
func (tm *Manager) ensureCertificate(domain string) {
	h := strings.ToLower(strings.TrimSpace(domain))
	if h == "" {
		return
	}
	tm.mu.RLock()
	allowed := tm.allowed[h]
	m := tm.m
	inflight := tm.inflight[h]
	tm.mu.RUnlock()
	if !allowed || m == nil || inflight {
		log.Printf("[tls] ensure %s 跳过（allowed=%v in-flight=%v）", h, allowed, inflight)
		return
	}
	log.Printf("[tls] ensure %s 开始签发尝试", h)
	if tm.Issuer != nil {
		// 受控签发：合并 SAN（同集群全部待签域名一张证书），避免 LE exact-set 5/7天配额
		tm.Issuer.Enqueue(h)
		return
	}
	tm.mu.Lock()
	if tm.inflight[h] { // 双检
		tm.mu.Unlock()
		return
	}
	tm.inflight[h] = true
	tm.mu.Unlock()
	defer func() {
		tm.mu.Lock()
		delete(tm.inflight, h)
		tm.mu.Unlock()
	}()

	if !tm.resolvesToNode(h) && !tm.resolvesToAnySibling(h) {
		tm.setState(h, "pending_dns", "域名未解析到本节点（请先配置 CNAME/A 记录）", time.Time{})
		return
	}
	tm.setState(h, "issuing", "", time.Time{})
	// 触发真实签发：伪造 ClientHello 走 autocert.GetCertificate（独立 goroutine + 总超时，防 LE 慢响应挂死）
	type certResult struct {
		c   *tls.Certificate
		err error
	}
	resCh := make(chan certResult, 1)
	go func() {
		c, err := m.GetCertificate(&tls.ClientHelloInfo{ServerName: h})
		resCh <- certResult{c, err}
	}()
	var c *tls.Certificate
	var err error
	select {
	case r := <-resCh:
		c, err = r.c, r.err
	case <-time.After(90 * time.Second):
		// 总超时：按用户节奏改为被动轮询 —— 订单可能仍在 LE 侧验证，每 15s 检查缓存里是否出现证书（最多再等 120s）
		log.Printf("[tls] ensure %s 签发响应慢，进入轮询等待（每 15s 检查一次，最多 120s）", h)
		for i := 0; i < 8; i++ {
			time.Sleep(15 * time.Second)
			if cc, e := m.Cache.Get(context.Background(), h); e == nil && len(cc) > 0 {
				if leaf, pe := x509.ParseCertificate(cc); pe == nil {
					tm.setState(h, "issued", "", leaf.NotAfter)
					log.Printf("[tls] ensure %s 轮询到证书签发成功", h)
					return
				}
			}
		}
		tm.setState(h, "failed", "签发超时（LE 验证/下载证书超过 90+120 秒），稍后自动重试", time.Time{})
		return
	}
	if (err != nil || c == nil) && isRateLimited(err) {
		// 主 CA 限流（如 LE 每域名/每账户限额）：自动切换备用 CA 重试一次
		tm.mu.RLock()
		bm := tm.backup
		tm.mu.RUnlock()
		if bm != nil {
			log.Printf("[tls] 主 CA 限流（%v），切换备用 CA 重试 %s", err, h)
			if c2, err2 := bm.GetCertificate(&tls.ClientHelloInfo{ServerName: h}); err2 == nil && c2 != nil {
				if len(c2.Certificate) > 0 {
					if leaf, e := x509.ParseCertificate(c2.Certificate[0]); e == nil {
						tm.setState(h, "issued", "", leaf.NotAfter)
						return
					}
				}
				tm.setState(h, "issued", "", time.Time{})
				return
			} else {
				tm.setState(h, "failed", fmt.Sprintf("备用 CA 签发失败: %v", err2), time.Time{})
				return
			}
		}
	}
	if err != nil || c == nil {
		msg := fmt.Sprintf("签发失败: %v", err)
		// CA 限流错误带 retry-after 时间：解析出来，重试循环直接等到该时间再试（不浪费尝试）
		if ra := parseRetryAfter(err); ra != nil {
			msg = fmt.Sprintf("CA 限流，%s 后可重试（%v）", ra.Format("15:04:05"), err)
			tm.mu.Lock()
			tm.retryNotBefore[h] = *ra
			tm.mu.Unlock()
		}
		tm.setState(h, "failed", msg, time.Time{})
		return
	}
	if len(c.Certificate) > 0 {
		if leaf, e := x509.ParseCertificate(c.Certificate[0]); e == nil {
			tm.setState(h, "issued", "", leaf.NotAfter)
			return
		}
	}
	tm.setState(h, "issued", "", time.Time{})
}

// SetChallenges 接收主控下发的 HTTP-01 挑战（受控签发，全节点监听应答）
func (tm *Manager) SetChallenges(chals []*pb.AcmeChallenge) {
	tm.mu.Lock()
	tm.activeChallenges = chals
	tm.mu.Unlock()
}

// hasActiveChallenge 查询某挑战路径是否在本节点生效中
func (tm *Manager) hasActiveChallenge(path string) bool {
	tm.mu.RLock()
	defer tm.mu.RUnlock()
	for _, c := range tm.activeChallenges {
		if c.Path == path {
			return true
		}
	}
	return false
}

// SetSiblingIPs 设置同集群兄弟节点公网 IP（配置同步时由 main 注入）
func (tm *Manager) SetSiblingIPs(ips []string) {
	tm.mu.Lock()
	tm.siblingIPs = ips
	tm.mu.Unlock()
}

// resolvesToAnySibling 检查域名是否解析到同集群任意兄弟节点
func (tm *Manager) resolvesToAnySibling(domain string) bool {
	tm.mu.RLock()
	sibs := tm.siblingIPs
	tm.mu.RUnlock()
	if len(sibs) == 0 {
		return false
	}
	host := strings.TrimSuffix(strings.ToLower(domain), ".")
	for hop := 0; hop < 5; hop++ {
		cnames, err := net.LookupCNAME(host)
		if err == nil && cnames != "" && strings.TrimSuffix(strings.ToLower(cnames), ".") != host {
			host = strings.TrimSuffix(strings.ToLower(cnames), ".")
			continue
		}
		break
	}
	addrs, err := net.LookupIP(host)
	if err != nil {
		return false
	}
	set := make(map[string]bool, len(sibs))
	for _, ip := range sibs {
		set[strings.TrimSpace(ip)] = true
	}
	for _, a := range addrs {
		if set[a.String()] {
			return true
		}
	}
	return false
}

// StartRetryLoop 启动证书签发重试循环：对 allowed 中尚未有有效证书的域名，
// 每 2 分钟（失败指数退避至 10 分钟上限）主动尝试，直到签发成功或域名被移除。
// 解决"签发依赖 TLS 握手触发，没人访问就永远卡住"的问题。
func (tm *Manager) StartRetryLoop() {
	go func() {
		for {
			// 失败/待签发域名周期重试；间隔随连续失败次数退避
			time.Sleep(2 * time.Minute)
			tm.mu.RLock()
			if !tm.enabled || tm.m == nil {
				tm.mu.RUnlock()
				continue
			}
			var candidates []string
			for d := range tm.allowed {
				if tm.fails[d] >= 0 && tm.fails[d] < 100 { // 100 次后不再自动重试（域名长期未解析）
					candidates = append(candidates, d)
				}
			}
			tm.mu.RUnlock()
			for _, d := range candidates {
				tm.mu.RLock()
				last, attempted := tm.lastAttempt[d]
				fails := tm.fails[d]
				tm.mu.RUnlock()
				// CA 明确限流未到期：直接跳过（不浪费尝试）
				tm.mu.RLock()
				nb, nbOk := tm.retryNotBefore[d]
				tm.mu.RUnlock()
				if nbOk && time.Now().Before(nb) {
					continue
				}
				// 退避：失败 0-1 次 2 分钟，之后每次翻倍，上限 10 分钟
				wait := 2 * time.Minute
				if fails > 1 {
					wait = time.Duration(fails) * 2 * time.Minute
					if wait > 10*time.Minute {
						wait = 10 * time.Minute
					}
				}
				if attempted && time.Since(last) < wait {
					continue
				}
				tm.mu.Lock()
				tm.lastAttempt[d] = time.Now()
				tm.mu.Unlock()
				tm.ensureCertificate(d)
			}
		}
	}()
}

// allowed 当前允许 ACME 签发的域名集合（SyncFromConfig 更新）
// hostPolicy 动态校验：域名必须在 allowed 集合内，且 DNS 解析到本节点
func (tm *Manager) hostPolicy(ctx context.Context, host string) error {
	tm.mu.RLock()
	allowed := tm.allowed
	tm.mu.RUnlock()

	h := strings.ToLower(host)
	if allowed == nil || !allowed[h] {
		return fmt.Errorf("acme/autocert: host %q not configured", host)
	}
	// DNS 预校验：域名必须解析到本集群任一节点（每节点独立签发自己的证书副本，
	// LE 的 TLS-ALPN 验证可能经分线路/多值 A 到达任意节点 —— 每个节点都有自己的 token 才能保证验证成功）
	if !tm.resolvesToNode(h) && !tm.resolvesToAnySibling(h) {
		tm.setState(h, "pending_dns", "域名未解析到本集群节点（请先配置 CNAME/A 记录），跳过证书签发", time.Time{})
		return fmt.Errorf("acme/autocert: host %q DNS not pointing to this cluster yet", host)
	}
	tm.setState(h, "issuing", "", time.Time{})
	return nil
}

// registerEabAccount 为备用 CA（需要 EAB 的 CA，如 Google Trust Services）预注册 ACME 账户。
// 返回账户私钥与 KID，注入 autocert Manager 的 Client 后 autocert 跳过自身注册流程。
func registerEabAccount(directoryURL, eabKid, eabHmacKey, email string) (crypto.Signer, acme.KeyID, error) {
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return nil, "", err
	}
	hmacKey, err := base64.RawURLEncoding.DecodeString(strings.TrimRight(eabHmacKey, "="))
	if err != nil {
		return nil, "", fmt.Errorf("EAB HMAC 解码失败: %w", err)
	}
	client := &acme.Client{Key: key, DirectoryURL: directoryURL}
	contact := []string{}
	if email != "" {
		contact = []string{"mailto:" + email}
	}
	acct, err := client.Register(context.Background(), &acme.Account{
		Contact: contact,
		ExternalAccountBinding: &acme.ExternalAccountBinding{
			KID: eabKid,
			Key: hmacKey,
		},
	}, acme.AcceptTOS)
	if err != nil {
		return nil, "", err
	}
	return key, acme.KeyID(acct.URI), nil
}

// isTransientHandshakeErr 判断握手错误是否为瞬态（签发进行中/挑战验证中），不应标记 failed
func isTransientHandshakeErr(err error) bool {
	if err == nil {
		return false
	}
	msg := err.Error()
	for _, kw := range []string{"no token cert", "not configured", "resolves to sibling", "DNS not pointing"} {
		if strings.Contains(msg, kw) {
			return true
		}
	}
	return false
}

// parseRetryAfter 从 CA 限流错误中解析 "retry after 2006-01-02 15:04:05 UTC" 时间（LE 格式）
func parseRetryAfter(err error) *time.Time {
	if err == nil {
		return nil
	}
	m := regexp.MustCompile(`retry after (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) UTC`).FindStringSubmatch(err.Error())
	if m == nil {
		return nil
	}
	t, e := time.Parse("2006-01-02 15:04:05", m[1])
	if e != nil {
		return nil
	}
	return &t
}

// isRateLimited 识别 CA 限流类错误（LE: "too many failed authorizations"/"rate limited"/429）
func isRateLimited(err error) bool {
	if err == nil {
		return false
	}
	msg := strings.ToLower(err.Error())
	return strings.Contains(msg, "too many") || strings.Contains(msg, "rate limit") ||
		strings.Contains(msg, "ratelimit") || strings.Contains(msg, "429")
}

func hasWildcard(d string) bool {
	for i := 0; i+1 < len(d); i++ {
		if d[i] == '*' && d[i+1] == '.' {
			return true
		}
	}
	return false
}

// TLSConfig 返回用于 HTTPS 监听的 tls.Config（SNI 动态证书）
func (tm *Manager) TLSConfig() *tls.Config {
	tm.mu.RLock()
	defer tm.mu.RUnlock()
	if !tm.enabled {
		return nil
	}
	// 仅手动证书（无 ACME 域名）：构建基础 TLS 配置，SNI 走手动证书表
	if tm.m == nil {
		if len(tm.certs) == 0 {
			return nil
		}
		cfg := &tls.Config{MinVersion: tls.VersionTLS12}
		cfg.GetCertificate = tm.getCertificate
		cfg.NextProtos = append(cfg.NextProtos, "h2", "http/1.1")
		return cfg
	}
	cfg := tm.m.TLSConfig()
	cfg.GetCertificate = tm.getCertificate
	cfg.NextProtos = append(cfg.NextProtos, "h2", "http/1.1")
	return cfg
}

func (tm *Manager) getCertificate(hello *tls.ClientHelloInfo) (*tls.Certificate, error) {
	{
		tm.mu.RLock()
		cert, ok := tm.certs[hello.ServerName]
		tm.mu.RUnlock()
		if ok {
			return cert, nil
		}
		// 其次 ACME
		if tm.m != nil {
			c, err := tm.m.GetCertificate(hello)
			if err == nil && c != nil {
				// 记录签发成功状态（证书叶子到期时间）
				if len(c.Certificate) > 0 && hello.ServerName != "" {
					if leaf, e := x509.ParseCertificate(c.Certificate[0]); e == nil {
						tm.setState(hello.ServerName, "issued", "", leaf.NotAfter)
					}
				}
				return c, nil
			}
			// TLS-ALPN 挑战握手 / 瞬态错误：不上报 failed（签发进行中的正常现象，避免状态闪烁）
			isChallenge := len(hello.SupportedProtos) == 1 && hello.SupportedProtos[0] == acme.ALPNProto
			if !isChallenge && hello.ServerName != "" && !isTransientHandshakeErr(err) {
				tm.setState(hello.ServerName, "failed", err.Error(), time.Time{})
			}
			return c, err
		}
		return nil, fmt.Errorf("no certificate for %s", hello.ServerName)
	}
}

// ACMEHandler 返回 ACME HTTP-01 挑战处理器（挂在 80 端口最前面）。
// 多节点 + 分线路 DNS 场景：LE 的验证可能到达任意节点；本地无 token 时转发给兄弟节点查询
// （兄弟节点 3128 缓存共享端口上暴露 /acme-challenge?token=... 响应挑战值）。
func (tm *Manager) ACMEHandler(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		tm.mu.RLock()
		m := tm.m
		sibs := tm.siblingIPs
		controlled := tm.activeChallenges
		tm.mu.RUnlock()
		// 受控签发挑战（主控下发，全节点统一应答）：优先级最高
		if strings.HasPrefix(r.URL.Path, "/.well-known/acme-challenge/") {
			for _, c := range controlled {
				if c.Path == r.URL.Path && c.KeyAuth != "" {
					w.Header().Set("Content-Type", "text/plain")
					w.WriteHeader(200)
					w.Write([]byte(c.KeyAuth))
					return
				}
			}
		}
		if m != nil {
			if len(r.URL.Path) > len("/.well-known/acme-challenge/") &&
				r.URL.Path[:len("/.well-known/acme-challenge/")] == "/.well-known/acme-challenge/" {
				// 先尝试本地挑战 token（200 才返回；404 = token 不在本节点 → 继续尝试兄弟转发）
				h := m.HTTPHandler(nil)
				rec := &captureWriter{header: http.Header{}}
				h.ServeHTTP(rec, r)
				if rec.code == 200 {
					for k, vs := range rec.header {
						for _, v := range vs {
							w.Header().Add(k, v)
						}
					}
					w.WriteHeader(200)
					w.Write(rec.body.Bytes())
					return
				}
				// 本地无 token：尝试兄弟节点（挑战可能在它们那里进行中）
				client := &http.Client{Timeout: 3 * time.Second}
				for _, ip := range sibs {
					u := "http://" + ip + ":3128/acme-challenge?path=" + url.QueryEscape(r.URL.Path)
					if resp, err := client.Get(u); err == nil {
						body, _ := io.ReadAll(resp.Body)
						resp.Body.Close()
						if resp.StatusCode == 200 && len(body) > 0 {
							w.Header().Set("Content-Type", "text/plain")
							w.Write(body)
							return
						}
					}
				}
				w.WriteHeader(rec.code)
				w.Write(rec.body.Bytes())
				return
			}
		}
		next.ServeHTTP(w, r)
	})
}

// ChallengeHandler 供 3128 缓存共享服务挂载：兄弟节点查询本节点持有的 HTTP-01 挑战响应。
// 返回 200 + 挑战 keyauth（若本地该挑战在途）。
func (tm *Manager) ChallengeHandler() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		path := r.URL.Query().Get("path")
		if path == "" || !strings.HasPrefix(path, "/.well-known/acme-challenge/") {
			http.Error(w, "bad path", 400)
			return
		}
		tm.mu.RLock()
		m := tm.m
		tm.mu.RUnlock()
		if m == nil {
			http.Error(w, "no acme", 404)
			return
		}
		// 借用 autocert 的 HTTPHandler 本地响应（它内部持有 httpTokens）
		h := m.HTTPHandler(nil)
		rec := &captureWriter{header: http.Header{}}
		req := httptest.NewRequest("GET", path, nil)
		h.ServeHTTP(rec, req)
		if rec.code == 200 {
			w.Write(rec.body.Bytes())
			return
		}
		http.Error(w, "not found", 404)
	})
}

// captureWriter 捕获 HTTP 响应（内部转发用）
type captureWriter struct {
	header http.Header
	body   bytes.Buffer
	code   int
}

func (c *captureWriter) Header() http.Header { return c.header }
func (c *captureWriter) Write(b []byte) (int, error) {
	if c.code == 0 {
		c.code = 200
	}
	return c.body.Write(b)
}
func (c *captureWriter) WriteHeader(code int) { c.code = code }

// AddManualCert 添加手动上传证书（PEM 对）
func (tm *Manager) AddManualCert(domain, certPEM, keyPEM string) error {
	cert, err := tls.X509KeyPair([]byte(certPEM), []byte(keyPEM))
	if err != nil {
		return err
	}
	tm.mu.Lock()
	tm.certs[domain] = &cert
	tm.mu.Unlock()
	return nil
}

var _ = filepath.Join
