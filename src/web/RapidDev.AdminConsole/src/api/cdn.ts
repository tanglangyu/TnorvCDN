/**
 * TnorvCDN 主控 API 客户端。
 * 控制台与主控同源部署（/api 反代到主控），认证即框架登录态（JWT 由主控签发），
 * 无需二次配置连接。所有 CDN 页面经此读出主控数据。
 */
import { http } from '@/utils/request'

export const cdnApi = {
  get: <T = any>(url: string, params?: any) => http.get<T>(url, params),
  post: <T = any>(url: string, data?: any) => http.post<T>(url, data),
  put: <T = any>(url: string, data?: any) => http.put<T>(url, data),
  delete: <T = any>(url: string) => http.delete<T>(url),
}

export const cdnStore = {
  get connected(): boolean {
    return true // 同源架构：随框架登录即连接
  },
}
export const cdnLogin = async () => {}
export const cdnDisconnect = () => {}
