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
      meta: { title: '首页', icon: 'home', affix: true },
    },
    // ===== CDN 业务模块(通用设计) =====
    // 数据看板
    { path: '/cdn/dashboard/waf', name: 'CdnDashWaf', component: () => import('@/views/cdn/dashwaf.vue'), meta: { title: 'WAF', module: '数据看板', icon: 'shield-half' } },
    { path: '/cdn/dashboard/dns', name: 'CdnDashDns', component: () => import('@/views/cdn/dashdns.vue'), meta: { title: 'DNS', module: '数据看板', icon: 'affiliate' } },
    { path: '/cdn/dashboard/user', name: 'CdnDashUser', component: () => import('@/views/cdn/dashuser.vue'), meta: { title: '用户', module: '数据看板', icon: 'users-group' } },
    { path: '/cdn/dashboard/events', name: 'CdnDashEvents', component: () => import('@/views/cdn/dashevents.vue'), meta: { title: '事件', module: '数据看板', icon: 'alert-triangle' } },
    // 网站列表
    { path: '/cdn/servers', name: 'CdnServers', component: () => import('@/views/cdn/servers.vue'), meta: { title: '网站列表', module: '网站列表', icon: 'world', perm: 'cdn:server:list' } },
    { path: '/cdn/servers/config/:id', name: 'CdnServerConfig', component: () => import('@/views/cdn/server-settings/index.vue'), meta: { title: '站点设置', module: '网站列表', icon: 'settings', hidden: true, perm: 'cdn:server:list' } },
    { path: '/cdn/servers/logs', name: 'CdnServerLogs', component: () => import('@/views/cdn/accesslogs.vue'), meta: { title: '访问日志', module: '网站列表', icon: 'file-text', perm: 'cdn:server:log' } },
    { path: '/cdn/servers/groups', name: 'CdnServerGroups', component: () => import('@/views/cdn/groups.vue'), meta: { title: '网站分组', module: '网站列表', icon: 'folder', perm: 'cdn:server:list' } },
    { path: '/cdn/servers/certs', name: 'CdnCerts', component: () => import('@/views/cdn/certs.vue'), meta: { title: '证书管理', module: '网站列表', icon: 'certificate', perm: 'cdn:cert:list' } },
    { path: '/cdn/servers/cert-events', name: 'CdnCertEvents', component: () => import('@/views/cdn/cert_events.vue'), meta: { title: '证书签发日志', module: '网站列表', icon: 'scroll-text', perm: 'cdn:cert:list' } },
    { path: '/cdn/servers/traffic-stats', name: 'CdnTrafficStats', component: () => import('@/views/cdn/traffic.vue'), meta: { title: '用量统计', module: '网站列表', icon: 'chart-area', perm: 'cdn:stat:list' } },
    { path: '/cdn/servers/components/cache', name: 'CdnCachePolicy', component: () => import('@/views/cdn/cache_policy.vue'), meta: { title: '缓存策略', module: '网站列表', icon: 'database', perm: 'cdn:cache:list' } },
    { path: '/cdn/servers/components/cache/batch', name: 'CdnCacheBatch', component: () => import('@/views/cdn/cache_batch.vue'), meta: { title: '刷新预热', module: '网站列表', icon: 'refresh', perm: 'cdn:cache:batch' } },
    { path: '/cdn/servers/components/waf', name: 'CdnWafPolicy', component: () => import('@/views/cdn/waf_policy.vue'), meta: { title: 'WAF策略', module: '网站列表', icon: 'shield', perm: 'cdn:waf:list' } },
    { path: '/cdn/servers/iplists', name: 'CdnIpLists', component: () => import('@/views/cdn/iplists.vue'), meta: { title: 'IP名单', module: '网站列表', icon: 'list-details', perm: 'cdn:waf:list' } },
    { path: '/cdn/servers/cc', name: 'CdnCcEvents', component: () => import('@/views/cdn/cc.vue'), meta: { title: 'CC事件', module: '网站列表', icon: 'bolt', perm: 'cdn:waf:list' } },
    { path: '/cdn/servers/deleted-contents', name: 'CdnDeletedContents', component: () => import('@/views/cdn/deleted_contents.vue'), meta: { title: '内容封禁', module: '网站列表', icon: 'ban', perm: 'cdn:waf:list' } },
    { path: '/cdn/servers/accesslogs', name: 'CdnLogPolicy', component: () => import('@/views/cdn/log_policy.vue'), meta: { title: '日志策略', module: '网站列表', icon: 'file-description', perm: 'cdn:log:list' } },
    { path: '/cdn/servers/metrics', name: 'CdnMetrics', component: () => import('@/views/cdn/metrics.vue'), meta: { title: '统计指标', module: '网站列表', icon: 'chart-dots', perm: 'cdn:stat:list' } },
    { path: '/cdn/servers/user-scripts', name: 'CdnUserScripts', component: () => import('@/views/cdn/user-scripts.vue'), meta: { title: '边缘脚本', module: '网站列表', icon: 'script', perm: 'cdn:script:list' } },
    // 边缘节点
    { path: '/cdn/clusters', name: 'CdnClusters', component: () => import('@/views/cdn/clusters.vue'), meta: { title: '集群列表', module: '边缘节点', icon: 'server-2', perm: 'cdn:cluster:list' } },
    { path: '/cdn/clusters/logs', name: 'CdnClusterLogs', component: () => import('@/views/cdn/clusters_logs.vue'), meta: { title: '节点日志', module: '边缘节点', icon: 'history', perm: 'cdn:cluster:log' } },
    { path: '/cdn/clusters/ip-addrs', name: 'CdnClusterIps', component: () => import('@/views/cdn/nodes.vue'), meta: { title: '节点IP', module: '边缘节点', icon: 'ip', perm: 'cdn:cluster:list' } },
    { path: '/cdn/clusters/regions', name: 'CdnRegions', component: () => import('@/views/cdn/clusters_regions.vue'), meta: { title: '区域设置', module: '边缘节点', icon: 'map-pin', perm: 'cdn:region:list' } },
    { path: '/cdn/clusters/grants', name: 'CdnSshGrants', component: () => import('@/views/cdn/clusters_grants.vue'), meta: { title: '节点SSH', module: '边缘节点', icon: 'key', perm: 'cdn:cluster:ssh' } },
    { path: '/cdn/clusters/index-nodes', name: 'CdnIndexNodes', component: () => import('@/views/cdn/clusters_index.vue'), meta: { title: '索引节点', module: '边缘节点', icon: 'hierarchy', perm: 'cdn:cluster:list' } },
    { path: '/cdn/clusters/monitors', name: 'CdnMonitors', component: () => import('@/views/cdn/monitors.vue'), meta: { title: '区域监控', module: '边缘节点', icon: 'activity', perm: 'cdn:monitor:list' } },
    { path: '/cdn/clusters/anti-ddos', name: 'CdnAntiDdos', component: () => import('@/views/cdn/antiddos.vue'), meta: { title: '高防IP', module: '边缘节点', icon: 'shield-lock', perm: 'cdn:antiddos:list' } },
    // 域名解析(第三方DNS同步)
    { path: '/cdn/dns', name: 'CdnDnsClusters', component: () => import('@/views/cdn/dns.vue'), meta: { title: '集群列表', module: '域名解析', icon: 'cloud', perm: 'cdn:dns:list' } },
    { path: '/cdn/dns/providers', name: 'CdnDnsProviders', component: () => import('@/views/cdn/dns_providers.vue'), meta: { title: 'DNS服务商', module: '域名解析', icon: 'plug', perm: 'cdn:dns:list' } },
    { path: '/cdn/dns/issues', name: 'CdnDnsIssues', component: () => import('@/views/cdn/dns_issues.vue'), meta: { title: '问题修复', module: '域名解析', icon: 'tool', perm: 'cdn:dns:list' } },
    { path: '/cdn/dns/icp', name: 'CdnIcp', component: () => import('@/views/cdn/icp.vue'), meta: { title: '备案审核', module: '域名解析', icon: 'clipboard-check', perm: 'cdn:dns:list' } },
    // 智能DNS
    { path: '/cdn/ns', name: 'CdnNsDashboard', component: () => import('@/views/cdn/dashdns.vue'), meta: { title: '数据看板', module: '智能DNS', icon: 'dashboard', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/domains', name: 'CdnNsDomains', component: () => import('@/views/cdn/ns_domains.vue'), meta: { title: '域名管理', module: '智能DNS', icon: 'world', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/domains/groups', name: 'CdnNsDomainGroups', component: () => import('@/views/cdn/ns_groups.vue'), meta: { title: '域名分组', module: '智能DNS', icon: 'folder', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/domains/batch', name: 'CdnNsBatch', component: () => import('@/views/cdn/ns_batch.vue'), meta: { title: '批量操作', module: '智能DNS', icon: 'stack', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/clusters', name: 'CdnNsClusters', component: () => import('@/views/cdn/ns_clusters.vue'), meta: { title: '集群管理', module: '智能DNS', icon: 'server', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/routes', name: 'CdnNsRoutes', component: () => import('@/views/cdn/ns_routes.vue'), meta: { title: '线路管理', module: '智能DNS', icon: 'route', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/settings', name: 'CdnNsSettings', component: () => import('@/views/cdn/ns_settings.vue'), meta: { title: '全局配置', module: '智能DNS', icon: 'settings', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/userPlans', name: 'CdnNsUserPlans', component: () => import('@/views/cdn/ns_userplans.vue'), meta: { title: '用户套餐', module: '智能DNS', icon: 'package', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/plans', name: 'CdnNsPlans', component: () => import('@/views/cdn/ns_plans.vue'), meta: { title: '套餐设置', module: '智能DNS', icon: 'ticket', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/clusters/accessLogs', name: 'CdnNsAccessLogs', component: () => import('@/views/cdn/ns_accesslogs.vue'), meta: { title: '访问日志', module: '智能DNS', icon: 'file-text', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/clusters/logs', name: 'CdnNsRunLogs', component: () => import('@/views/cdn/ns_runlogs.vue'), meta: { title: '运行日志', module: '智能DNS', icon: 'history', perm: 'cdn:ns:list' } },
    { path: '/cdn/ns/test', name: 'CdnNsTest', component: () => import('@/views/cdn/ns_test.vue'), meta: { title: '解析测试', module: '智能DNS', icon: 'test-pipe', perm: 'cdn:ns:list' } },
    // 平台用户
    { path: '/cdn/users', name: 'CdnUsers', component: () => import('@/views/cdn/users_list.vue'), meta: { title: '用户列表', module: '平台用户', icon: 'users', perm: 'cdn:user:list' } },
    { path: '/cdn/users/setting', name: 'CdnUserSettings', component: () => import('@/views/cdn/users_setting.vue'), meta: { title: '用户设置', module: '平台用户', icon: 'adjustments', perm: 'cdn:user:list' } },
    { path: '/cdn/users/invitations', name: 'CdnInvitations', component: () => import('@/views/cdn/users_invitations.vue'), meta: { title: '邀请码', module: '平台用户', icon: 'mail-forward', perm: 'cdn:user:list' } },
    // 财务管理
    { path: '/cdn/finance/bills', name: 'CdnFinanceBills', component: () => import('@/views/cdn/finance_bills.vue'), meta: { title: '费用账单', module: '财务管理', icon: 'receipt', perm: 'cdn:finance:list' } },
    { path: '/cdn/finance/accounts', name: 'CdnFinanceAccounts', component: () => import('@/views/cdn/finance_accounts.vue'), meta: { title: '用户账户', module: '财务管理', icon: 'wallet', perm: 'cdn:finance:list' } },
    { path: '/cdn/finance/logs', name: 'CdnFinanceLogs', component: () => import('@/views/cdn/finance_logs.vue'), meta: { title: '收支明细', module: '财务管理', icon: 'arrows-left-right', perm: 'cdn:finance:list' } },
    { path: '/cdn/finance/orders', name: 'CdnFinanceOrders', component: () => import('@/views/cdn/finance_orders.vue'), meta: { title: '订单管理', module: '财务管理', icon: 'shopping-cart', perm: 'cdn:finance:list' } },
    { path: '/cdn/finance/income', name: 'CdnFinanceIncome', component: () => import('@/views/cdn/finance_income.vue'), meta: { title: '统计报表', module: '财务管理', icon: 'chart-bar', perm: 'cdn:finance:list' } },
    { path: '/cdn/finance/fee', name: 'CdnFeeSettings', component: () => import('@/views/cdn/finance_fee.vue'), meta: { title: '计费设置', module: '财务管理', icon: 'calculator', perm: 'cdn:finance:list' } },
    { path: '/cdn/finance/packages', name: 'CdnTrafficPackages', component: () => import('@/views/cdn/finance_packages.vue'), meta: { title: '流量包', module: '财务管理', icon: 'box', perm: 'cdn:finance:list' } },
    // 套餐管理
    { path: '/cdn/plans', name: 'CdnPlans', component: () => import('@/views/cdn/plans_list.vue'), meta: { title: '套餐列表', module: '套餐管理', icon: 'gift', perm: 'cdn:plan:list' } },
    { path: '/cdn/plans/categories', name: 'CdnPlanCategories', component: () => import('@/views/cdn/plans_categories.vue'), meta: { title: '套餐分类', module: '套餐管理', icon: 'category', perm: 'cdn:plan:list' } },
    { path: '/cdn/plans/userPlans', name: 'CdnUserPlans', component: () => import('@/views/cdn/plans_userplans.vue'), meta: { title: '已购套餐', module: '套餐管理', icon: 'shopping bag', perm: 'cdn:plan:list' } },
    { path: '/cdn/plans/coupons', name: 'CdnCoupons', component: () => import('@/views/cdn/plans_coupons.vue'), meta: { title: '优惠券', module: '套餐管理', icon: 'discount', perm: 'cdn:plan:list' } },
    { path: '/cdn/plans/coupons/user-coupons', name: 'CdnUserCoupons', component: () => import('@/views/cdn/plans_user_coupons.vue'), meta: { title: '已领优惠券', module: '套餐管理', icon: 'discount-2', perm: 'cdn:plan:list' } },
    { path: '/cdn/plans/quota-packages', name: 'CdnQuotaPackages', component: () => import('@/views/cdn/plans_quota.vue'), meta: { title: '资源包', module: '套餐管理', icon: 'package', perm: 'cdn:plan:list' } },
    { path: '/cdn/plans/quota-packages/user-quota-packages', name: 'CdnUserQuotaPackages', component: () => import('@/views/cdn/plans_quota_user.vue'), meta: { title: '已购资源包', module: '套餐管理', icon: 'box-multiple', perm: 'cdn:plan:list' } },
    // 工单系统
    { path: '/cdn/tickets', name: 'CdnTickets', component: () => import('@/views/cdn/tickets.vue'), meta: { title: '工单', module: '工单系统', icon: 'messages', perm: 'cdn:ticket:list' } },
    { path: '/cdn/tickets/categories', name: 'CdnTicketCategories', component: () => import('@/views/cdn/ticketcats.vue'), meta: { title: '分类', module: '工单系统', icon: 'tags', perm: 'cdn:ticket:list' } },
    // 管理员
    { path: '/cdn/admins', name: 'CdnAdmins', component: () => import('@/views/cdn/admins.vue'), meta: { title: '管理员列表', module: '管理员', icon: 'user-shield', perm: 'cdn:admin:list' } },
    { path: '/cdn/admins/recipients', name: 'CdnRecipients', component: () => import('@/views/cdn/admins_recipients.vue'), meta: { title: '通知媒介', module: '管理员', icon: 'bell', perm: 'cdn:admin:list' } },
    { path: '/cdn/messages', name: 'CdnMessages', component: () => import('@/views/cdn/messages.vue'), meta: { title: '消息中心', module: '管理员', icon: 'mail', perm: 'cdn:admin:list' } },
    { path: '/cdn/posts', name: 'CdnPosts', component: () => import('@/views/cdn/posts.vue'), meta: { title: '公告管理', module: '管理员', icon: 'speakerphone', perm: 'cdn:setting:list' } },
    { path: '/cdn/ipbox', name: 'CdnIpbox', component: () => import('@/views/cdn/ipbox.vue'), meta: { title: 'IP 盒', module: 'CDN管理', icon: 'search', perm: 'cdn:server:list' } },
    { path: '/cdn/identities', name: 'CdnIdentities', component: () => import('@/views/cdn/identities.vue'), meta: { title: '实名认证', module: '平台用户', icon: 'certificate', perm: 'cdn:user:list' } },
    // 系统设置
    { path: '/cdn/settings', name: 'CdnSettings', component: () => import('@/views/cdn/settings_basic.vue'), meta: { title: '基础设置', module: '系统设置', icon: 'settings', perm: 'cdn:setting:list' } },
    { path: '/cdn/settings/advanced', name: 'CdnSettingsAdvanced', component: () => import('@/views/cdn/settings_advanced.vue'), meta: { title: '高级设置', module: '系统设置', icon: 'settings-2', perm: 'cdn:setting:list' } },
    { path: '/cdn/settings/acme-accounts', name: 'CdnAcmeAccounts', component: () => import('@/views/cdn/acme_accounts.vue'), meta: { title: 'ACME 签发账户', module: '系统设置', icon: 'key', perm: 'cdn:setting:list' } },
    { path: '/cdn/settings/authority', name: 'CdnSettingsAuthority', component: () => import('@/views/cdn/settings_authority.vue'), meta: { title: '商业版本', module: '系统设置', icon: 'rosette-discount-check', perm: 'cdn:setting:list' } },
    // 框架自身能力(保留)
    { path: '/profile', name: 'Profile', component: () => import('@/views/profile/index.vue'), meta: { title: '个人中心', hidden: true } },
    { path: '/system/operlog', name: 'SysOperLog', component: () => import('@/views/cdn/audit.vue'), meta: { title: '操作日志', icon: 'history', perm: 'system:operlog:list' } },
    { path: '/system/loginlog', name: 'SysLoginLog', component: () => import('@/views/system/loginlog/index.vue'), meta: { title: '登录日志', icon: 'login', perm: 'system:loginlog:list' } },
    { path: '/system/sms', name: 'SmsSettings', component: () => import('@/views/cdn/sms_settings.vue'), meta: { title: '短信设置', icon: 'message', perm: 'system:sms:settings' } },
  ],
}

/** CDN 占位页面组件工厂:生成 meta.title + meta.module 的占位页 */
function Cdn(title: string) {
  return () => import('@/views/cdn/placeholder.vue').then((m) => {
    // placeholder 从 route.meta 读取标题,这里无需传参
    void title
    return m
  })
}

const router = createRouter({
  history: createWebHistory(),
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
