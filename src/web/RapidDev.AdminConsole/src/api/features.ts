import { http } from '@/utils/request'

export const auditApi = {
  entityChanges: (params?: any) => http.get('/audit/entity-changes', params),
  entityHistory: (entityType: string, recordId: number) => http.get(`/audit/entity-changes/${entityType}/${recordId}`),
}

export const opsApi = {
  cacheInfo: () => http.get('/admin/cache/info'),
  cacheKeys: (params?: any) => http.get('/admin/cache/keys', params),
  cacheKeyDetail: (key: string) => http.get(`/admin/cache/keys/${encodeURIComponent(key)}`),
  clearKeys: (prefix: string, exact = false) => http.delete(`/admin/cache/keys?prefix=${encodeURIComponent(prefix)}&exact=${exact}`),
  backups: () => http.get('/admin/db/backups'),
  createBackup: (name?: string) => http.post(`/admin/db/backup${name ? `?name=${encodeURIComponent(name)}` : ''}`),
  downloadBackup: (fileName: string) => `${window.location.origin}/api/admin/db/backups/${fileName}`,
  deleteBackup: (fileName: string) => http.delete(`/admin/db/backups/${fileName}`),
  backupSchedule: () => http.get('/admin/db/schedule'),
  updateBackupSchedule: (data: any) => http.put('/admin/db/schedule', data),
}

export const healthApi = {
  dashboard: () => http.get('/health/dashboard'),
}

export const workflowApi = {
  definitions: (params?: any) => http.get('/workflow/definitions', params),
  createDef: (data: any) => http.post('/workflow/definitions', data),
  updateDef: (id: number, data: any) => http.put(`/workflow/definitions/${id}`, data),
  deleteDef: (id: number) => http.delete(`/workflow/definitions/${id}`),
  deploy: (id: number) => http.post(`/workflow/definitions/${id}/deploy`),
  start: (data: any) => http.post('/workflow/start', data),
  approve: (taskId: number, data: any) => http.post(`/workflow/tasks/${taskId}/approve`, data),
  delegate: (taskId: number, data: any) => http.post(`/workflow/tasks/${taskId}/delegate`, data),
  recall: (instanceId: number) => http.post(`/workflow/instances/${instanceId}/recall`),
  myTasks: () => http.get('/workflow/my-tasks'),
  myInstances: (status?: number) => http.get('/workflow/my-instances', { status }),
  instance: (id: number) => http.get(`/workflow/instances/${id}`),
}

export const reportApi = {
  query: (sql: string) => http.post('/report/query', { sql }),
  export: (sql: string) => http.post('/report/export', { sql }, { responseType: 'blob' }),
}

export const gatewayApi = {
  aggregate: (requests: any, baseUrl?: string) => http.post('/gateway/aggregate', { requests, baseUrl }),
}
