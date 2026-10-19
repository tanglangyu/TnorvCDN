// Package tlsx 受控签发：单证书多节点共享。
// 流程：执行节点向 LE 发起订单 → 生成 HTTP-01 挑战 → 经主控下发给全部节点监听应答
// → 节点确认配置已收到挑战 → 主控等待 30s 后接受挑战 → 轮询验证结果 → 拿到证书上报主控
// → 主控存库并随配置下发全部节点（SNI 统一使用同一份证书）。
// 挑战路径下发后 10 分钟自动失效（主控 ChallengeRegistry TTL），不影响用户实际路径业务。
package tlsx

import (
	"bytes"
	"context"
	"crypto"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"log"
	"net/http"
	"os"
	"regexp"
	"strings"
	"sync"
	"time"

	"golang.org/x/crypto/acme"
)

// ControlledIssuer 受控签发器（每个节点一个）
type ControlledIssuer struct {
	tm        *Manager
	masterURL string
	nodeToken string
	http      *http.Client

	inflightMu sync.Mutex
	inflight   map[string]bool

	// 合并签发队列：per-domain 请求先进队列，由批处理 goroutine 定期合并为一次多 SAN 订单
	queueMu    sync.Mutex
	queue      map[string]time.Time // domain -> 入队时间
	batchTimer *time.Timer

	currentEmail string
	emailMu      sync.Mutex
}

func NewControlledIssuer(tm *Manager, masterURL, nodeToken string) *ControlledIssuer {
	return &ControlledIssuer{
		tm:        tm,
		masterURL: strings.TrimRight(masterURL, "/"),
		nodeToken: nodeToken,
		http:      &http.Client{Timeout: 30 * time.Second},
		inflight:  make(map[string]bool),
	}
}

// post 向主控上报
func (ci *ControlledIssuer) post(path string, body any) error {
	b, _ := json.Marshal(body)
	resp, err := ci.http.Post(ci.masterURL+"/grpc"+path, "application/json", bytes.NewReader(b))
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != 200 {
		return fmt.Errorf("master %s: HTTP %d", path, resp.StatusCode)
	}
	return nil
}

// IssueAll 多域名合并 SAN 签发：同集群全部待签/待续域名合成一张证书（一个"标识符集合"）。
// 好处：① 全集群一张证书（满足"不允许每节点不同证书"）② LE 对"exact set"的 5 张/7 天配额
// 变为"全集群 5 次全量签发/周"，批量新增域名也不会撞线 ③ 挑战一次性下发全节点应答。
// 成功后逐域名上报主控（cert-issued），失败上报 cert-failed。
func (ci *ControlledIssuer) IssueAll(domains []string) {
	var list []string
	for _, d := range domains {
		d = strings.ToLower(strings.TrimSpace(d))
		if d != "" && !strings.Contains(d, "*") {
			list = append(list, d)
		}
	}
	if len(list) == 0 {
		return
	}
	// 过滤已在途的
	ci.inflightMu.Lock()
	var todo []string
	for _, d := range list {
		if !ci.inflight[d] {
			todo = append(todo, d)
			ci.inflight[d] = true
		}
	}
	for _, d := range todo {
		ci.tm.setState(d, "issuing", "", time.Time{})
	}
	ci.inflightMu.Unlock()
	if len(todo) == 0 {
		return // 全部已在途（去重过滤后为空），避免空订单请求 CA
	}
	defer func() {
		ci.inflightMu.Lock()
		for _, d := range todo {
			delete(ci.inflight, d)
		}
		ci.inflightMu.Unlock()
	}()

	log.Printf("[tls] 受控签发（合并 SAN %d 域名）开始: %v", len(todo), todo)
	err := ci.issueMulti(todo)
	// 无效域名（无公共后缀/被 CA 政策拒绝）会拖垮整个合并订单：解析被拒域名，剔除后重试
	for attempt := 0; err != nil && attempt < 3 && strings.Contains(err.Error(), "rejectedIdentifier"); attempt++ {
		re := regexp.MustCompile(`Cannot issue for "([^"]+)"`)
		var bad []string
		for _, m := range re.FindAllStringSubmatch(err.Error(), -1) {
			bad = append(bad, strings.ToLower(m[1]))
		}
		if len(bad) == 0 {
			break
		}
		var kept []string
		for _, d := range todo {
			blocked := false
			for _, b := range bad {
				if d == b {
					blocked = true
					break
				}
			}
			if !blocked {
				kept = append(kept, d)
			} else {
				ci.tm.setState(d, "failed", "域名无法签发（CA 政策拒绝：非公共后缀/保留域名）", time.Time{})
				log.Printf("[tls] 域名 %s 被 CA 拒绝，从合并签发中剔除", d)
			}
		}
		if len(kept) == 0 {
			break
		}
		todo = kept
		log.Printf("[tls] 剔除无效域名后重试合并签发: %v", todo)
		err = ci.issueMulti(todo)
	}
	if err != nil {
		log.Printf("[tls] 受控签发（合并）失败: %v", err)
		msg := fmt.Sprintf("签发失败: %v", err)
		if ra := parseRetryAfter(err); ra != nil {
			msg = fmt.Sprintf("CA 限流，%s 后可重试（%v）", ra.Format("15:04:05"), err)
			// 对参与签发的所有域名设置统一退避
			ci.tm.mu.Lock()
			for _, d := range todo {
				ci.tm.retryNotBefore[d] = *ra
			}
			ci.tm.mu.Unlock()
		}
		for _, d := range todo {
			ci.tm.setState(d, "failed", msg, time.Time{})
			_ = ci.post("/cert-failed", map[string]string{"nodeToken": ci.nodeToken, "domain": d, "error": msg})
		}
		return
	}
	log.Printf("[tls] 受控签发（合并）成功: %d 个域名", len(todo))
}

