import { http } from '@/utils/request'
import type { SysJob, SysJobLog, RateLimitPolicy } from '@/types'

export interface ServerInfo {
  host: { hostname: string; osName: string; dotnetVersion: string; processorCount: number }
  jvm: { runtimeName: string; startTime: string; runTime: string; usedMemoryMb: number; totalMemoryMb: number; threadCount: number }
  sys: { cpuUsage: number; disk: Array<{ name: string; totalGb: number; freeGb: number; usageRate: number }> }
  timestamp: string
}

export interface OnlineUser {
  id: number
  userName: string
  nickName: string
  device?: string | null
  ip?: string | null
  loginAt?: string | null
  lastActiveAt?: string | null
  sessionCount: number
}

export const monitorApi = {
  server: () => http.get<ServerInfo>('/monitor/server'),
  onlineUsers: () => http.get<OnlineUser[]>('/monitor/server/online'),
  forceLogout: (userId: number) => http.delete(`/monitor/server/online/${userId}`),
}

export const jobApi = {
  list: (params: any) => http.get('/monitor/job/list', params),
  get: (id: number) => http.get<SysJob>(`/monitor/job/${id}`),
  add: (data: SysJob) => http.post('/monitor/job', data),
  edit: (id: number, data: SysJob) => http.put(`/monitor/job/${id}`, data),
  remove: (id: number) => http.delete(`/monitor/job/${id}`),
  pause: (id: number) => http.put(`/monitor/job/${id}/pause`),
  resume: (id: number) => http.put(`/monitor/job/${id}/resume`),
  run: (id: number) => http.post(`/monitor/job/${id}/run`),
  logs: (params: any) => http.get('/monitor/job/log/list', params),
}

export const rateLimitApi = {
  list: (params: any) => http.get('/monitor/ratelimit/list', params),
  get: (id: number) => http.get<RateLimitPolicy>(`/monitor/ratelimit/${id}`),
  add: (data: RateLimitPolicy) => http.post('/monitor/ratelimit', data),
  edit: (id: number, data: RateLimitPolicy) => http.put(`/monitor/ratelimit/${id}`, data),
  remove: (id: number) => http.delete(`/monitor/ratelimit/${id}`),
  changeStatus: (id: number, status: number) => http.put(`/monitor/ratelimit/${id}/status`, { status }),
}

export interface MqttBrokerStatus {
  isRunning: boolean
  enabled: boolean
  tcpPort: number
  enableWebSocket: boolean
  webSocketPath: string
  enableTls: boolean
  tlsPort: number
  clientCount: number
  retainedMessageCount: number
  startedAt?: string | null
  version: string
  totalMessagesPublished: number
  totalBytesPublished: number
  totalConnectCount: number
  uptimeSeconds: number
  messagesPerMinute: number
}
export interface MqttClientInfo {
  clientId: string
  username: string
  protocol: string
  remoteIp: string
  connectedAt: string
  lastActiveAt: string
  subscribedTopics: string[]
  messagesSent: number
  messagesReceived: number
}
export interface MqttRecentMessage {
  topic: string
  payload: string
  fromClientId: string
  time: string
  retained: boolean
  qos: number
}
export interface MqttRetainedMessageView {
  topic: string
  payload: string
  qos: number
  payloadBytes: number
}
export interface MqttTopicStat {
  topic: string
  subscriberCount: number
  isRetained: boolean
  recentMessageCount: number
}

export const mqttApi = {
  status: () => http.get<MqttBrokerStatus>('/monitor/mqtt'),
  clients: (params: any) => http.get('/monitor/mqtt/clients', params),
  clientDetail: (clientId: string) => http.get<MqttClientInfo>(`/monitor/mqtt/clients/${encodeURIComponent(clientId)}`),
  kick: (clientId: string) => http.post('/monitor/mqtt/kick', { clientId }),
  retained: () => http.get<MqttRetainedMessageView[]>('/monitor/mqtt/retained'),
  deleteRetained: (topic: string) => http.delete(`/monitor/mqtt/retained/${encodeURIComponent(topic)}`),
  clearRetained: () => http.delete('/monitor/mqtt/retained'),
  topics: () => http.get<MqttTopicStat[]>('/monitor/mqtt/topics'),
  testPublish: (data: { topic: string; payload: string; retained: boolean; qos: number }) => http.post('/monitor/mqtt/test-publish', data),
  testRecent: (topic?: string) => http.get<MqttRecentMessage[]>('/monitor/mqtt/test-recent', topic ? { topic } : {}),
  generateCert: (data: { commonName: string; sanDns?: string; sanIp?: string; password: string; validityYears: number }) =>
    http.post<{ pfxPath: string; thumbprint: string; notBefore: string; notAfter: string; commonName: string }>('/monitor/mqtt/generate-cert', data),
  restart: () => http.post('/monitor/mqtt/restart'),
}
