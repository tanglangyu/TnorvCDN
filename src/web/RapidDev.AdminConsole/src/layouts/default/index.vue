<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserStore } from '@/store/user'
import { useThemeStore } from '@/store/theme'
import { useLayoutStore } from '@/store/layout'
import { useSiteStore } from '@/store/site'
import { TThemeToggle } from '@/components/t'
import { authApi } from '@/api/auth'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import NotificationBell from '@/components/NotificationBell.vue'
import UserMenu from '@/components/UserMenu.vue'
import CryptoSwitch from '@/components/CryptoSwitch.vue'
import { startNotificationHub, stopNotificationHub } from '@/composables/useNotification'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()
const themeStore = useThemeStore()
const layoutStore = useLayoutStore()
const siteStore = useSiteStore()

interface MenuItem { name: string; path: string; title: string; icon?: string; perm?: string }

const staticMenus = computed(() => {
  const groups: Record<string, MenuItem[]> = {
    数据看板: [{ name: 'Dashboard', path: '/dashboard', title: '概况', icon: 'chart-line' }],
    网站列表: [
      { name: 'CdnServers', path: '/cdn/servers', title: '网站列表', icon: 'world', perm: 'cdn:server:list' },
      { name: 'CdnServerLogs', path: '/cdn/servers/logs', title: '访问日志', icon: 'file-text', perm: 'cdn:server:log' },
      { name: 'CdnServerGroups', path: '/cdn/servers/groups', title: '网站分组', icon: 'folder', perm: 'cdn:server:list' },
      { name: 'CdnCerts', path: '/cdn/servers/certs', title: '证书管理', icon: 'certificate', perm: 'cdn:cert:list' },
      { name: 'CdnCertEvents', path: '/cdn/servers/cert-events', title: '证书签发日志', icon: 'scroll-text', perm: 'cdn:cert:list' },
      { name: 'CdnTrafficStats', path: '/cdn/servers/traffic-stats', title: '用量统计', icon: 'chart-area', perm: 'cdn:stat:list' },
      { name: 'CdnCachePolicy', path: '/cdn/servers/components/cache', title: '缓存策略', icon: 'database', perm: 'cdn:cache:list' },
      { name: 'CdnCacheBatch', path: '/cdn/servers/components/cache/batch', title: '刷新预热', icon: 'refresh', perm: 'cdn:cache:batch' },
      { name: 'CdnWafPolicy', path: '/cdn/servers/components/waf', title: 'WAF策略', icon: 'shield', perm: 'cdn:waf:list' },
      { name: 'CdnIpLists', path: '/cdn/servers/iplists', title: 'IP名单', icon: 'list-details', perm: 'cdn:waf:list' },
      { name: 'CdnCcEvents', path: '/cdn/servers/cc', title: 'CC事件', icon: 'bolt', perm: 'cdn:waf:list' },
      { name: 'CdnDeletedContents', path: '/cdn/servers/deleted-contents', title: '内容封禁', icon: 'ban', perm: 'cdn:waf:list' },
      { name: 'CdnLogPolicy', path: '/cdn/servers/accesslogs', title: '日志策略', icon: 'file-description', perm: 'cdn:log:list' },
      { name: 'CdnMetrics', path: '/cdn/servers/metrics', title: '统计指标', icon: 'chart-dots', perm: 'cdn:stat:list' },
      { name: 'CdnUserScripts', path: '/cdn/servers/user-scripts', title: '边缘脚本', icon: 'script', perm: 'cdn:script:list' },
      { name: 'CdnIpbox', path: '/cdn/ipbox', title: 'IP 盒', icon: 'search', perm: 'cdn:server:list' },
    ],
    边缘节点: [
      { name: 'CdnClusters', path: '/cdn/clusters', title: '集群列表', icon: 'server-2', perm: 'cdn:cluster:list' },
      { name: 'CdnClusterLogs', path: '/cdn/clusters/logs', title: '节点日志', icon: 'history', perm: 'cdn:cluster:log' },
      { name: 'CdnClusterIps', path: '/cdn/clusters/ip-addrs', title: '节点IP', icon: 'ip', perm: 'cdn:cluster:list' },
      { name: 'CdnRegions', path: '/cdn/clusters/regions', title: '区域设置', icon: 'map-pin', perm: 'cdn:region:list' },
      { name: 'CdnSshGrants', path: '/cdn/clusters/grants', title: '节点SSH', icon: 'key', perm: 'cdn:cluster:ssh' },
      { name: 'CdnIndexNodes', path: '/cdn/clusters/index-nodes', title: '索引节点', icon: 'hierarchy', perm: 'cdn:cluster:list' },
      { name: 'CdnMonitors', path: '/cdn/clusters/monitors', title: '区域监控', icon: 'activity', perm: 'cdn:monitor:list' },
      { name: 'CdnAntiDdos', path: '/cdn/clusters/anti-ddos', title: '高防IP', icon: 'shield-lock', perm: 'cdn:antiddos:list' },
    ],
    域名解析: [
      { name: 'CdnDnsClusters', path: '/cdn/dns', title: '集群列表', icon: 'cloud', perm: 'cdn:dns:list' },
      { name: 'CdnDnsProviders', path: '/cdn/dns/providers', title: 'DNS服务商', icon: 'plug', perm: 'cdn:dns:list' },
      { name: 'CdnDnsIssues', path: '/cdn/dns/issues', title: '问题修复', icon: 'tool', perm: 'cdn:dns:list' },
    ],
    智能DNS: [
      { name: 'CdnNsDashboard', path: '/cdn/ns', title: '数据看板', icon: 'dashboard', perm: 'cdn:ns:list' },
      { name: 'CdnNsDomains', path: '/cdn/ns/domains', title: '域名管理', icon: 'world', perm: 'cdn:ns:list' },
      { name: 'CdnNsDomainGroups', path: '/cdn/ns/domains/groups', title: '域名分组', icon: 'folder', perm: 'cdn:ns:list' },
      { name: 'CdnNsBatch', path: '/cdn/ns/domains/batch', title: '批量操作', icon: 'stack', perm: 'cdn:ns:list' },
      { name: 'CdnNsClusters', path: '/cdn/ns/clusters', title: '集群管理', icon: 'server', perm: 'cdn:ns:list' },
      { name: 'CdnNsRoutes', path: '/cdn/ns/routes', title: '线路管理', icon: 'route', perm: 'cdn:ns:list' },
      { name: 'CdnNsSettings', path: '/cdn/ns/settings', title: '全局配置', icon: 'settings', perm: 'cdn:ns:list' },
      { name: 'CdnNsUserPlans', path: '/cdn/ns/userPlans', title: '用户套餐', icon: 'package', perm: 'cdn:ns:list' },
      { name: 'CdnNsPlans', path: '/cdn/ns/plans', title: '套餐设置', icon: 'ticket', perm: 'cdn:ns:list' },
      { name: 'CdnNsAccessLogs', path: '/cdn/ns/clusters/accessLogs', title: '访问日志', icon: 'file-text', perm: 'cdn:ns:list' },
      { name: 'CdnNsRunLogs', path: '/cdn/ns/clusters/logs', title: '运行日志', icon: 'history', perm: 'cdn:ns:list' },
      { name: 'CdnNsTest', path: '/cdn/ns/test', title: '解析测试', icon: 'test-pipe', perm: 'cdn:ns:list' },
    ],
    平台用户: [
      { name: 'CdnUsers', path: '/cdn/users', title: '用户列表', icon: 'users', perm: 'cdn:user:list' },
      { name: 'CdnUserIdentity', path: '/cdn/identities', title: '实名认证', icon: 'certificate', perm: 'cdn:user:list' },
      { name: 'CdnUserSettings', path: '/cdn/users/setting', title: '用户设置', icon: 'adjustments', perm: 'cdn:user:list' },
      { name: 'CdnInvitations', path: '/cdn/users/invitations', title: '邀请码', icon: 'mail-forward', perm: 'cdn:user:list' },
    ],
    财务管理: [
      { name: 'CdnFinanceBills', path: '/cdn/finance/bills', title: '费用账单', icon: 'receipt', perm: 'cdn:finance:list' },
      { name: 'CdnFinanceAccounts', path: '/cdn/finance/accounts', title: '用户账户', icon: 'wallet', perm: 'cdn:finance:list' },
      { name: 'CdnFinanceLogs', path: '/cdn/finance/logs', title: '收支明细', icon: 'arrows-left-right', perm: 'cdn:finance:list' },
      { name: 'CdnFinanceOrders', path: '/cdn/finance/orders', title: '订单管理', icon: 'shopping-cart', perm: 'cdn:finance:list' },
      { name: 'CdnFinanceIncome', path: '/cdn/finance/income', title: '统计报表', icon: 'chart-bar', perm: 'cdn:finance:list' },
      { name: 'CdnFeeSettings', path: '/cdn/finance/fee', title: '计费设置', icon: 'calculator', perm: 'cdn:finance:list' },
      { name: 'CdnTrafficPackages', path: '/cdn/finance/packages', title: '流量包', icon: 'box', perm: 'cdn:finance:list' },
    ],
    套餐管理: [
      { name: 'CdnPlans', path: '/cdn/plans', title: '套餐列表', icon: 'gift', perm: 'cdn:plan:list' },
      { name: 'CdnPlanCategories', path: '/cdn/plans/categories', title: '套餐分类', icon: 'category', perm: 'cdn:plan:list' },
      { name: 'CdnUserPlans', path: '/cdn/plans/userPlans', title: '已购套餐', icon: 'shopping bag', perm: 'cdn:plan:list' },
      { name: 'CdnCoupons', path: '/cdn/plans/coupons', title: '优惠券', icon: 'discount', perm: 'cdn:plan:list' },
      { name: 'CdnUserCoupons', path: '/cdn/plans/coupons/user-coupons', title: '已领优惠券', icon: 'discount-2', perm: 'cdn:plan:list' },
      { name: 'CdnQuotaPackages', path: '/cdn/plans/quota-packages', title: '资源包', icon: 'package', perm: 'cdn:plan:list' },
      { name: 'CdnUserQuotaPackages', path: '/cdn/plans/quota-packages/user-quota-packages', title: '已购资源包', icon: 'box-multiple', perm: 'cdn:plan:list' },
    ],
    工单系统: [
      { name: 'CdnTickets', path: '/cdn/tickets', title: '工单', icon: 'messages', perm: 'cdn:ticket:list' },
      { name: 'CdnTicketCategories', path: '/cdn/tickets/categories', title: '分类', icon: 'tags', perm: 'cdn:ticket:list' },
    ],
    管理员: [
      { name: 'CdnAdmins', path: '/cdn/admins', title: '管理员列表', icon: 'user-shield', perm: 'cdn:admin:list' },
      { name: 'CdnRecipients', path: '/cdn/admins/recipients', title: '通知媒介', icon: 'bell', perm: 'cdn:admin:list' },
      { name: 'CdnMessages', path: '/cdn/messages', title: '消息中心', icon: 'mail', perm: 'cdn:admin:list' },
      { name: 'CdnPosts', path: '/cdn/posts', title: '公告管理', icon: 'speakerphone', perm: 'cdn:setting:list' },
    ],
    日志审计: [
      { name: 'SysOperLog', path: '/system/operlog', title: '操作日志', icon: 'history', perm: 'system:operlog:list' },
      { name: 'SysLoginLog', path: '/system/loginlog', title: '登录日志', icon: 'login', perm: 'system:loginlog:list' },
    ],
    系统设置: [
      { name: 'CdnSettings', path: '/cdn/settings', title: '基础设置', icon: 'settings', perm: 'cdn:setting:list' },
      { name: 'CdnSettingsAdvanced', path: '/cdn/settings/advanced', title: '高级设置', icon: 'settings-2', perm: 'cdn:setting:list' },
      { name: 'CdnAcmeAccounts', path: '/cdn/settings/acme-accounts', title: 'ACME 签发账户', icon: 'key', perm: 'cdn:setting:list' },
      { name: 'CdnSettingsAuthority', path: '/cdn/settings/authority', title: '商业版本', icon: 'rosette-discount-check', perm: 'cdn:setting:list' },
      { name: 'Profile', path: '/profile', title: '个人中心', icon: 'user-circle' },
    ],
  }
  return Object.entries(groups).map(([key, items]) => ({
    key,
    // 过滤掉用户无权限访问的菜单项(超管可见全部)
    items: items.filter((item: any) => {
      const userStore = useUserStore()
      if (userStore.isSuperAdmin) return true
      // 无权限码的菜单(如首页/个人中心)默认可见
      if (!item.perm) return true
      return userStore.hasPermission(item.perm)
    }),
  })).filter(g => g.items.length > 0) // 整组无权限也不显示组标题
})

