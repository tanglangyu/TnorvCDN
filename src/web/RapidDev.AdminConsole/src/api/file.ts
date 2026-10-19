import { http } from '@/utils/request'
import type { SysFile, FileSignatureResult } from '@/types'

export const fileApi = {
  upload: (formData: FormData) => http.post<SysFile>('/file/upload', formData),
  list: (params: any) => http.get('/file/list', params),
  get: (id: number) => http.get<SysFile>(`/file/${id}`),
  remove: (id: number) => http.delete(`/file/${id}`),
  signature: (id: number, data: { mode: string; expirySeconds?: number; maxCount?: number }) =>
    http.post<FileSignatureResult>(`/file/${id}/signature`, data),
}
