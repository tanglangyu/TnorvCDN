package pipeline

import (
	"sync"
	"time"
)

// ccLimiter 滑动窗口频率限制 + 超限封禁通用设计。
type ccLimiter struct {
	mu        sync.Mutex
	threshold int           // 窗口内最大请求数
	window    time.Duration // 滑动窗口宽度
	ban       time.Duration // 超限封禁时长
	clients   map[string]*ccState
	lastGC    time.Time
}

type ccState struct {
	times    []time.Time // 窗口内请求时间戳
	bannedTo time.Time   // 封禁截止
}

func newCcLimiter(threshold, windowSec, banSec int) *ccLimiter {
	if threshold <= 0 {
		threshold = 120
	}
	if windowSec <= 0 {
		windowSec = 60
	}
	if banSec <= 0 {
		banSec = 300
	}
	return &ccLimiter{
		threshold: threshold,
		window:    time.Duration(windowSec) * time.Second,
		ban:       time.Duration(banSec) * time.Second,
		clients:   make(map[string]*ccState),
		lastGC:    time.Now(),
	}
}

// UpdateParams 热更新时调整参数，保留计数状态
func (l *ccLimiter) UpdateParams(threshold, windowSec, banSec int) {
	if threshold > 0 {
		l.threshold = threshold
	}
	if windowSec > 0 {
		l.window = time.Duration(windowSec) * time.Second
	}
	if banSec > 0 {
		l.ban = time.Duration(banSec) * time.Second
	}
}

// Unban 清除指定 IP 的封禁与计数状态。
func (l *ccLimiter) Unban(ip string) {
	l.mu.Lock()
	defer l.mu.Unlock()
	delete(l.clients, ip)
}

// Allow 返回是否放行
func (l *ccLimiter) Allow(ip string) bool {
	now := time.Now()
	l.mu.Lock()
	defer l.mu.Unlock()

	// 低频 GC 防内存增长
	if now.Sub(l.lastGC) > time.Minute {
		for k, st := range l.clients {
			if now.After(st.bannedTo) && (len(st.times) == 0 || now.Sub(st.times[len(st.times)-1]) > 10*time.Minute) {
				delete(l.clients, k)
			}
		}
		l.lastGC = now
	}

	st, ok := l.clients[ip]
	if !ok {
		st = &ccState{}
		l.clients[ip] = st
	}

	// 封禁中
	if now.Before(st.bannedTo) {
		return false
	}

	// 清理窗口外
	cutoff := now.Add(-l.window)
	keep := st.times[:0]
	for _, t := range st.times {
		if t.After(cutoff) {
			keep = append(keep, t)
		}
	}
	st.times = keep

	// 超限 → 封禁
	if len(st.times) >= l.threshold {
		st.bannedTo = now.Add(l.ban)
		st.times = st.times[:0]
		return false
	}

	st.times = append(st.times, now)
	return true
}

// UnbanCc 清除指定 IP 的 CC 封禁状态（管理端解封指令，经配置快照下发）。
func UnbanCc(ip string) {
	globalCcMu.Lock()
	defer globalCcMu.Unlock()
	if globalCc == nil {
		return
	}
	globalCc.Unban(ip)
}