// 所有菜单路径扁平列表(权限过滤后),供高亮判定"最长匹配"使用
const allMenuPaths = computed(() => staticMenus.value.flatMap(g => g.items.map(i => i.path)))

// 菜单高亮:精确匹配或子路径匹配,且取"最长匹配"——/servers/certs 只高亮 /servers/certs,不再同时高亮 /servers
const matchedMenuPath = (current: string, menuPath: string) => {
  if (current === menuPath) return true
  if (!current.startsWith(menuPath + '/')) return false
  // 已有更长的菜单路径也命中当前路由时,让位给最长者(如 /servers/certs 与 /servers 都能匹配 /servers/certs/xxx)
  return !allMenuPaths.value.some(p => p !== menuPath && p.length > menuPath.length && (current === p || current.startsWith(p + '/')))
}

const collapsed = computed(() => layoutStore.sidebarCollapsed)

const breadcrumbs = computed(() => {
  return route.matched.filter((r) => r.meta?.title).map((r) => ({ title: r.meta.title as string, path: r.path }))
})

async function handleLogout() {
  try { await authApi.logout() } catch {}
  await stopNotificationHub()
  userStore.logout()
  router.push('/login')
}

function toggleSidebar() { layoutStore.toggle() }

onMounted(async () => {
  themeStore.init()
  siteStore.load()
  if (userStore.token) startNotificationHub()
  try {
    const r = await cdnApi.get('/admin/version/check')
    version.value = r.current || ''
  } catch { version.value = '' }
})

