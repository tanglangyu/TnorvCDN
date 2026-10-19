<script setup lang="ts">
/**
 * 通知铃铛 —— 右上角铃铛图标 + 红点未读数 + 点击展开下拉面板。
 * 适配暗色模式、高斯模糊、点击外部关闭。
 */
import { ref, onMounted, onUnmounted } from 'vue'
import { notificationApi } from '@/api/notification'
import { onNotification, startNotificationHub } from '@/composables/useNotification'
import { formatTime, fromNow } from '@/utils/time'
import type { SysNotification } from '@/types'

const unread = ref(0)
const list = ref<SysNotification[]>([])
const open = ref(false)
const loading = ref(false)
const panel = ref<HTMLDivElement>()

async function loadUnread() {
  try { unread.value = (await notificationApi.unreadCount()) as unknown as number } catch {}
}
async function loadList() {
  loading.value = true
  try {
    const res: any = await notificationApi.list({ pageIndex: 1, pageSize: 20 })
    // http.get 拆包到 data 层: { list: [...], total: N }
    list.value = Array.isArray(res) ? res : (res?.list ?? res?.items ?? [])
  } finally { loading.value = false }
}

function toggle() {
  open.value = !open.value
  if (open.value) {
    if (list.value.length === 0) loadList()
    // 点击外部关闭
    setTimeout(() => document.addEventListener('click', closeOnOutside, { once: true }), 0)
  }
}
function closeOnOutside(e: MouseEvent) {
  if (panel.value && !panel.value.contains(e.target as Node)) open.value = false
}

async function markRead(n: SysNotification) {
  if (n.isRead) return
  try {
    await notificationApi.markRead(n.id)
    n.isRead = true
    if (unread.value > 0) unread.value--
  } catch {}
}
async function markAll() {
  try {
    await notificationApi.markAllRead()
    list.value.forEach(n => (n.isRead = true))
    unread.value = 0
  } catch {}
}

function typeBadge(t: number): string {
  return t === 1 ? 'bg-primary' : t === 2 ? 'bg-warning' : 'bg-info'
}
function typeLabel(t: number): string {
  return t === 1 ? '系统' : t === 2 ? '任务' : t === 3 ? '会话' : '通知'
}

let unsub: (() => void) | null = null
onMounted(async () => {
  await loadUnread()
  await startNotificationHub()
  unsub = onNotification(() => { unread.value++; loadUnread() })
})
onUnmounted(() => {
  unsub?.()
  document.removeEventListener('click', closeOnOutside)
})
</script>

<template>
  <div class="notify-bell" ref="panel">
    <button class="bell-btn" :class="{ 'is-active': open }" @click.stop="toggle" title="通知">
      <i class="ti ti-bell"></i>
      <span v-if="unread > 0" class="bell-badge">{{ unread > 99 ? '99+' : unread }}</span>
    </button>

    <Teleport to="body">
      <div v-if="open" class="notify-backdrop" @click="open = false"></div>
      <div v-if="open" class="notify-dropdown" :class="{ 'theme-dark': isDark }">
        <div class="notify-header">
          <span class="notify-title">通知</span>
          <button v-if="unread > 0" class="notify-mark-all" @click="markAll">全部已读</button>
        </div>

        <div class="notify-body">
          <div v-if="loading" class="notify-empty"><div class="spinner-border spinner-border-sm text-primary"></div></div>
          <div v-else-if="list.length === 0" class="notify-empty">
            <i class="ti ti-bell-off fs-2 text-muted mb-2 d-block"></i>
            <span class="text-muted">暂无通知</span>
          </div>
          <button
            v-for="n in list"
            :key="n.id"
            class="notify-item"
            :class="{ 'is-unread': !n.isRead }"
            @click="markRead(n)"
          >
            <span class="notify-item-icon" :class="typeBadge(n.type)">
              <i class="ti ti-bell"></i>
            </span>
            <div class="notify-item-body">
              <div class="notify-item-title">
                <template v-if="!n.isRead"><strong>{{ n.title }}</strong></template>
                <template v-else>{{ n.title }}</template>
                <span class="notify-item-tag" :class="typeBadge(n.type)">{{ typeLabel(n.type) }}</span>
              </div>
              <div class="notify-item-text">{{ n.content }}</div>
              <div class="notify-item-time">{{ fromNow(n.createdAt) }} · {{ formatTime(n.createdAt, 'MM-DD HH:mm') }}</div>
            </div>
          </button>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script lang="ts">
