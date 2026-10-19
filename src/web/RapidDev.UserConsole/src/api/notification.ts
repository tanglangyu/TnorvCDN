import { http } from '@/utils/request'
import type { SysNotification } from '@/types'

export const notificationApi = {
  list: (params: any) => http.get('/notification/list', params),
  unreadCount: () => http.get<number>('/notification/unread-count'),
  markRead: (id: number) => http.put(`/notification/${id}/read`),
  markAllRead: () => http.put('/notification/read-all'),
  remove: (id: number) => http.delete(`/notification/${id}`),
  clearRead: () => http.delete<number>('/notification/read'),
  send: (data: { userId: number; title: string; content?: string }) => http.post('/notification/send', data),
}