// ---- 版本检查与全局搜索 (merge: 原 Options API 混合块, 转为 setup 写法) ----
const version = ref('')
const searchKw = ref('')
const searchVisible = ref(false)
const searching = ref(false)
const results = ref<any>(null)
const hoverItem = ref('')
const searchCatLabel: Record<string, string> = { servers: '站点', users: '用户', nodes: '节点', tickets: '工单', orders: '订单' }
let searchTimer: any = null

async function checkVersion() {
  try {
    const r = await cdnApi.get('/admin/version/check')
    if (!r.latest) ElMessage.info(`当前版本 ${r.current}（未获取到最新版本信息）`)
    else if (r.upToDate) ElMessage.success(`已是最新版本 ${r.current}`)
    else ElMessage.warning(`发现新版本 ${r.latest}，当前 ${r.current}`)
  } catch { ElMessage.error('版本检查失败') }
}
function doSearch() {
  clearTimeout(searchTimer)
  const kw = searchKw.value.trim()
  if (!kw) { results.value = null; return }
  searchTimer = setTimeout(async () => {
    searching.value = true
    try {
      results.value = await cdnApi.get('/admin/search', { q: kw })
    } catch { results.value = null }
    finally { searching.value = false }
  }, 250)
}
function goResult(url: string) {
  searchVisible.value = false
  searchKw.value = ''
  results.value = null
  if (url) router.push(url)
}
</script>

