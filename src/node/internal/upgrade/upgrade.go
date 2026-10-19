package upgrade

// upgrade.go 节点自升级：主控下发升级指令（经本地 admin HTTP 端点触发），
// 节点下载新二进制 → 原子替换 → 重启自身。
// 流程通用设计：
//   1. POST /admin/upgrade {url, version, sha256}
//   2. 下载到 /opt/tnorv/tnorv-node.new（下载目录与运行目录同盘保证 rename 原子）
//   3. sha256 校验（可选）
//   4. chmod +x，rename 覆盖当前二进制
//   5. syscall.Exec 重启自身（新二进制立即生效）

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"syscall"
	"time"
)

// Self 自升级自身（升级 URL 由主控下发；sha256 可空）
func Self(downloadURL, version, sha256Hex string) error {
	if downloadURL == "" {
		return fmt.Errorf("download url required")
	}
	// 当前可执行文件路径
	exe, err := os.Executable()
	if err != nil {
		return fmt.Errorf("locate self: %w", err)
	}
	exePath, _ := filepath.Abs(exe)
	tmpPath := exePath + ".new"

	// 下载（60s 超时；节点二进制 ~9MB）
	client := &http.Client{Timeout: 60 * time.Second}
	resp, err := client.Get(downloadURL)
	if err != nil {
		return fmt.Errorf("download: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("download: status %d", resp.StatusCode)
	}
	f, err := os.Create(tmpPath)
	if err != nil {
		return fmt.Errorf("create tmp: %w", err)
	}
	h := sha256.New()
	_, err = io.Copy(io.MultiWriter(f, h), resp.Body)
	fc := f.Close()
	if err != nil {
		os.Remove(tmpPath)
		return fmt.Errorf("write tmp: %w", err)
	}
	if fc != nil {
		return fc
	}

	// sha256 校验
	if sha256Hex != "" {
		got := hex.EncodeToString(h.Sum(nil))
		if got != sha256Hex {
			os.Remove(tmpPath)
			return fmt.Errorf("sha256 mismatch: got %s want %s", got, sha256Hex)
		}
	}

	// 原子替换
	if err := os.Chmod(tmpPath, 0o755); err != nil {
		os.Remove(tmpPath)
		return err
	}
	if err := os.Rename(tmpPath, exePath); err != nil {
		os.Remove(tmpPath)
		return fmt.Errorf("replace: %w", err)
	}

	// 重启自身（exec 替换进程映像，监听端口由新进程重开；短暂中断 <1s）
	return syscall.Exec(exePath, os.Args, os.Environ())
}
