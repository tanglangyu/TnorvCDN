package pipeline

// fastcgi.go FastCGI 回源通用设计：origin.scheme=fastcgi 时
// 经 FastCGI 协议把请求转给 PHP-FPM / fcgiwrap 等 responder。
// SCRIPT_FILENAME 规则：<root><uri>；root 取 origin.Bucket（复用字段）或默认 /var/www。

import (
	"log"
	"net"
	"net/http"
	"path"
	"strings"

	"github.com/yookoala/gofast"
	"tnorv/node/pb"
)

func (p *ProxyLayer) serveFastCGI(w http.ResponseWriter, r *http.Request, origin *pb.Origin, prule *pb.PathOriginRule) {
	if isForbiddenHost(origin.Host) {
		http.Error(w, "502 Bad Gateway: fastcgi origin not allowed", http.StatusBadGateway)
		return
	}
	root := origin.Bucket
	if root == "" {
		root = "/var/www"
	}
	network, addr := "tcp", origin.Host
	if strings.HasPrefix(origin.Host, "unix:") {
		network, addr = "unix", strings.TrimPrefix(origin.Host, "unix:")
	} else {
		addr = net.JoinHostPort(origin.Host, itoaPort(origin.Port))
	}
	remoteIP := clientIP(r)

	// 会话：BasicParamsMap 生成标准 CGI 环境，再补 SCRIPT_FILENAME / DOCUMENT_ROOT / 真实 IP
	session := gofast.BasicParamsMap(gofast.BasicSession)
	h := gofast.NewHandler(func(client gofast.Client, req *gofast.Request) (*gofast.ResponsePipe, error) {
		uri := r.URL.RequestURI()
		req.Params["SCRIPT_FILENAME"] = path.Clean(root + "/" + strings.TrimPrefix(uri, "?"))
		req.Params["DOCUMENT_ROOT"] = root
		req.Params["SCRIPT_NAME"] = r.URL.Path
		req.Params["REMOTE_ADDR"] = remoteIP
		req.Params["HTTP_HOST"] = hostFor(origin, prule, p.cfg, r)
		return session(client, req)
	}, gofast.SimpleClientFactory(gofast.SimpleConnFactory(network, addr)))

	// X-Real-IP 透传：gofast 从 http.Request 生成参数，改写前注入
	r.Header.Set("X-Real-IP", remoteIP)
	h.ServeHTTP(w, r)
	globalHealth.Store(originKey(origin), true)
	log.Printf("[fastcgi] 回源 %s addr=%s root=%s uri=%s", originKey(origin), addr, root, r.URL.RequestURI())
}