<template>
  <div class="rapiddev-layout" :class="{ 'is-collapsed': collapsed }">
    <aside class="rapiddev-sidebar">
      <div class="sidebar-header">
        <RouterLink to="/dashboard" class="brand">
          <span class="brand-icon"><i class="ti ti-bolt"></i></span>
          <span class="brand-text" v-show="!collapsed">{{ siteStore.name }}</span>
        </RouterLink>
      </div>
      <div class="sidebar-body">
        <template v-for="group in staticMenus" :key="group.key">
          <div class="menu-group-title" v-show="!collapsed">{{ group.key }}</div>
          <div class="menu-group-title menu-group-title--collapsed" v-show="collapsed">&nbsp;</div>
          <RouterLink
            v-for="item in group.items" :key="item.name" :to="item.path" class="menu-item"
            :class="{ active: matchedMenuPath(route.path, item.path) }"
            :title="collapsed ? item.title : undefined"
          >
            <span class="menu-icon"><i class="ti" :class="`ti-${item.icon}`"></i></span>
            <span class="menu-text" v-show="!collapsed">{{ item.title }}</span>
          </RouterLink>
        </template>
      </div>
    </aside>

    <div class="rapiddev-main">
      <header class="rapiddev-topbar">
        <div class="topbar-left">
          <button class="hamburger-btn" @click="toggleSidebar" :title="collapsed ? '展开菜单' : '收起菜单'">
            <i class="ti" :class="collapsed ? 'ti-menu-2' : 'ti-x'"></i>
          </button>
          <ol class="breadcrumb breadcrumb-arrows d-none d-md-flex">
            <li v-for="(b, i) in breadcrumbs" :key="i" class="breadcrumb-item" :class="{ active: i === breadcrumbs.length - 1 }">
              <RouterLink v-if="i !== breadcrumbs.length - 1" :to="b.path">{{ b.title }}</RouterLink>
              <span v-else>{{ b.title }}</span>
            </li>
          </ol>
          <!-- 全局搜索（对齐竞品 /search：站点/用户/节点/工单/订单聚合） -->
          <el-popover v-model:visible="searchVisible" placement="bottom-start" :width="420" trigger="click">
            <template #reference>
              <el-input v-model="searchKw" size="small" placeholder="搜索站点/用户/节点/工单/订单…" style="width: 240px" class="ms-3 d-none d-lg-block"
                @input="doSearch" @focus="searchVisible = results ? true : searchVisible" clearable />
            </template>
            <div v-if="searching" class="text-center py-3 text-muted small">搜索中…</div>
            <template v-else-if="results">
              <template v-for="(items, cat) in results" :key="cat">
                <div v-if="items.length" class="small text-muted px-2 pt-2 pb-1">{{ searchCatLabel[cat] || cat }}</div>
                <div v-for="it in items" :key="cat + it.id" class="px-2 py-1 rounded" style="cursor: pointer"
                  :class="'bg-' + (hoverItem === cat + it.id ? 'primary-lt' : 'transparent')"
                  @mouseenter="hoverItem = cat + it.id" @click="goResult(it.url)">
                  <div class="d-flex justify-content-between">
                    <span class="small">{{ it.title }}</span>
                    <span class="text-muted" style="font-size: 12px">{{ it.sub }}</span>
                  </div>
                </div>
              </template>
              <el-empty v-if="!Object.values(results).some((a: any) => a.length)" description="无匹配结果" :image-size="50" />
            </template>
          </el-popover>
        </div>
        <div class="topbar-right">
          <CryptoSwitch />
          <NotificationBell />
          <TThemeToggle />
          <UserMenu
            :nick-name="userStore.userInfo?.nickName"
            :user-name="userStore.userInfo?.userName"
            :roles="userStore.roles"
            @logout="handleLogout"
          />
        </div>
      </header>

      <main class="rapiddev-content">
        <RouterView v-slot="{ Component, route: r }">
          <transition name="page" mode="out-in">
            <div :key="r.fullPath">
              <component :is="Component" />
            </div>
          </transition>
        </RouterView>
      </main>
      <footer class="text-center text-muted small py-3">
        TnorvCDN
        <a href="javascript:void(0)" class="ms-1" style="text-decoration: none" @click="checkVersion">{{ version }}</a>
      </footer>
      <el-backtop :right="24" :bottom="24" />
    </div>
  </div>