// issueMulti 多域名合并订单：授权→挑战→全节点应答→验证→一张多 SAN 证书
func (ci *ControlledIssuer) issueMulti(domains []string) error {
	accountKey, err := ci.loadOrCreateAccountKey()
	if err != nil {
		return fmt.Errorf("账户密钥: %w", err)
	}
	client := &acme.Client{Key: accountKey, DirectoryURL: ci.tm.acmeHost}

	contact := []string{}
	if ci.tm.email != "" {
		contact = []string{"mailto:" + ci.tm.email}
	}
	if _, err := client.Register(context.Background(), &acme.Account{Contact: contact}, acme.AcceptTOS); err != nil {
		log.Printf("[tls] 账户注册（复用已注册账户）: %v", err)
	}

	ids := make([]acme.AuthzID, 0, len(domains))
	for _, d := range domains {
		ids = append(ids, acme.AuthzID{Type: "dns", Value: d})
	}
	ctxOrder, cancelOrder := context.WithTimeout(context.Background(), 2*time.Minute)
	defer cancelOrder()
	order, err := client.AuthorizeOrder(ctxOrder, ids)
	if err != nil {
		return fmt.Errorf("创建订单: %w", err)
	}

	// 每域名取 http-01 挑战，全部经主控下发
	type chal struct {
		domain string
		ch     *acme.Challenge
		path   string
	}
	var chals []chal
	for _, zurl := range order.AuthzURLs {
		z, err := client.GetAuthorization(ctxOrder, zurl)
		if err != nil {
			return fmt.Errorf("授权查询: %w", err)
		}
		if z.Status != acme.StatusPending {
			continue
		}
		for _, c := range z.Challenges {
			if c.Type == "http-01" {
				path := client.HTTP01ChallengePath(c.Token)
				keyAuth, err := client.HTTP01ChallengeResponse(c.Token)
				if err != nil {
					return fmt.Errorf("生成 keyAuth: %w", err)
				}
				chals = append(chals, chal{domain: z.Identifier.Value, ch: c, path: path})
				if err := ci.post("/challenge-register", map[string]string{"nodeToken": ci.nodeToken, "path": path, "keyAuth": keyAuth}); err != nil {
					return fmt.Errorf("挑战下发: %w", err)
				}
				break
			}
		}
	}
	log.Printf("[tls] 受控签发（合并）已下发 %d 个挑战", len(chals))

	// 等全部就绪（各节点回执 challenge-ready），最多 40s 兜底
	if len(chals) > 0 {
		ci.waitAllNodesReady(chals[0].path)
	}

	// 接受全部挑战
	for _, c := range chals {
		if _, err := client.Accept(context.Background(), c.ch); err != nil {
			return fmt.Errorf("接受挑战 %s: %w", c.domain, err)
		}
	}

	// 轮询全部授权 valid（15s 间隔，最多 5 分钟）
	authorized := make(map[string]bool)
	for i := 0; i < 20 && len(authorized) < len(chals); i++ {
		time.Sleep(15 * time.Second)
		for _, c := range chals {
			if authorized[c.domain] {
				continue
			}
			z, err := client.GetAuthorization(context.Background(), c.ch.URI)
			if err != nil {
				continue
			}
			if z.Status == acme.StatusValid {
				authorized[c.domain] = true
			} else if z.Status != acme.StatusPending && i > 12 {
				return fmt.Errorf("验证失败 %s: %v", c.domain, z.Status)
			}
		}
	}
	if len(authorized) < len(chals) {
		var missing []string
		for _, c := range chals {
			if !authorized[c.domain] {
				missing = append(missing, c.domain)
			}
		}
		return fmt.Errorf("验证超时: %v", missing)
	}

	// CSR（SAN 含全部域名）
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return fmt.Errorf("生成私钥: %w", err)
	}
	csr, err := x509.CreateCertificateRequest(rand.Reader, &x509.CertificateRequest{DNSNames: domains}, key)
	if err != nil {
		return fmt.Errorf("生成 CSR: %w", err)
	}
	derChain, _, err := client.CreateOrderCert(context.Background(), order.FinalizeURL, csr, true)
	if err != nil {
		for _, c := range chals {
			_ = ci.post("/challenge-clear", map[string]string{"nodeToken": ci.nodeToken, "path": c.path})
		}
		return fmt.Errorf("下载证书: %w", err)
	}

	var certBuf bytes.Buffer
	for _, der := range derChain {
		pem.Encode(&certBuf, &pem.Block{Type: "CERTIFICATE", Bytes: der})
	}
	var keyBuf bytes.Buffer
	kder, err := x509.MarshalECPrivateKey(key)
	if err != nil {
		return fmt.Errorf("编码私钥: %w", err)
	}
	pem.Encode(&keyBuf, &pem.Block{Type: "EC PRIVATE KEY", Bytes: kder})
	certPem := certBuf.String()
	keyPem := keyBuf.String()

	// 逐域名上报（主控按域名匹配站点置 issued）
	exp := time.Time{}
	for _, der := range derChain {
		if c, e := x509.ParseCertificate(der); e == nil && !c.IsCA {
			exp = c.NotAfter
			break
		}
	}
	for _, d := range domains {
		if err := ci.post("/cert-issued", map[string]string{"nodeToken": ci.nodeToken, "domain": d, "certPem": certPem, "keyPem": keyPem}); err != nil {
			log.Printf("[tls] 证书上报 %s 失败: %v", d, err)
		}
		if err := ci.tm.AddManualCert(d, certPem, keyPem); err != nil {
			log.Printf("[tls] 本地挂载 %s 失败: %v", d, err)
		}
		ci.tm.setState(d, "issued", "", exp)
	}
	for _, c := range chals {
		_ = ci.post("/challenge-clear", map[string]string{"nodeToken": ci.nodeToken, "path": c.path})
	}
	return nil
}

