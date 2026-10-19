package syncer

import (
	"os"
	"strconv"
	"strings"
	"sync"
)

func osReadLoadavg() (float64, error) {
	data, err := os.ReadFile("/proc/loadavg")
	if err != nil {
		return 0, err
	}
	fields := strings.Fields(string(data))
	if len(fields) == 0 {
		return 0, nil
	}
	return strconv.ParseFloat(fields[0], 64)
}

// ---- CPU 使用率（/proc/stat 两次采样差值）----

var (
	cpuPrev      [2]uint64 // idle, total
	cpuPrevOK    bool
	cpuPrevMu    sync.Mutex
	memPercentV  float64
)

// osReadCpuPercent 返回自上次调用以来的 CPU 使用率百分比（首次返回上次缓存值或 0）
func osReadCpuPercent() float64 {
	cpuPrevMu.Lock()
	defer cpuPrevMu.Unlock()
	data, err := os.ReadFile("/proc/stat")
	if err != nil {
		return 0
	}
	for _, line := range strings.Split(string(data), "\n") {
		if !strings.HasPrefix(line, "cpu ") {
			continue
		}
		fields := strings.Fields(line)[1:]
		var user, nice, sys, idle, iowait, irq, softirq, steal uint64
		// 行格式：user nice system idle iowait irq softirq steal ...
		u, _ := strconv.ParseUint(get(fields, 0), 10, 64)
		n, _ := strconv.ParseUint(get(fields, 1), 10, 64)
		s, _ := strconv.ParseUint(get(fields, 2), 10, 64)
		i, _ := strconv.ParseUint(get(fields, 3), 10, 64)
		w, _ := strconv.ParseUint(get(fields, 4), 10, 64)
		q, _ := strconv.ParseUint(get(fields, 5), 10, 64)
		sq, _ := strconv.ParseUint(get(fields, 6), 10, 64)
		st, _ := strconv.ParseUint(get(fields, 7), 10, 64)
		user, nice, sys, idle, iowait, irq, softirq, steal = u, n, s, i, w, q, sq, st
		idleAll := idle + iowait
		total := user + nice + sys + idle + iowait + irq + softirq + steal
		if cpuPrevOK {
			dIdle := idleAll - cpuPrev[0]
			dTotal := total - cpuPrev[1]
			if dTotal > 0 {
				pct := float64(dTotal-dIdle) * 100.0 / float64(dTotal)
				if pct < 0 {
					pct = 0
				}
				if pct > 100 {
					pct = 100
				}
				cpuPrev[0], cpuPrev[1] = idleAll, total
				return pct
			}
		}
		cpuPrev[0], cpuPrev[1] = idleAll, total
		cpuPrevOK = true
		return 0
	}
	return 0
}

func get(fields []string, i int) string {
	if i < len(fields) {
		return fields[i]
	}
	return "0"
}

// osReadMemPercent 返回内存使用率百分比（MemAvailable 口径）
func osReadMemPercent() float64 {
	data, err := os.ReadFile("/proc/meminfo")
	if err != nil {
		return 0
	}
	var total, avail uint64
	for _, line := range strings.Split(string(data), "\n") {
		if strings.HasPrefix(line, "MemTotal:") {
			total = parseMemKB(line)
		} else if strings.HasPrefix(line, "MemAvailable:") {
			avail = parseMemKB(line)
		}
	}
	if total == 0 {
		return 0
	}
	pct := float64(total-avail) * 100.0 / float64(total)
	if pct < 0 {
		pct = 0
	}
	if pct > 100 {
		pct = 100
	}
	return pct
}

func parseMemKB(line string) uint64 {
	fields := strings.Fields(line)
	if len(fields) < 2 {
		return 0
	}
	v, _ := strconv.ParseUint(fields[1], 10, 64)
	return v
}
