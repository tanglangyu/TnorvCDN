/**
 * TnorvCDN 主控 API 客户端（用户端）。
 * 控制台与主控同源部署，认证即框架登录态（JWT 由主控签发）。
 */
import { http } from '@/utils/request'

export const cdnApi = {
  get: <T = any>(url: string, params?: any) => http.get<T>(url, params),
  post: <T = any>(url: string, data?: any) => http.post<T>(url, data),
  put: <T = any>(url: string, data?: any) => http.put<T>(url, data),
  delete: <T = any>(url: string) => http.delete<T>(url),
}

export const cdnStore = { get connected(): boolean { return true } }
export const cdnLogin = async () => {}
export const cdnDisconnect = () => {}
