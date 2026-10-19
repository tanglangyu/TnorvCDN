package pipeline

// locations.go 路由规则（location 级配置覆盖，通用设计：
// 命中 pathPrefix 的请求在安全检查与缓存前按规则覆盖：
//   - 缓存：整组替换（规则里配了 cache 就用 location 的，否则继承站点级）
//   - CC：整组替换（location 级限流阈值独立于站点级）
//   - IP 黑/白名单：与站点级合并（location 追加名单）
//   - Referer 防盗链：整组替换
//   - 请求限制 / 访客IP头：字段级覆盖
// 先配置的规则优先（与 matchPathRule 同序）。

import (
	"net"
	"strings"

	"tnorv/node/pb"
)

// compiledLocation 预编译的 location 视图（Pipeline 构建期一次，请求期零分配匹配）
type compiledLocation struct {
	prefix     string
	cache      *CacheLayer        // nil=继承站点级
	blackNet   []*net.IPNet       // location 级 IP 黑名单（追加匹配）
	whiteNet   []*net.IPNet       // location 级 IP 白名单
	referers   *pb.RefererConfig  // nil=继承
	reqLimit   *reqLimiter        // nil=继承站点级
	remoteHdr  string             // ""=继承
	hasRemote  bool
	adv        *advCompiled       // nil=继承站点级高级配置（报头/重写/压缩/字符集覆盖）
	compression *bool             // nil=继承；false=禁压缩
}

// compileLocations 编译站点路由规则（保持配置顺序）
func compileLocations(srv *pb.Server) []*compiledLocation {
	var out []*compiledLocation
	for _, lr := range srv.Locations {
		if lr == nil || lr.PathPrefix == "" || !lr.Enabled {
			continue
		}
		cl := &compiledLocation{prefix: lr.PathPrefix}
		if lr.Cache != nil && lr.Cache.Enabled && len(lr.Cache.Rules) > 0 {
			cl.cache = NewCacheLayer(lr.Cache, "")
		}
		if lr.Security != nil {
			for _, r := range lr.Security.IpBlacklist {
				if n := parseCIDR(r.Cidr); n != nil {
					cl.blackNet = append(cl.blackNet, n)
				}
			}
			for _, r := range lr.Security.IpWhitelist {
				if n := parseCIDR(r.Cidr); n != nil {
					cl.whiteNet = append(cl.whiteNet, n)
				}
			}
			cl.referers = lr.Security.Referers
		}
		if lr.RequestLimitPerMinute > 0 {
			cl.reqLimit = newReqLimiter(lr.RequestLimitPerMinute)
		}
		if lr.Advanced != nil {
			ac := &pb.AdvancedConfig{Headers: lr.Advanced.Headers, Rewrites: lr.Advanced.Rewrites, Charset: lr.Advanced.Charset}
			if lr.Advanced.Compression != nil && *lr.Advanced.Compression {
				ac.Compression = true
			}
			cl.adv = compileAdvanced(ac)
			cl.compression = lr.Advanced.Compression
		}
		if lr.RemoteAddrHeader != nil {
			cl.remoteHdr = *lr.RemoteAddrHeader
			cl.hasRemote = true
		}
		out = append(out, cl)
	}
	return out
}

// matchLocation 返回第一个命中的路由规则（先配置优先）
func matchLocation(locs []*compiledLocation, path string) *compiledLocation {
	for _, l := range locs {
		if strings.HasPrefix(path, l.prefix) {
			return l
		}
	}
	return nil
}

// locCache 命中 location 且配了缓存覆盖时返回该缓存层（请求路径用）
func (l *compiledLocation) locCache() *CacheLayer {
	if l == nil {
		return nil
	}
	return l.cache
}

// locIPCheck location 级 IP 名单：白名单命中 → allowed；黑名单命中 → blocked。
// 名单为空时 matched=false（不参与）。
func (l *compiledLocation) locIPCheck(ip string) (blocked, allowed, matched bool) {
	if l == nil || (len(l.blackNet) == 0 && len(l.whiteNet) == 0) {
		return false, false, false
	}
	p := net.ParseIP(ip)
	if p == nil {
		return false, false, false
	}
	for _, n := range l.whiteNet {
		if n.Contains(p) {
			return false, true, true
		}
	}
	for _, n := range l.blackNet {
		if n.Contains(p) {
			return true, false, true
		}
	}
	return false, false, true
}

// locReferers 命中 location 且配了防盗链覆盖时返回该配置
func (l *compiledLocation) locReferers() *pb.RefererConfig {
	if l == nil {
		return nil
	}
	return l.referers
}

// locAllowRequest location 级请求限制检查（nil=继承站点级）
func (l *compiledLocation) locAllowRequest(ip string) bool {
	if l == nil || l.reqLimit == nil {
		return true
	}
	return l.reqLimit.allow(ip)
}

// locAdv location 级高级配置覆盖（nil=继承站点级）
func (l *compiledLocation) locAdv() *advCompiled {
	if l == nil {
		return nil
	}
	return l.adv
}

// locCompression 压缩覆盖（nil=继承站点级）
func (l *compiledLocation) locCompression() *bool {
	if l == nil {
		return nil
	}
	return l.compression
}

// locRemoteHeader 返回 location 级访客IP头（hasRemote=false 表示继承站点级）
func (l *compiledLocation) locRemoteHeader() (string, bool) {
	if l == nil || !l.hasRemote {
		return "", false
	}
	return l.remoteHdr, true
}
