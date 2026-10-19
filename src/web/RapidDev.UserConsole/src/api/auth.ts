import { http } from '@/utils/request'

export interface GeeTestResult {
  lotNumber: string
  captchaOutput: string
  passToken: string
  genTime: string
}

export interface LoginInput {
  userName: string
  password: string
  uuid?: string
  code?: string
  rememberMe?: boolean
  // GeeTest v4 四要素
  lotNumber?: string
  captchaOutput?: string
  passToken?: string
  genTime?: string
  // 手机号登录:loginType=1 时用 phone+smsCode 免密登录
  loginType?: number
  phone?: string
  smsCode?: string
}

export const authApi = {
  /** 登录 */
  login: (data: LoginInput) => http.post('/auth/login', data),
  /** 登出 */
  logout: () => http.post('/auth/logout'),
  /** 刷新令牌 */
  refresh: (refreshToken: string) => http.post('/auth/refresh', { refreshToken }),
  /** 获取 GeeTest 验证配置 */
  getCaptcha: () => http.get<{ captchaId: string; enabled: boolean }>('/auth/captcha'),
  /** 获取当前用户信息 */
  getUserInfo: () => http.get('/auth/userinfo'),
  /** 获取当前用户菜单(动态路由) */
  getMenus: () => http.get('/auth/menus'),
  /** 获取当前用户权限码 */
  getPermissions: () => http.get('/auth/permissions'),
  /** 获取站点信息(登录页展示:名称/副标题/备案号等) */
  getSiteInfo: () => http.get('/auth/site-info'),
  /** 会话管理 */
  session: {
    /** 当前用户所有活跃会话(设备) */
    list: () => http.get('/auth/session/list'),
    /** 下线指定会话(仅自己的) */
    revoke: (sessionId: string) => http.delete(`/auth/session/${sessionId}`),
    /** 下线其他设备(保留当前会话) */
    revokeOthers: () => http.delete('/auth/session/others'),
    /** 下线全部会话(退出所有设备) */
    revokeAll: () => http.delete('/auth/session/all'),
    /** 管理员:查看指定用户的所有活跃会话 */
    listByUser: (userId: number) => http.get(`/auth/session/user/${userId}`),
    /** 管理员:强制下线指定用户全部会话 */
    revokeAllByUser: (userId: number) => http.delete(`/auth/session/user/${userId}`),
    /** 管理员:强制下线指定会话(踢单个设备) */
    forceRevoke: (sessionId: string) => http.delete(`/auth/session/${sessionId}/force`),
  },
}
