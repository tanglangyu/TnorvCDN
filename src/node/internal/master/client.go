// Package master 与主控通信：注册、拉配置、心跳、日志上报（M1 用 HTTP/JSON，与 proto 同构）。
package master

import (
	"bytes"
	"net"
	"encoding/json"
	"fmt"
	"net/http"
	"time"

	"tnorv/node/internal/config"
	"tnorv/node/pb"
)

type Client struct {
	baseURL string
	http    *http.Client
	cfg     *config.NodeConfig
	cfgPath string
}

func NewClient(cfg *config.NodeConfig, cfgPath string) *Client {
	return &Client{
		baseURL: cfg.MasterURL,
		http:    &http.Client{Timeout: 15 * time.Second},
		cfg:     cfg,
		cfgPath: cfgPath,
	}
}

func (c *Client) post(path string, req any, out any) error {
	body, err := json.Marshal(req)
	if err != nil {
		return err
	}
	resp, err := c.http.Post(c.baseURL+path, "application/json", bytes.NewReader(body))
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("master %s: HTTP %d", path, resp.StatusCode)
	}
	return json.NewDecoder(resp.Body).Decode(out)
}

// Register 自动注册：成功后保存 nodeToken 并清除 installToken（一次性）
func (c *Client) Register() error {
	req := &pb.RegisterRequest{
		InstallToken: c.cfg.InstallToken,
		Hostname:     hostname(),
		Version:      Version,
		PublicIps:    DetectPublicIPs(),
		Os:           "linux",
		Arch:         "amd64",
	}
	var out pb.RegisterResponse
	if err := c.post("/grpc/register", req, &out); err != nil {
		return err
	}
	c.cfg.NodeID = out.NodeId
	c.cfg.NodeToken = out.NodeToken
	c.cfg.InstallToken = "" // 消耗一次性令牌
	return config.Save(c.cfgPath, c.cfg)
}

// GetConfig 拉取配置快照
func (c *Client) GetConfig() (*pb.GetConfigResponse, error) {
	req := &pb.GetConfigRequest{NodeToken: c.cfg.NodeToken, ConfigVersion: c.cfg.ConfigVersion}
	var out pb.GetConfigResponse
	if err := c.post("/grpc/config", req, &out); err != nil {
		return nil, err
	}
	if out.PublicIp != "" && PublicIpSink != nil {
		PublicIpSink(out.PublicIp)
	}
	if out.Changed && out.ConfigJson != "" {
		var parsed pb.NodeConfig
		if err := json.Unmarshal([]byte(out.ConfigJson), &parsed); err != nil {
			return nil, fmt.Errorf("解析配置失败: %w", err)
		}
		parsed.Version = out.ConfigVersion
		out.Parsed = &parsed
	}
	return &out, nil
}

// PublicIpSink 主控下发的节点公网 IP（NAT 校正；main 注入 tlsx）
var PublicIpSink func(ip string)

// Heartbeat 上报状态
func (c *Client) Heartbeat(stats *pb.NodeStats) (*pb.HeartbeatResponse, error) {
	req := &pb.HeartbeatRequest{
		NodeToken:     c.cfg.NodeToken,
		ConfigVersion: c.cfg.ConfigVersion,
		Stats:         stats,
	}
	var out pb.HeartbeatResponse
	err := c.post("/grpc/heartbeat", req, &out)
	return &out, err
}

// ReportLogs 批量上报访问日志
func (c *Client) ReportLogs(logs []*pb.AccessLog) error {
	req := &pb.ReportAccessLogsRequest{NodeToken: c.cfg.NodeToken, Logs: logs}
	var out pb.ReportAccessLogsResponse
	return c.post("/grpc/logs", req, &out)
}

// detectPublicIPs 探测本机公网 IP（UDP dial 8.8.8.8，不发流量；失败回退本地 IP）
func DetectPublicIPs() []string {
	conn, err := net.Dial("udp", "8.8.8.8:53")
	if err != nil {
		return nil
	}
	defer conn.Close()
	if addr, ok := conn.LocalAddr().(*net.UDPAddr); ok && !addr.IP.IsLoopback() {
		return []string{addr.IP.String()}
	}
	return nil
}
