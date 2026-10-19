import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr'
import { useUserStore } from '@/store/user'
import { notificationApi } from '@/api/notification'
import router from '@/router'
import { formatTime } from '@/utils/time'

// 单例 SignalR 连接(全应用一份)。监听通知 + 会话被踢事件。
let connection: HubConnection | null = null
let connecting = false

type NotificationHandler = (n: { id: number; title: string; type?: number; nType?: number; createdAt?: string }) => void
type SessionKickedHandler = () => void

const notificationHandlers = new Set<NotificationHandler>()

/** 启动通知连接(登录后调用)。已连接则跳过。 */
export async function startNotificationHub() {
  const userStore = useUserStore()
  if (!userStore.token) return
  if (connection || connecting) return
  connecting = true
  try {
    connection = new HubConnectionBuilder()
      .withUrl('/hubs/notification', { accessTokenFactory: () => userStore.token || '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    // 通知事件(系统通知/任务事件)
    connection.on('notification', (payload: any) => {
      // 会话被踢:立即登出跳转
      if (payload?.type === 'session_kicked') {
        userStore.logout()
        showErrorToast(payload.title || '您的会话已下线', payload.content || '请重新登录')
        setTimeout(() => router.push(`/login?redirect=${encodeURIComponent(router.currentRoute.value.fullPath)}`), 500)
        return
      }
      // 普通通知:刷新未读数 + toast + 通知订阅者
      notificationHandlers.forEach((h) => h(payload))
      showErrorToast(payload.title || '新通知', payload.content || '')
    })

    await connection.start()
  } catch (e) {
    // 连接失败不阻断应用(下次请求或刷新重试)
    connection = null
  } finally {
    connecting = false
  }
}

/** 停止连接(登出时调用) */
export async function stopNotificationHub() {
  if (connection) {
    try { await connection.stop() } catch {}
    connection = null
  }
}

/** 订阅新通知(组件用,返回取消订阅函数) */
export function onNotification(handler: NotificationHandler): () => void {
  notificationHandlers.add(handler)
  return () => notificationHandlers.delete(handler)
}

function showErrorToast(title: string, body: string) {
  const el = document.createElement('div')
  el.className = 'toast-container position-fixed top-0 end-0 p-3'
  el.style.zIndex = '9999'
  el.innerHTML = `<div class="toast show" role="alert"><div class="toast-header"><i class="ti ti-bell me-2 text-primary"></i><strong class="me-auto">${title}</strong><small class="text-muted">${formatTime(new Date(), 'HH:mm')}</small><button type="button" class="btn-close" data-bs-dismiss="toast"></button></div><div class="toast-body">${body}</div></div>`
  document.body.appendChild(el)
  setTimeout(() => el.remove(), 4000)
}