// dark mode detection
const darkQuery = typeof window !== 'undefined' ? window.matchMedia('(prefers-color-scheme: dark)') : null
const isDark = ref(darkQuery?.matches ?? false)
if (darkQuery) {
  darkQuery.addEventListener('change', (e) => { isDark.value = e.matches })
}
// also check Tabler theme attribute
onMounted(() => {
  const observer = new MutationObserver(() => {
    isDark.value = document.documentElement.classList.contains('dark')
  })
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
  isDark.value = document.documentElement.classList.contains('dark')
})
</script>

<style scoped>
.bell-btn {
  position: relative;
  width: 36px; height: 36px;
  display: inline-flex; align-items: center; justify-content: center;
  border: none; background: transparent;
  color: var(--el-text-color-primary);
  border-radius: 8px; cursor: pointer;
  font-size: 1.25rem; transition: background 0.15s;
}
.bell-btn:hover, .bell-btn.is-active { background: var(--el-fill-color-light); }

.bell-badge {
  position: absolute; top: 1px; right: 2px;
  min-width: 17px; height: 17px; line-height: 17px;
  text-align: center; font-size: 10px; font-weight: 600;
  background: var(--el-color-danger); color: #fff;
  border-radius: 10px; padding: 0 4px;
}

/* ===== 下拉面板 (Teleport to body) ===== */
.notify-backdrop {
  position: fixed; inset: 0; z-index: 1050;
  background: rgba(0,0,0,0.15);
  backdrop-filter: blur(3px);
  -webkit-backdrop-filter: blur(3px);
}
.notify-dropdown {
  position: fixed; top: 56px; right: 16px; z-index: 1055;
  width: 380px; max-height: min(540px, calc(100vh - 80px));
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color);
  border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.15);
  display: flex; flex-direction: column; overflow: hidden;
}
:global(html.dark) .notify-dropdown,
.notify-dropdown.theme-dark {
  background: #1e293b; border-color: #334155;
  box-shadow: 0 8px 32px rgba(0,0,0,0.5);
}

.notify-header {
  display: flex; justify-content: space-between; align-items: center;
  padding: 12px 16px; border-bottom: 1px solid var(--el-border-color);
  flex-shrink: 0;
}
.notify-title { font-weight: 600; font-size: 0.95rem; }
.notify-mark-all {
  background: none; border: none; color: var(--el-color-primary);
  font-size: 0.8rem; cursor: pointer; padding: 0;
}
.notify-mark-all:hover { text-decoration: underline; }

.notify-body { flex: 1; overflow-y: auto; }
.notify-empty {
  display: flex; flex-direction: column; align-items: center;
  padding: 32px 16px; font-size: 0.85rem;
}

.notify-item {
  display: flex; align-items: flex-start; gap: 10px;
  width: 100%; padding: 12px 16px;
  border: none; border-bottom: 1px solid var(--el-border-color-lighter);
  background: transparent; text-align: left; cursor: pointer;
  transition: background 0.1s;
}
.notify-item:last-child { border-bottom: none; }
.notify-item:hover { background: var(--el-fill-color-light); }
.notify-item.is-unread {
  background: var(--el-color-primary-light-9);
  border-left: 3px solid var(--el-color-primary);
}

.notify-item-icon {
  width: 36px; height: 36px; border-radius: 10px;
  display: inline-flex; align-items: center; justify-content: center;
  color: #fff; font-size: 0.9rem; flex-shrink: 0;
}
.notify-item-icon.bg-primary { background: var(--el-color-primary); }
.notify-item-icon.bg-warning { background: var(--el-color-warning); }
.notify-item-icon.bg-info { background: var(--el-color-info); }

.notify-item-body { flex: 1; min-width: 0; }
.notify-item-title {
  display: flex; justify-content: space-between; align-items: baseline;
  font-size: 0.85rem; gap: 8px; margin-bottom: 2px;
}
.notify-item-tag {
  font-size: 10px; padding: 1px 6px; border-radius: 4px;
  color: #fff; flex-shrink: 0; font-weight: 600;
  opacity: 0.85;
}
.notify-item-text {
  font-size: 0.78rem; color: var(--el-text-color-secondary);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  margin-bottom: 2px;
}
.notify-item-time {
  font-size: 0.7rem; color: var(--el-text-color-secondary); opacity: 0.7;
}
</style>