// Issue 为域名执行受控签发（阻塞；含挑战下发/确认/验证/取证书全流程）。
// 成功后证书上报主控（主控存库并下发全部节点）并本地挂 SNI；失败上报 cert-failed。
func (ci *ControlledIssuer) Issue(domain string) {
	h := strings.ToLower(strings.TrimSpace(domain))
	if h == "" {
		return
	}
	ci.inflightMu.Lock()
	if ci.inflight[h] {
		ci.inflightMu.Unlock()
		return
	}
	ci.inflight[h] = true
	ci.inflightMu.Unlock()
	defer func() {
		ci.inflightMu.Lock()
		delete(ci.inflight, h)
		ci.inflightMu.Unlock()
	}()

	ci.tm.setState(h, "issuing", "", time.Time{})
	defer func() {
		if r := recover(); r != nil {
			log.Printf("[tls] 受控签发 %s panic: %v", h, r)
			ci.tm.setState(h, "failed", fmt.Sprintf("签发异常: %v", r), time.Time{})
		}
	}()
	log.Printf("[tls] 受控签发 %s 开始流程", h)
	err := ci.issue(h)
	log.Printf("[tls] 受控签发 %s 流程结束（err=%v）", h, err)
	if err != nil {
		msg := fmt.Sprintf("签发失败: %v", err)
		// CA 限流错误带 retry-after：解析出来精确设置重试时间（重试循环不浪费尝试）
		if ra := parseRetryAfter(err); ra != nil {
			msg = fmt.Sprintf("CA 限流，%s 后可重试（%v）", ra.Format("15:04:05"), err)
			ci.tm.mu.Lock()
			ci.tm.retryNotBefore[h] = *ra
			ci.tm.mu.Unlock()
		}
		ci.tm.setState(h, "failed", msg, time.Time{})
		_ = ci.post("/cert-failed", map[string]string{"nodeToken": ci.nodeToken, "domain": h, "error": msg})
		return
	}
	log.Printf("[tls] 受控签发 %s 成功", h)
}

