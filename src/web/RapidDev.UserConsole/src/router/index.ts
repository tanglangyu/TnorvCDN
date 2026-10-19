import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useUserStore } from '@/store/user'
import { useCryptoStore } from '@/store/crypto'
import { useSiteStore } from '@/store/site'
import NProgress from 'nprogress'
import 'nprogress/nprogress.css'

NProgress.configure({ showSpinner: false })

// 静态路由(无需登录)
export const staticRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'Login',
    component: () => import('@/views/login/index.vue'),
    meta: { title: '登录', hidden: true },
  },
  {
    path: '/404',
    name: 'NotFound',
    component: () => import('@/views/error/404.vue'),
    meta: { title: '404', hidden: true },
  },
]

// 所有后台页面统一挂载在同一个 Layout 下,确保侧边栏状态全局一致
const appRoutes: RouteRecordRaw = {
  path: '/',
  component: () => import('@/layouts/default/index.vue'),
  children: [
    {
      path: '/dashboard',
      name: 'Dashboard',
      component: () => import('@/views/dashboard/index.vue'),
      meta: { title: '概览', icon: 'chart-line', affix: true },
    },
    {
      path: '/profile',
      name: 'Profile',
      component: () => import('@/views/profile/index.vue'),
      meta: { title: '个人中心', hidden: true },
    },
    // ===== CDN 用户端模块(通用设计) =====
    // CDN加速
    { path: '/servers', name: 'MyServers', component: () => import('@/views/cdn/servers.vue'), meta: { title: '我的网站', module: 'CDN加速', icon: 'world', perm: 'user:server:list' } },
    { path: '/servers/config/:id', name: 'ServerConfig', component: () => import('@/views/cdn/server-settings/index.vue'), meta: { title: '站点设置', module: 'CDN加速', icon: 'settings', hidden: true, perm: 'user:server:list' } },
    { path: '/servers/cache', name: 'ServerCache', component: () => import('@/views/cdn/cache.vue'), meta: { title: '刷新预热', module: 'CDN加速', icon: 'refresh', perm: 'user:server:cache' } },
    { path: '/servers/certs', name: 'MyCerts', component: () => import('@/views/cdn/mycerts.vue'), meta: { title: '证书管理', module: 'CDN加速', icon: 'certificate', perm: 'user:cert:list' } },
    { path: '/servers/cert-events', name: 'MyCertEvents', component: () => import('@/views/cdn/mycert_events.vue'), meta: { title: '证书签发日志', module: 'CDN加速', icon: 'scroll-text', perm: 'user:cert:list' } },
    { path: '/servers/icp', name: 'MyIcp', component: () => import('@/views/cdn/icp.vue'), meta: { title: '备案管理', module: 'CDN加速', icon: 'clipboard-check', perm: 'user:server:list' } },
    { path: '/servers/traffic-stats', name: 'MyTrafficStats', component: () => import('@/views/cdn/traffic.vue'), meta: { title: '用量统计', module: 'CDN加速', icon: 'chart-area', perm: 'user:stat:list' } },
    // 负载均衡
    { path: '/lb', name: 'LoadBalance', component: () => import('@/views/cdn/lb.vue'), meta: { title: '负载均衡', module: '负载均衡', icon: 'arrows-split-2', perm: 'user:lb:list' } },
    // WAF安全
    { path: '/waf', name: 'WafOverview', component: () => import('@/views/cdn/waf.vue'), meta: { title: '概览', module: 'WAF安全', icon: 'shield-half', perm: 'user:waf:list' } },
    { path: '/waf/logs', name: 'WafLogs', component: () => import('@/views/cdn/waflogs.vue'), meta: { title: '拦截日志', module: 'WAF安全', icon: 'file-text', perm: 'user:waf:list' } },
    { path: '/waf/iplists', name: 'WafIpLists', component: () => import('@/views/cdn/waf_iplists.vue'), meta: { title: '拦截IP', module: 'WAF安全', icon: 'list-details', perm: 'user:waf:list' } },
    { path: '/waf/settings', name: 'WafSettings', component: () => import('@/views/cdn/waf_settings.vue'), meta: { title: '全局规则', module: 'WAF安全', icon: 'settings', perm: 'user:waf:list' } },
    // 套餐管理
    { path: '/plans', name: 'UserPlans', component: () => import('@/views/cdn/plans.vue'), meta: { title: '套餐管理', module: '套餐管理', icon: 'gift', perm: 'user:plan:list' } },
    // DDoS高防
    { path: '/anti-ddos/instances', name: 'AntiDdosInstances', component: () => import('@/views/cdn/antiddos.vue'), meta: { title: '实例列表', module: 'DDoS高防', icon: 'shield-lock', perm: 'user:antiddos:list' } },
    { path: '/anti-ddos/packages', name: 'AntiDdosPackages', component: () => import('@/views/cdn/antiddos_packages.vue'), meta: { title: '购买实例', module: 'DDoS高防', icon: 'shopping-cart', perm: 'user:antiddos:list' } },
    // 域名解析
    { path: '/ns/access', name: 'NsAccess', component: () => import('@/views/cdn/ns-access.vue'), meta: { title: 'NS托管接入', module: '域名解析', icon: 'cloud', perm: 'user:ns:list' } },
    { path: '/ns/domains', name: 'NsDomains', component: () => import('@/views/cdn/nsdomains.vue'), meta: { title: '我的域名', module: '域名解析', icon: 'world', perm: 'user:ns:list' } },
    { path: '/ns/domains/groups', name: 'NsDomainGroups', component: () => import('@/views/cdn/ns_groups.vue'), meta: { title: '域名分组', module: '域名解析', icon: 'folder', perm: 'user:ns:list' } },
    { path: '/ns/domains/batch', name: 'NsBatch', component: () => import('@/views/cdn/ns_batch.vue'), meta: { title: '批量操作', module: '域名解析', icon: 'stack', perm: 'user:ns:list' } },
    { path: '/ns/routes', name: 'NsRoutes', component: () => import('@/views/cdn/ns_routes.vue'), meta: { title: '线路管理', module: '域名解析', icon: 'route', perm: 'user:ns:list' } },
    { path: '/ns/plans', name: 'NsPlans', component: () => import('@/views/cdn/ns_plans.vue'), meta: { title: '套餐', module: '域名解析', icon: 'ticket', perm: 'user:ns:list' } },
    // 财务管理
    { path: '/finance', name: 'FinanceOverview', component: () => import('@/views/cdn/finance.vue'), meta: { title: '概览', module: '财务管理', icon: 'wallet', perm: 'user:finance:list' } },
    { path: '/finance/bills', name: 'FinanceBills', component: () => import('@/views/cdn/finance_bills.vue'), meta: { title: '费用账单', module: '财务管理', icon: 'receipt', perm: 'user:finance:list' } },
    { path: '/finance/logs', name: 'FinanceLogs', component: () => import('@/views/cdn/finance_logs.vue'), meta: { title: '收支明细', module: '财务管理', icon: 'arrows-left-right', perm: 'user:finance:list' } },
    { path: '/finance/charge', name: 'FinanceCharge', component: () => import('@/views/cdn/finance_charge.vue'), meta: { title: '充值', module: '财务管理', icon: 'coin', perm: 'user:finance:list' } },
    { path: '/finance/coupons', name: 'FinanceCoupons', component: () => import('@/views/cdn/finance_coupons.vue'), meta: { title: '优惠券', module: '财务管理', icon: 'discount', perm: 'user:finance:list' } },
    // 工单系统
    { path: '/tickets', name: 'MyTickets', component: () => import('@/views/cdn/tickets.vue'), meta: { title: '工单系统', module: '工单系统', icon: 'messages', perm: 'user:ticket:list' } },
    // 访问控制
    { path: '/acl', name: 'AccessControl', component: () => import('@/views/cdn/acl.vue'), meta: { title: '访问控制', module: '访问控制', icon: 'lock', perm: 'user:acl:list' } },
    // 消息
    { path: '/messages', name: 'MyMessages', component: () => import('@/views/cdn/messages.vue'), meta: { title: '消息', module: '消息', icon: 'bell', perm: 'user:message:list' } },
    { path: '/notify', name: 'NotifySettings', component: () => import('@/views/cdn/notify.vue'), meta: { title: '消息提醒设置', module: '消息', icon: 'bell-ringing', perm: 'user:message:list' } },
  ],
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    ...staticRoutes,
    appRoutes,
    { path: '/:pathMatch(.*)*', redirect: '/404' },
  ],
})

