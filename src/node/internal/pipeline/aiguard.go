package pipeline

// aiguard.go AI 语义防护：对可疑请求（本地规则 log 命中 / CC 高频 / 简单规则放过的模糊特征）
// 异步采样送 LLM（OpenAI 兼容 /chat/completions）做语义判定。
//   - 绝不阻塞请求路径：判定在独立 goroutine 进行
//   - 判定结果进入 globalAiBlocks（IP+path 前缀 → 过期时间），后续请求命中即 403
//   - Token 用量统计（prompt/completion 累计），随心跳上报主控

import (
	"bytes"
	"encoding/json"
	"net/http"
	"strings"
	"sync"
	"time"

	"tnorv/node/pb"
)

const aiSaltPath = "/__aiguard__"

// globalAiBlocks AI 判定为恶意的 IP 封禁表
var globalAiBlocks = struct {
	sync.Mutex
	m map[string]time.Time // ip -> ban till
}{m: make(map[string]time.Time)}

// aiStats Token 用量统计（心跳上报）
var aiStats = struct {
	sync.Mutex
	PromptTokens     uint64
	CompletionTokens uint64
	Requests         uint64
	Blocked          uint64
}{}

// AiGuardCheck 主入口：返回 BlockResult（nil=放行）。同步部分只查缓存表（O(1)）。
func AiGuardCheck(cfg *pb.AiGuardConfig, r *http.Request) *BlockResult {
	if cfg == nil || !cfg.Enabled {
		return nil
	}
	ip := clientIP(r)
	globalAiBlocks.Lock()
	till, ok := globalAiBlocks.m[ip]
	globalAiBlocks.Unlock()
	if ok && time.Now().Before(till) {
		return &BlockResult{Status: 403, Reason: "ai-guard: semantically blocked"}
	}
	if ok { // 过期清理
		globalAiBlocks.Lock()
		delete(globalAiBlocks.m, ip)
		globalAiBlocks.Unlock()
	}
	return nil
}

// AiGuardAudit 异步审计：采样送 LLM 判定（在 security.Check 标记 log/suspicious 时调用）
func AiGuardAudit(cfg *pb.AiGuardConfig, r *http.Request, reason string) {
	if cfg == nil || !cfg.Enabled || cfg.ApiUrl == "" || cfg.ApiKey == "" {
		return
	}
	if cfg.SampleRate <= 0 || int(time.Now().UnixNano()%100) >= cfg.SampleRate {
		return // 采样率之外跳过
	}
	ip := clientIP(r)
	// 抓取请求快照（不读 body——避免消耗/阻塞；只用元数据）
	meta := map[string]any{
		"method": r.Method,
		"path":   r.URL.Path,
		"query":  r.URL.RawQuery,
		"ua":     r.UserAgent(),
		"ip":     ip,
		"reason": reason,
	}
	mb, _ := json.Marshal(meta)
	go func(snapshot []byte) {
		malicious, tokens := aiJudge(cfg, snapshot)
		aiStats.Lock()
		aiStats.Requests++
		aiStats.PromptTokens += tokens.prompt
		aiStats.CompletionTokens += tokens.completion
		aiStats.Unlock()
		if malicious {
			globalAiBlocks.Lock()
			globalAiBlocks.m[ip] = time.Now().Add(10 * time.Minute)
			globalAiBlocks.Unlock()
			aiStats.Lock()
			aiStats.Blocked++
			aiStats.Unlock()
		}
	}(mb)
}

type aiTokens struct {
	prompt     uint64
	completion uint64
}

// aiJudge 调 LLM 判定（OpenAI 兼容），返回 (恶意, token用量)
func aiJudge(cfg *pb.AiGuardConfig, snapshot []byte) (bool, aiTokens) {
	sys := "你是 CDN 边缘安全分析器。判断以下 HTTP 请求元数据是否恶意（SQL注入/XSS/扫描器/爆破/Webshell探测等）。只输出 JSON：{\"malicious\": true/false, \"score\": 0.0-1.0}"
	body := map[string]any{
		"model": cfg.Model,
		"messages": []map[string]string{
			{"role": "system", "content": sys},
			{"role": "user", "content": string(snapshot)},
		},
		"max_tokens":  100,
		"temperature": 0.0,
	}
	jb, _ := json.Marshal(body)
	req, err := http.NewRequest(http.MethodPost, strings.TrimSuffix(cfg.ApiUrl, "/"), bytes.NewReader(jb))
	if err != nil {
		return false, aiTokens{}
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+cfg.ApiKey)
	client := &http.Client{Timeout: 10 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		return false, aiTokens{}
	}
	defer resp.Body.Close()
	var out struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
		Usage struct {
			PromptTokens     int `json:"prompt_tokens"`
			CompletionTokens int `json:"completion_tokens"`
		} `json:"usage"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return false, aiTokens{}
	}
	if len(out.Choices) == 0 {
		return false, aiTokens{}
	}
	// 解析 LLM 输出 JSON（容忍包装）
	content := strings.TrimSpace(out.Choices[0].Message.Content)
	content = strings.Trim(content, "` \n")
	content = strings.TrimPrefix(content, "json")
	var verdict struct {
		Malicious bool    `json:"malicious"`
		Score     float64 `json:"score"`
	}
	if err := json.Unmarshal([]byte(content), &verdict); err != nil {
		// 兜底：文本包含 malicious true
		verdict.Malicious = strings.Contains(content, "\"malicious\": true") || strings.Contains(content, "\"malicious\":true")
	}
	return verdict.Malicious && verdict.Score >= cfg.BlockThreshold || verdict.Malicious && cfg.BlockThreshold <= 0,
		aiTokens{prompt: uint64(out.Usage.PromptTokens), completion: uint64(out.Usage.CompletionTokens)}
}

// AiStatsSnapshot 供心跳上报
func AiStatsSnapshot() (prompt, completion, requests, blocked uint64) {
	aiStats.Lock()
	defer aiStats.Unlock()
	return aiStats.PromptTokens, aiStats.CompletionTokens, aiStats.Requests, aiStats.Blocked
}