func (ci *ControlledIssuer) issue(domain string) error {
	// 1) 账户 key
	accountKey, err := ci.loadOrCreateAccountKey()
	if err != nil {
		return fmt.Errorf("账户密钥: %w", err)
	}
	client := &acme.Client{Key: accountKey, DirectoryURL: ci.tm.acmeHost}

	// 2) 注册账户（已注册则复用）
	contact := []string{}
	if ci.tm.email != "" {
		contact = []string{"mailto:" + ci.tm.email}
	}
	if _, err := client.Register(context.Background(), &acme.Account{Contact: contact}, acme.AcceptTOS); err != nil {
		log.Printf("[tls] 账户注册（复用已注册账户）: %v", err)
	}

	// 3) 订单 + 授权
	ctxOrder, cancelOrder := context.WithTimeout(context.Background(), 2*time.Minute)
	defer cancelOrder()
	order, err := client.AuthorizeOrder(ctxOrder, []acme.AuthzID{{Type: "dns", Value: domain}})
	if err != nil {
		return fmt.Errorf("创建订单: %w", err)
	}

	// 4) 取 http-01 挑战（文件验证）
	var httpChal *acme.Challenge
	for _, zurl := range order.AuthzURLs {
		z, err := client.GetAuthorization(ctxOrder, zurl)
		if err != nil {
			return fmt.Errorf("授权查询: %w", err)
		}
		if z.Status != acme.StatusPending {
			continue
		}
		for _, c := range z.Challenges {
			if c.Type == "http-01" {
				httpChal = c
				break
			}
		}
		if httpChal != nil {
			break
		}
	}
	if httpChal == nil {
		return fmt.Errorf("授权不含 http-01 挑战")
	}
	path := client.HTTP01ChallengePath(httpChal.Token)
	keyAuth, err := client.HTTP01ChallengeResponse(httpChal.Token)
	if err != nil {
		return fmt.Errorf("生成 keyAuth: %w", err)
	}

	// 5) 经主控下发挑战给全部节点（主控侧 10 分钟 TTL 自动失效）
	if err := ci.post("/challenge-register", map[string]string{"nodeToken": ci.nodeToken, "path": path, "keyAuth": keyAuth}); err != nil {
		return fmt.Errorf("挑战下发: %w", err)
	}
	log.Printf("[tls] 受控签发 %s 挑战已下发: %s", domain, path)

	// 6) 确认本节点配置已收到挑战（= 主控下发周期已跑 → 全部节点也会收到）
	confirmed := false
	for i := 0; i < 6; i++ { // 最多 60s
		time.Sleep(10 * time.Second)
		if ci.tm.hasActiveChallenge(path) {
			confirmed = true
			break
		}
	}
	if !confirmed {
		log.Printf("[tls] 受控签发 %s 挑战确认超时（继续尝试验证）", domain)
	}

	// 7) 等待全部在线节点确认监听就绪（每节点收到挑战后回执 challenge-ready）；
	//    主控侧 30s 缓冲后验证（用户要求）。就绪查询最多等 40s，超时兜底继续（任意节点可应答即可）。
	ci.waitAllNodesReady(path)

	if _, err := client.Accept(context.Background(), httpChal); err != nil {
		return fmt.Errorf("接受挑战: %w", err)
	}

	// 8) 轮询授权直到 valid（15s 间隔，最多 5 分钟）
	authorized := false
	var failMsg string
	for i := 0; i < 20; i++ {
		time.Sleep(15 * time.Second)
		z, err := client.GetAuthorization(context.Background(), httpChal.URI)
		if err != nil {
			return fmt.Errorf("验证状态查询: %w", err)
		}
		switch z.Status {
		case acme.StatusValid:
			authorized = true
		case acme.StatusPending:
			continue
		default:
			failMsg = fmt.Sprintf("验证失败: %v", z.Status)
			for _, c := range z.Challenges {
				if c.Error != nil {
					failMsg = fmt.Sprintf("验证失败: %v", c.Error)
				}
			}
		}
		if authorized {
			break
		}
	}
	if !authorized {
		_ = ci.post("/challenge-clear", map[string]string{"nodeToken": ci.nodeToken, "path": path})
		if failMsg == "" {
			failMsg = "验证超时（5 分钟）"
		}
		return fmt.Errorf("%s", failMsg)
	}

	// 9) CSR + 取证书
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return fmt.Errorf("生成私钥: %w", err)
	}
	csr, err := x509.CreateCertificateRequest(rand.Reader, &x509.CertificateRequest{DNSNames: []string{domain}}, key)
	if err != nil {
		return fmt.Errorf("生成 CSR: %w", err)
	}
	derChain, _, err := client.CreateOrderCert(context.Background(), order.FinalizeURL, csr, true)
	if err != nil {
		_ = ci.post("/challenge-clear", map[string]string{"nodeToken": ci.nodeToken, "path": path})
		return fmt.Errorf("下载证书: %w", err)
	}

	// 10) 编码 PEM
	var certBuf bytes.Buffer
	for _, der := range derChain {
		pem.Encode(&certBuf, &pem.Block{Type: "CERTIFICATE", Bytes: der})
	}
	var keyBuf bytes.Buffer
	kder, err := x509.MarshalECPrivateKey(key)
	if err != nil {
		return fmt.Errorf("编码私钥: %w", err)
	}
	pem.Encode(&keyBuf, &pem.Block{Type: "EC PRIVATE KEY", Bytes: kder})
	certPem := certBuf.String()
	keyPem := keyBuf.String()

	// 11) 上报主控（存库 + manualCerts 下发全部节点 SNI）
	if err := ci.post("/cert-issued", map[string]string{"nodeToken": ci.nodeToken, "domain": domain, "certPem": certPem, "keyPem": keyPem}); err != nil {
		return fmt.Errorf("证书上报: %w", err)
	}

	// 12) 清除挑战（早于 10 分钟 TTL，不占用用户路径）
	_ = ci.post("/challenge-clear", map[string]string{"nodeToken": ci.nodeToken, "path": path})

	// 13) 本地挂 SNI + 状态
	if err := ci.tm.AddManualCert(domain, certPem, keyPem); err != nil {
		log.Printf("[tls] 受控签发 %s 本地挂载失败: %v", domain, err)
	}
	exp := time.Time{}
	for _, der := range derChain {
		if c, e := x509.ParseCertificate(der); e == nil && !c.IsCA {
			exp = c.NotAfter
			break
		}
	}
	ci.tm.setState(domain, "issued", "", exp)
	return nil
}

