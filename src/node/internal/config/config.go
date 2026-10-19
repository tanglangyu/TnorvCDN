// Package config 节点本地配置（configs/tnorv-node.json）
package config

import (
	"encoding/json"
	"os"
)

type NodeConfig struct {
	MasterURL      string `json:"masterUrl"`      // 主控地址，如 http://master.example.com:8080
	InstallToken   string `json:"installToken"`   // 安装令牌（首次注册用，注册成功后自动清除）
	NodeToken      string `json:"nodeToken"`      // 注册成功后保存
	NodeID         int64  `json:"nodeId"`
	ConfigVersion  int64  `json:"configVersion"`
	CacheDir       string `json:"cacheDir"`       // 文件缓存目录
	MemoryCacheMB  int    `json:"memoryCacheMB"`  // 内存热点缓存上限（MB）
	LogBufferMax   int    `json:"logBufferMax"`   // 日志本地缓冲上限（断网不丢）
	AdminHTTPAddr  string `json:"adminHttpAddr"`  // 节点本地管理端口（/health 等）
}

func Default() *NodeConfig {
	return &NodeConfig{
		MasterURL:     "http://127.0.0.1:8080",
		CacheDir:      "/opt/tnorv/cache",
		MemoryCacheMB: 128,
		LogBufferMax:  10000,
		AdminHTTPAddr: "127.0.0.1:8081",
	}
}

// Load 从磁盘加载；文件不存在返回默认值
func Load(path string) (*NodeConfig, error) {
	cfg := Default()
	data, err := os.ReadFile(path)
	if err != nil {
		if os.IsNotExist(err) {
			return cfg, nil
		}
		return nil, err
	}
	if err := json.Unmarshal(data, cfg); err != nil {
		return nil, err
	}
	return cfg, nil
}

// Save 原子写盘（0600 权限，防其他用户读取 token）
func Save(path string, cfg *NodeConfig) error {
	data, err := json.MarshalIndent(cfg, "", "  ")
	if err != nil {
		return err
	}
	tmp := path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o600); err != nil {
		return err
	}
	return os.Rename(tmp, path)
}
