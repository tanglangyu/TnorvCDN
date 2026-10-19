import axios, { type AxiosInstance, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios'
import { useUserStore } from '@/store/user'
import router from '@/router'

/** 统一后端返回结构 */
export interface ApiResult<T = any> {
  code: number
  message: string
  data: T
}

const service: AxiosInstance = axios.create({
  baseURL: '/api',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json;charset=utf-8' },
  // ASP.NET Core [FromQuery] 需要的嵌套对象格式:filters[0].field=xx&filters[0].op=like
  paramsSerializer: {
    serialize: (params: Record<string, any>) => {
      const parts: string[] = []
      const encode = (v: string) => encodeURIComponent(v)
      function build(key: string, value: any) {
        if (value === null || value === undefined || value === '') return
        if (Array.isArray(value)) {
          value.forEach((item, i) => build(`${key}[${i}]`, item))
        } else if (typeof value === 'object') {
          for (const [k, v] of Object.entries(value)) build(`${key}.${k}`, v)
        } else {
          parts.push(`${encode(key)}=${encode(String(value))}`)
        }
      }
      for (const [k, v] of Object.entries(params)) build(k, v)
      return parts.join('&')
    },
  },
})

// ========== 加密通道:根据持久化的 crypto mode 自动安装 adapter ==========
// 这样不依赖 pinia store 初始化时机,也避免 HMR 热更新覆盖 adapter
;(function bootCrypto() {
  try {
    const raw = localStorage.getItem('rapiddev:crypto')
    if (!raw) return
    const parsed = JSON.parse(raw)
    const mode = parsed?.mode
    if (!mode || mode === 'off') return
    // 延迟 import,等 vite 把模块解析完
    import('@/utils/rapiddev-crypto-sdk.js').then(({ installCryptoInterceptor }) => {
      installCryptoInterceptor(service, {
        baseUrl: window.location.origin,
        mode,
        debug: false,
      })
      console.log('[Crypto] booted with mode=' + mode)
    })
  } catch {}
})()

// 请求拦截器:自动带 token
service.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const userStore = useUserStore()
    if (userStore.token) {
      config.headers.Authorization = `Bearer ${userStore.token}`
    }
    return config
  },
  (error) => Promise.reject(error),
)

// 响应拦截器:统一处理业务码
let hasHandled401 = false      // 防止并发 401 导致多次跳转/多次提示
service.interceptors.response.use(
  // 注意:此处有意拆包返回 ApiResult(而非 AxiosResponse),由下方 request() 统一取 .data。
  (response: any) => {
    const res = response.data as ApiResult
    // 二进制流(文件下载)直接返回
    if (response.config.responseType === 'blob') {
      return response
    }
    // 非 Result 结构(如健康检查),直接返回
    if (res?.code === undefined) {
      return res
    }
    if (res.code === 200) {
      return res
    }
    // 401:会话/令牌失效,按 reason 细分提示
    if (res.code === 401) {
      handleUnauthorized((res as any).reason, res.message)
      return Promise.reject(new Error(res.message || '未授权'))
    }
    // 其他业务错误:提示但不跳转
    showError(res.message)
    return Promise.reject(new Error(res.message || 'Error'))
  },
  async (error) => {
    const status = error?.response?.status
    if (status === 401) {
      const reason = error?.response?.data?.reason
      const msg = error?.response?.data?.message
      handleUnauthorized(reason, msg)
    } else if (status === 403) {
      showError('没有权限执行此操作')
    } else {
      showError(error?.response?.data?.message || error.message || '请求失败')
    }
    return Promise.reject(error)
  },
)

function handleUnauthorized(reason?: string, msg?: string) {
  // 已在登录页,不做任何跳转/提示(避免登录接口本身的 401 触发这些逻辑)
  const path = router.currentRoute.value.path
  if (path === '/login') return

  // 幂等:同一次会话过期期间只处理一次
  if (hasHandled401) return
  hasHandled401 = true

  const userStore = useUserStore()
  userStore.logout()

  const tip =
    reason === 'session_expired' ? (msg || '您的会话已失效(被踢下线或在他处登录),请重新登录') :
    reason === 'token_revoked' ? '您已登出,请重新登录' :
    reason === 'token_expired' ? '登录已过期,请重新登录' :
    reason === 'no_token' ? '请先登录' :
    (msg || '登录已失效,请重新登录')
  showError(tip)

  // 立即跳转,不等 toast(toast 3.5s 后自动消失,不阻塞导航)
  const redirect = encodeURIComponent(router.currentRoute.value.fullPath)
  router.replace(`/login?redirect=${redirect}`).finally(() => {
    // 登录页加载后允许再次处理(用户可能会在登录页再触发,但已被上面 path==='/login' 拦截)
    setTimeout(() => { hasHandled401 = false }, 2000)
  })
}

// 全局错误提示(Element Plus message,替代旧 Tabler toast 垫片)
function showError(msg: string) {
  console.error('[API Error]', msg)
  import('element-plus').then(({ ElMessage }) => ElMessage.error(msg || '请求失败'))
}

// 封装请求方法(自动拆包 data)
export function request<T = any>(config: AxiosRequestConfig): Promise<T> {
  return service(config).then((res: any) => (res?.data !== undefined ? res.data : res) as T)
}

export const http = {
  get: <T = any>(url: string, params?: any, config?: AxiosRequestConfig) =>
    request<T>({ url, method: 'get', params, ...config }),
  post: <T = any>(url: string, data?: any, config?: AxiosRequestConfig) =>
    request<T>({ url, method: 'post', data, ...config }),
  put: <T = any>(url: string, data?: any, config?: AxiosRequestConfig) =>
    request<T>({ url, method: 'put', data, ...config }),
  delete: <T = any>(url: string, config?: AxiosRequestConfig) =>
    request<T>({ url, method: 'delete', ...config }),
  upload: <T = any>(url: string, formData: FormData, config?: AxiosRequestConfig) =>
    request<T>({ url, method: 'post', data: formData, headers: { 'Content-Type': 'multipart/form-data' }, ...config }),
}

export default service