// OnEmailChanged 检测运营邮箱变更：重置账户（新邮箱注册新 ACME 账户）。
// 以持久化账户文件旁的 meta 记录为准 —— 节点重启后也能正确检测（内存态会被首次设置跳过的坑已修复）。
func (ci *ControlledIssuer) OnEmailChanged(email string) {
	if email == "" {
		return
	}
	ci.emailMu.Lock()
	defer ci.emailMu.Unlock()
	metaPath := ci.tm.cacheDir + "/controlled-account.meta"
	if data, err := os.ReadFile(metaPath); err == nil && strings.TrimSpace(string(data)) == email {
		ci.currentEmail = email // 与持久化记录一致：无变更
		return
	}
	// 邮箱与持久化记录不同（或无记录）：重置账户，用新邮箱注册
	ci.currentEmail = email
	ci.tm.email = email
	ci.ResetAccount()
	_ = os.MkdirAll(ci.tm.cacheDir, 0o700)
	_ = os.WriteFile(metaPath, []byte(email), 0o600)
	log.Printf("[tls] 受控签发账户邮箱更新为 %s", email)
}

// Enqueue 域名加入合并签发队列（2 秒窗口收集，合并为一次多 SAN 订单）
func (ci *ControlledIssuer) Enqueue(domain string) {
	h := strings.ToLower(strings.TrimSpace(domain))
	if h == "" {
		return
	}
	ci.queueMu.Lock()
	if ci.queue == nil {
		ci.queue = make(map[string]time.Time)
	}
	ci.queue[h] = time.Now()
	if ci.batchTimer == nil {
		ci.batchTimer = time.AfterFunc(2*time.Second, ci.drainQueue)
	}
	ci.queueMu.Unlock()
}

