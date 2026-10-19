package pipeline

// waf.go 通用 WAF 规则引擎：条件-操作符-动作通用设计。
// 性能：规则编译一次（Security 构造期），请求期零分配匹配；正则预编译。

import (
	"net"
	"net/http"
	"regexp"
	"strconv"
	"strings"
)

type compiledWafRule struct {
	rule      *wafRuleView
	conds     []*compiledCond
	action    string
	actionVal string
	priority  int
}

type compiledCond struct {
	field    string
	header   string // field=header_X / query_X 时的具体名
	op       string
	value    string
	negate   bool
	re       *regexp.Regexp // op=regex 时预编译
	cidrs    []*net.IPNet   // op=in-cidr / not-in-cidr 时预编译
	inList   []string       // op=in / not-in（分号分隔）
	numVal   float64        // 数值比较
}

// wafRuleView 与 pb.WafRule 同构的本地视图（避免 pipeline 直接依赖 pb 字段名漂移）
type wafRuleView struct {
	Name        string
	Enabled     bool
	Conditions  []*wafConditionView
	Action      string
	ActionValue string
	Priority    int
}

type wafConditionView struct {
	Field    string
	Operator string
	Value    string
	Negate   bool
}

// urlAuthView 与 pb.UrlAuthConfig 同构的本地视图
type urlAuthView struct {
	Enabled       bool
	Type          string
	Key           string
	ExpirySeconds int
}

// 编译规则集：按优先级排序
func compileWafRules(rules []*wafRuleView) []*compiledWafRule {
	var out []*compiledWafRule
	for _, r := range rules {
		if r == nil || !r.Enabled || len(r.Conditions) == 0 {
			continue
		}
		cw := &compiledWafRule{rule: r, action: r.Action, actionVal: r.ActionValue, priority: r.Priority}
		for _, c := range r.Conditions {
			cc := &compiledCond{field: strings.ToLower(c.Field), op: c.Operator, value: c.Value, negate: c.Negate}
			if i := strings.Index(cc.field, "_"); i > 0 && (strings.HasPrefix(cc.field, "header_") || strings.HasPrefix(cc.field, "query_")) {
				cc.header = c.Field[i+1:]
			}
			switch cc.op {
			case "regex":
				if re, err := regexp.Compile(cc.value); err == nil {
					cc.re = re
				} else {
					continue // 非法正则跳过该条件
				}
			case "in-cidr", "not-in-cidr":
				for _, s := range strings.Split(cc.value, ";") {
					if n := parseCIDR(strings.TrimSpace(s)); n != nil {
						cc.cidrs = append(cc.cidrs, n)
					}
				}
			case "in", "not-in":
				for _, s := range strings.Split(cc.value, ";") {
					if t := strings.TrimSpace(s); t != "" {
						cc.inList = append(cc.inList, t)
					}
				}
			case "gt", "lt", "gte", "lte":
				cc.numVal, _ = strconv.ParseFloat(cc.value, 64)
			}
			cw.conds = append(cw.conds, cc)
		}
		if len(cw.conds) > 0 {
			out = append(out, cw)
		}
	}
	// 稳定排序：priority 小者先执行
	for i := 1; i < len(out); i++ {
		for j := i; j > 0 && out[j].priority < out[j-1].priority; j-- {
			out[j], out[j-1] = out[j-1], out[j]
		}
	}
	return out
}

// matchWaf 返回命中的动作（""=未命中）。规则内条件全部满足才算命中（AND）。
func matchWaf(rules []*compiledWafRule, r *http.Request) (action, reason string) {
	if len(rules) == 0 {
		return "", ""
	}
	ip := net.ParseIP(clientIP(r))
	for _, wr := range rules {
		all := true
		for _, c := range wr.conds {
			if !c.match(r, ip) {
				all = false
				break
			}
		}
		if all {
			return wr.action, wr.rule.Name
		}
	}
	return "", ""
}

func (c *compiledCond) match(r *http.Request, ip net.IP) bool {
	var subject string
	exists := true

	switch c.field {
	case "uri":
		subject = r.URL.Path
	case "uri-full":
		subject = r.URL.RequestURI()
	case "ip":
		if ip != nil {
			subject = ip.String()
		} else {
			exists = false
		}
	case "user_agent":
		subject = r.UserAgent()
	case "referer":
		subject = r.Referer()
	case "method":
		subject = r.Method
	case "host":
		subject = r.Host
	case "body_length":
		if r.ContentLength >= 0 {
			subject = strconv.FormatInt(r.ContentLength, 10)
		} else {
			exists = false
		}
	default:
		if h, ok := strings.CutPrefix(c.field, "header_"); ok {
			subject, exists = r.Header.Get(h), r.Header.Get(h) != ""
		} else if q, ok := strings.CutPrefix(c.field, "query_"); ok {
			subject, exists = r.URL.Query().Get(q), r.URL.Query().Has(q)
		} else {
			return false // 未知字段：视为不匹配（不误杀）
		}
	}

	var hit bool
	switch c.op {
	case "eq":
		hit = subject == c.value
	case "ne":
		hit = subject != c.value
	case "contains":
		hit = strings.Contains(subject, c.value)
	case "not-contains":
		hit = !strings.Contains(subject, c.value)
	case "prefix":
		hit = strings.HasPrefix(subject, c.value)
	case "suffix":
		hit = strings.HasSuffix(subject, c.value)
	case "regex":
		if c.re != nil {
			hit = c.re.MatchString(subject)
		}
	case "in":
		for _, v := range c.inList {
			if subject == v {
				hit = true
				break
			}
		}
	case "not-in":
		hit = true
		for _, v := range c.inList {
			if subject == v {
				hit = false
				break
			}
		}
	case "gt", "lt", "gte", "lte":
		n, err := strconv.ParseFloat(subject, 64)
		if err != nil {
			hit = false
		} else {
			switch c.op {
			case "gt":
				hit = n > c.numVal
			case "lt":
				hit = n < c.numVal
			case "gte":
				hit = n >= c.numVal
			case "lte":
				hit = n <= c.numVal
			}
		}
	case "exists":
		hit = exists
	case "not-exists":
		hit = !exists
	case "in-cidr":
		if ip != nil {
			for _, n := range c.cidrs {
				if n.Contains(ip) {
					hit = true
					break
				}
			}
		}
	case "not-in-cidr":
		hit = true
		if ip != nil {
			for _, n := range c.cidrs {
				if n.Contains(ip) {
					hit = false
					break
				}
			}
		}
	case "empty":
		hit = subject == ""
	case "not-empty":
		hit = subject != ""
	default:
		return false
	}

	if c.negate {
		hit = !hit
	}
	return hit
}
