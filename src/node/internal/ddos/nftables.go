package ddos

// nftables.go DDoS 防护执行器：把主控下发的集群级防护配置翻译为本机 nftables 规则。
// 设计：
//   - 独立表 "tnorv_ddos"，全量替换（flush + 重建），幂等且可随时关闭
//   - 每 IP 并发连接数限制 → connlimit
//   - 每 IP 新建连接速率 → meter + limit rate/second（burst 桶）
//   - SYN Flood → ct state new 的 syn 包限速
//   - 仅对配置端口生效；未配端口则对表内定义的所有常见 Web 端口不设防（由端口列表决定）
// 权限：需要 root（节点以 root 运行）；nft 不可用时记录告警并降级为仅内核参数。

import (
	"fmt"
	"os/exec"
	"strings"
	"sync"

	"tnorv/node/pb"
)

var (
	nftMu    sync.Mutex
	lastRule string // 上次下发的规则指纹，避免无谓的重写
)

// ApplyDdos 应用 DDoS 配置：生成 nftables 规则并原子替换。cfg 为 nil 或未启用时卸载防护表。
func ApplyDdos(cfg *pb.DdosConfig) (applied bool, err error) {
	nftMu.Lock()
	defer nftMu.Unlock()

	if cfg == nil || !cfg.Enabled {
		if lastRule != "" {
			_ = runNft("delete table inet tnorv_ddos")
			lastRule = ""
		}
		return false, nil
	}

	rules := buildRules(cfg)
	if rules == lastRule {
		return true, nil // 无变化
	}

	// 先删旧表再建（幂等；表不存在时 delete 报错可忽略）
	_ = runNft("delete table inet tnorv_ddos")
	if err := runNft(rules); err != nil {
		lastRule = ""
		return false, fmt.Errorf("nft apply failed: %w", err)
	}
	lastRule = rules
	return true, nil
}

// buildRules 生成 nft 脚本
func buildRules(cfg *pb.DdosConfig) string {
	var b strings.Builder
	b.WriteString("table inet tnorv_ddos {\n")
	b.WriteString("  chain input {\n    type filter hook input priority -150; policy accept;\n")

	ports := cfg.Ports
	if len(ports) == 0 {
		ports = []int{80, 443}
	}
	var portExprs []string
	for _, p := range ports {
		portExprs = append(portExprs, fmt.Sprintf("%d", p))
	}
	portMatch := "tcp dport { " + strings.Join(portExprs, ", ") + " }"

	// 网络安全：管理网段白名单优先放行（置于一切限速规则之前）
	if len(cfg.AdminAllowCidrs) > 0 {
		var cidrs []string
		for _, c := range cfg.AdminAllowCidrs {
			if c = strings.TrimSpace(c); c != "" {
				cidrs = append(cidrs, c)
			}
		}
		if len(cidrs) > 0 {
			fmt.Fprintf(&b, "    ip saddr { %s } accept\n", strings.Join(cidrs, ", "))
		}
	}

	// 每 IP 并发连接数限制（ct count 是动态检查，直接在规则里写）
	if cfg.ConnLimit > 0 {
		fmt.Fprintf(&b, "    %s ct state new ct count over %d drop\n", portMatch, cfg.ConnLimit)
	}
	// 每 IP 新建连接速率（meter 内联定义动态 set + limit）
	if cfg.ConnRate > 0 {
		burst := cfg.BurstRate
		if burst <= 0 {
			burst = cfg.ConnRate
		}
		fmt.Fprintf(&b, "    %s ct state new meter rate_meter { ip saddr timeout 1m limit rate over %d/second burst %d packets } drop\n",
			portMatch, cfg.ConnRate, burst)
	}
	// SYN Flood 防护
	if cfg.SynFloodLimit > 0 {
		fmt.Fprintf(&b, "    %s tcp flags & (fin|syn|rst|ack) == syn meter syn_meter { ip saddr timeout 1m limit rate over %d/second burst %d packets } drop\n",
			portMatch, cfg.SynFloodLimit, cfg.SynFloodLimit)
	}
	b.WriteString("  }\n}\n")
	return b.String()
}

func runNft(script string) error {
	cmd := exec.Command("nft", "-f", "-")
	cmd.Stdin = strings.NewReader(script)
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("%v: %s", err, strings.TrimSpace(string(out)))
	}
	return nil
}