// drainQueue 取出队列全部域名，一次性合并签发
func (ci *ControlledIssuer) drainQueue() {
	ci.queueMu.Lock()
	queue := ci.queue
	ci.queue = make(map[string]time.Time)
	ci.batchTimer = nil
	ci.queueMu.Unlock()
	if len(queue) == 0 {
		return
	}
	var domains []string
	for d := range queue {
		domains = append(domains, d)
	}
	// 过滤限流未到期的域名
	ci.tm.mu.RLock()
	var goList []string
	for _, d := range domains {
		if nb, ok := ci.tm.retryNotBefore[d]; !ok || !time.Now().Before(nb) {
			goList = append(goList, d)
		} else {
			log.Printf("[tls] %s 限流未到期（%s），跳过本轮", d, nb.Format("15:04:05"))
		}
	}
	ci.tm.mu.RUnlock()
	if len(goList) == 0 {
		return
	}
	go func() {
		defer func() {
			if r := recover(); r != nil {
				log.Printf("[tls] 合并签发 panic: %v", r)
			}
		}()
		ci.IssueAll(goList)
	}()
}

// ChallengeReady 向主控上报"本节点已收到挑战并在监听该路径"
func (ci *ControlledIssuer) ChallengeReady(path string) {
	_ = ci.post("/challenge-ready", map[string]string{"nodeToken": ci.nodeToken, "path": path})
}

// waitAllNodesReady 轮询主控挑战就绪状态：全部在线节点已监听，或 40s 超时兜底
func (ci *ControlledIssuer) waitAllNodesReady(path string) {
	for i := 0; i < 8; i++ { // 8 × 5s = 40s
		time.Sleep(5 * time.Second)
		b, _ := json.Marshal(map[string]string{"nodeToken": ci.nodeToken, "path": path})
		resp, err := ci.http.Post(ci.masterURL+"/grpc/challenge-status", "application/json", bytes.NewReader(b))
		if err != nil {
			continue
		}
		var out struct {
			AllReady    bool `json:"allReady"`
			ReadyCount  int  `json:"readyCount"`
			OnlineCount int  `json:"onlineCount"`
		}
		if e := json.NewDecoder(resp.Body).Decode(&out); e == nil {
			resp.Body.Close()
			if out.AllReady {
				log.Printf("[tls] 挑战就绪：%d/%d 节点已监听 %s", out.ReadyCount, out.OnlineCount, path)
				return
			}
		} else {
			resp.Body.Close()
		}
	}
	log.Printf("[tls] 挑战就绪确认超时（40s），继续验证（兜底：任意节点可应答即可）")
}

// loadOrCreateAccountKey 加载/创建 ACME 账户私钥（本地文件持久化：重启不换账户，
// 避免 LE "new certificates per account" 配额因重启反复换账户而无法累积定位）。
// 邮箱变更时由 ResetAccount() 清除文件重建账户。
func (ci *ControlledIssuer) loadOrCreateAccountKey() (crypto.Signer, error) {
	ci.tm.accountKeyMu.Lock()
	defer ci.tm.accountKeyMu.Unlock()
	if ci.tm.accountKey != nil {
		return ci.tm.accountKey, nil
	}
	keyPath := ci.tm.cacheDir + "/controlled-account.key"
	if data, err := os.ReadFile(keyPath); err == nil {
		block, _ := pem.Decode(data)
		if block != nil {
			if k, e := x509.ParseECPrivateKey(block.Bytes); e == nil {
				ci.tm.accountKey = k
				return k, nil
			}
		}
	}
	k, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return nil, err
	}
	kder, err := x509.MarshalECPrivateKey(k)
	if err != nil {
		return nil, err
	}
	_ = os.MkdirAll(ci.tm.cacheDir, 0o700)
	if err := os.WriteFile(keyPath, pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: kder}), 0o600); err != nil {
		log.Printf("[tls] 受控账户私钥写入失败（仅内存保存）: %v", err)
	}
	ci.tm.accountKey = k
	return k, nil
}

// ResetAccount 账户轮换（管理端邮箱变更触发）：删除持久化账户私钥，
// 下次签发将以新邮箱注册全新 ACME 账户（用于绕开 LE 按账户计的配额限流）。
// 同时清除旧账户的限流重试时间（新账户无历史，可立即重试）。
func (ci *ControlledIssuer) ResetAccount() {
	ci.tm.accountKeyMu.Lock()
	ci.tm.accountKey = nil
	ci.tm.accountKeyMu.Unlock()
	_ = os.Remove(ci.tm.cacheDir + "/controlled-account.key")
	ci.tm.mu.Lock()
	ci.tm.retryNotBefore = make(map[string]time.Time)
	ci.tm.mu.Unlock()
	log.Printf("[tls] 受控签发账户已重置（下次签发注册新 ACME 账户，限流重试时间已清除）")
}
