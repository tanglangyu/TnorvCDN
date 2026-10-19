package pipeline

// edgeScript.go 边缘脚本：基于 expr-lang 的请求改写通用设计。
//   - 脚本在安全检查之后、缓存查询之前执行，可改写请求头/URL/请求上下文，也可直接短路响应
//   - 上下文变量：method, path, query, host, ua, ip, header（map）、queryParam(name)
//   - 动作函数：setHeader(k,v), delHeader(k), setPath(p), setQuery(k,v),
//     respond(status, body)（短路直接响应）, log(msg)
//   - 表达式结果为 true/false 也可（纯条件脚本），错误安全（失败仅记日志不阻断）
//   - 脚本编译失败在配置加载时跳过并告警，绝不阻断流量

import (
	"fmt"
	"net/http"
	"strings"

	"github.com/expr-lang/expr"
	"github.com/expr-lang/expr/vm"

	"tnorv/node/pb"
)

// currentScriptCtx 当前请求的脚本上下文（动作函数 respond 绑定目标；单 worker 语义不并发——
// pipeline 串行处理内每请求先设再跑，且 vm.Run 同步返回，无竞态窗口）
var currentScriptCtx *scriptCtx

// compiledScript 编译后的边缘脚本
type compiledScript struct {
	rule *pb.EdgeScript
	prog *vm.Program
}

// scriptCtx 脚本执行环境（每次请求重建，轻量）
type scriptCtx struct {
	Method     string            `expr:"method"`
	Path       string            `expr:"path"`
	Query      string            `expr:"query"`
	Host       string            `expr:"host"`
	UA         string            `expr:"ua"`
	IP         string            `expr:"ip"`
	Header     map[string]string `expr:"header"`
	respWriter http.ResponseWriter
	req        *http.Request
	shortCircuited bool
}

// QueryParam 取查询参数（脚本函数）
func (c *scriptCtx) QueryParam(name string) string {
	return c.req.URL.Query().Get(name)
}

// SetHeader 设置请求头（改写上游可见请求）
func (c *scriptCtx) SetHeader(k, v string) {
	c.req.Header.Set(k, v)
}

// DelHeader 删除请求头
func (c *scriptCtx) DelHeader(k string) {
	c.req.Header.Del(k)
}

// SetPath 改写请求路径
func (c *scriptCtx) SetPath(p string) {
	if strings.HasPrefix(p, "/") {
		c.req.URL.Path = p
		c.Path = p
	}
}

// SetQuery 设置/覆盖查询参数
func (c *scriptCtx) SetQuery(k, v string) {
	q := c.req.URL.Query()
	q.Set(k, v)
	c.req.URL.RawQuery = q.Encode()
	c.Query = c.req.URL.RawQuery
}

// Respond 短路响应（直接返回固定内容，不回源）
func (c *scriptCtx) Respond(status int, body string) bool {
	if c.respWriter == nil || c.shortCircuited {
		return false
	}
	c.shortCircuited = true
	w := c.respWriter
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.WriteHeader(status)
	w.Write([]byte(body))
	return true
}

// Log 记录脚本日志（写入响应头便于调试，生产可接日志系统）
func (c *scriptCtx) Log(msg string) bool {
	if c.respWriter != nil {
		c.respWriter.Header().Add("X-Tnorv-Script-Log", msg)
	}
	return true
}

// CompileEdgeScripts 编译配置中的全部脚本（配置加载时调用）
func CompileEdgeScripts(rules []*pb.EdgeScript) []*compiledScript {
	var out []*compiledScript
	env := scriptCtx{}
	for _, r := range rules {
		if !r.Enabled || strings.TrimSpace(r.Expr) == "" {
			continue
		}
			prog, err := expr.Compile(r.Expr,
			expr.Env(&env),
			expr.AllowUndefinedVariables(),
			expr.Function("respond",
				func(params ...any) (any, error) {
					status, _ := params[0].(int)
					body, _ := params[1].(string)
					return currentScriptCtx.Respond(status, body), nil
				},
				new(func(int, string) bool),
			),
			expr.Function("setPath",
				func(params ...any) (any, error) {
					p2, _ := params[0].(string)
					currentScriptCtx.SetPath(p2)
					return true, nil
				},
				new(func(string) bool),
			),
			expr.Function("setQuery",
				func(params ...any) (any, error) {
					k, _ := params[0].(string)
					v, _ := params[1].(string)
					currentScriptCtx.SetQuery(k, v)
					return true, nil
				},
				new(func(string, string) bool),
			),
			expr.Function("setHeader",
				func(params ...any) (any, error) {
					k, _ := params[0].(string)
					v, _ := params[1].(string)
					currentScriptCtx.SetHeader(k, v)
					return true, nil
				},
				new(func(string, string) bool),
			),
			expr.Function("delHeader",
				func(params ...any) (any, error) {
					k, _ := params[0].(string)
					currentScriptCtx.DelHeader(k)
					return true, nil
				},
				new(func(string) bool),
			),
			expr.Function("log",
				func(params ...any) (any, error) {
					msg, _ := params[0].(string)
					currentScriptCtx.Log(msg)
					return true, nil
				},
				new(func(string) bool),
			),
		)
		if err != nil {
			fmt.Printf("[edge-script] 编译失败 %q: %v（已跳过）\n", r.Name, err)
			continue
		}
		out = append(out, &compiledScript{rule: r, prog: prog})
	}
	return out
}

// RunEdgeScripts 请求路径执行脚本链；返回 true = 已短路响应（请求终止）
func RunEdgeScripts(scripts []*compiledScript, w http.ResponseWriter, r *http.Request) bool {
	if len(scripts) == 0 {
		return false
	}
	hdr := map[string]string{}
	for k := range r.Header {
		hdr[strings.ToLower(k)] = r.Header.Get(k)
	}
	ctx := &scriptCtx{
		Method:     r.Method,
		Path:       r.URL.Path,
		Query:      r.URL.RawQuery,
		Host:       r.Host,
		UA:         r.UserAgent(),
		IP:         clientIP(r),
		Header:     hdr,
		respWriter: w,
		req:        r,
	}
	currentScriptCtx = ctx // 动作函数绑定到本次请求
	for _, s := range scripts {
		out, err := vm.Run(s.prog, ctx)
		if err != nil {
			continue // 脚本错误安全：跳过
		}
		if b, ok := out.(bool); ok && !b {
			continue // false 条件脚本：继续下一条
		}
		if ctx.shortCircuited {
			return true
		}
	}
	return false
}