</template>


<style scoped>
.rapiddev-layout { display: flex; min-height: 100vh; }

.rapiddev-sidebar {
  width: var(--rapiddev-sidebar-width, 260px); flex-shrink: 0;
  background: var(--el-bg-color); border-right: 1px solid var(--el-border-color);
  position: sticky; top: 0; height: 100vh; overflow-y: scroll; overflow-x: hidden;
  /* 侧栏内容固定超高,强制常驻滚动条,避免出现/消失导致宽度跳变 */
  display: flex; flex-direction: column; transition: width 0.2s ease; z-index: 1030;
}
.is-collapsed .rapiddev-sidebar { width: var(--rapiddev-sidebar-collapsed-width, 64px); }

.sidebar-header {
  height: var(--rapiddev-header-height, 56px); display: flex; align-items: center;
  padding: 0 1rem; border-bottom: 1px solid var(--el-border-color); flex-shrink: 0;
}
.brand { display: flex; align-items: center; gap: 0.5rem; text-decoration: none; color: inherit; font-weight: 700; font-size: 1.1rem; white-space: nowrap; }
.brand-icon { width: 32px; height: 32px; display: inline-flex; align-items: center; justify-content: center; background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-dark-2)); color: #fff; border-radius: 8px; flex-shrink: 0; box-shadow: 0 2px 8px var(--el-color-primary-light-5); }
.sidebar-body { flex: 1; padding: 0.5rem 0; }

