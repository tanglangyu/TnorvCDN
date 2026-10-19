package master

import (
	"os"
	"runtime"
)

// Version 节点程序版本（发布时由 -ldflags 注入）
var Version = "0.1.0-dev"

func hostname() string {
	h, err := os.Hostname()
	if err != nil {
		return "unknown"
	}
	return h + "/" + runtime.GOOS + "-" + runtime.GOARCH
}