// 全局前置守卫
const whiteList = ['/login', '/404']

	router.beforeEach(async (to, _from, next) => {
  NProgress.start()
  const userStore = useUserStore()
  const cryptoStore = useCryptoStore()
  const siteStore = useSiteStore()
  const siteName = (to.meta.title as string) || ''
  document.title = siteName ? `${siteName} - ${siteStore.name}` : siteStore.name

  // 加密切换进行中时等待(最多 3s),避免切换中导航导致请求走半装好的 adapter
  if (cryptoStore.locked) {
    const start = Date.now()
    while (cryptoStore.locked && Date.now() - start < 3000) {
      await new Promise(r => setTimeout(r, 50))
    }
  }

  // 客户端本地过期检查:token 已过期直接清并跳登录(避免让页面渲染后再撞 401)
  // expiresAt 是 Unix 秒;留 10s 余量应对时钟偏差
  const now = Math.floor(Date.now() / 1000)
  if (userStore.token && userStore.expiresAt && userStore.expiresAt <= now + 10) {
    userStore.logout()
    if (to.path !== '/login') {
      next(`/login?redirect=${encodeURIComponent(to.fullPath)}&reason=token_expired`)
      return
    }
  }

  if (userStore.token) {
    if (to.path === '/login') {
      next('/')
    } else {
      // 已登录但无用户信息,先拉取
      if (!userStore.userInfo) {
        try {
          await userStore.fetchUserInfo()
        } catch {
          userStore.logout()
          next('/login')
          return
        }
      }
      // 路由级权限校验:超管跳过,白名单跳过,无 perm 的路由跳过
      if (!userStore.isSuperAdmin && !whiteList.includes(to.path)) {
        const routePerm = to.meta?.perm as string | undefined
        if (routePerm && !userStore.hasPermission(routePerm)) {
          // 无权限 → 跳 403 或首页
          next('/dashboard')
          return
        }
      }
      next()
    }
  } else {
    if (whiteList.includes(to.path)) {
      next()
    } else {
      next(`/login?redirect=${encodeURIComponent(to.fullPath)}`)
    }
  }
})

router.afterEach(() => {
  NProgress.done()
})

export default router

/** CDN 占位页面组件工厂 */
function Cdn(_title: string) {
  return () => import('@/views/cdn/placeholder.vue')
}