.menu-group-title { padding: 0.75rem 1.25rem 0.25rem; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; color: var(--el-text-color-secondary); white-space: nowrap; overflow: hidden; }
.menu-group-title--collapsed { padding: 0.5rem 0; border-bottom: 1px solid var(--el-border-color); margin: 0 0.5rem; }
.menu-item { display: flex; align-items: center; gap: 0.75rem; padding: 0.5rem 1.25rem; margin: 1px 0.5rem; border-radius: 6px; color: var(--el-text-color-primary); text-decoration: none; font-size: 0.875rem; white-space: nowrap; position: relative; transition: background 0.15s, color 0.15s; }
.is-collapsed .menu-item { justify-content: center; padding: 0.5rem 0; }
.menu-item:hover { background: var(--el-fill-color-light); }
.menu-item.active { background: var(--el-color-primary-light-9); color: var(--el-color-primary); font-weight: 600; }
/* 活动项左侧指示条(圆角短棒) */
.menu-item.active::before {
  content: ''; position: absolute; left: -0.5rem; top: 50%; transform: translateY(-50%);
  width: 3px; height: 18px; border-radius: 2px; background: var(--el-color-primary);
}
.menu-icon { width: 20px; height: 20px; display: inline-flex; align-items: center; justify-content: center; flex-shrink: 0; font-size: 1.1rem; }
.menu-text { overflow: hidden; text-overflow: ellipsis; }

.rapiddev-main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
.rapiddev-topbar {
  height: var(--rapiddev-header-height, 56px); display: flex; align-items: center; justify-content: space-between; padding: 0 1rem;
  /* 毛玻璃顶栏:半透明底 + 模糊,滚动内容从底下穿过更通透 */
  background: color-mix(in srgb, var(--el-bg-color) 82%, transparent);
  backdrop-filter: saturate(160%) blur(8px);
  border-bottom: 1px solid var(--el-border-color-lighter);
  position: sticky; top: 0; z-index: 1020;
}
.topbar-left { display: flex; align-items: center; gap: 0.75rem; }
.topbar-right { display: flex; align-items: center; gap: 0.25rem; }
.hamburger-btn { width: 36px; height: 36px; display: inline-flex; align-items: center; justify-content: center; border: none; background: transparent; color: var(--el-text-color-primary); border-radius: 6px; cursor: pointer; font-size: 1.2rem; transition: background 0.15s; }
.hamburger-btn:hover { background: var(--el-fill-color-light); }
.rapiddev-content { flex: 1; padding: 1.5rem 1.5rem 3rem; min-width: 0; }
.rapiddev-sidebar::-webkit-scrollbar { width: 4px; }
.rapiddev-sidebar::-webkit-scrollbar-thumb { background: rgba(128,128,128,0.25); border-radius: 2px; }
</style>
