<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserStore } from '@/store/user'
import { useThemeStore } from '@/store/theme'
import { useLayoutStore } from '@/store/layout'
import { useSiteStore } from '@/store/site'
import { TThemeToggle } from '@/components/t'
import { authApi } from '@/api/auth'
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
    概览: [{ name: 'Dashboard', path: '/dashboard', title: '概览', icon: 'chart-line' }],
    CDN加速: [
      { name: 'MyServers', path: '/servers', title: '我的网站', icon: 'world', perm: 'user:server:list' },
      { name: 'ServerCache', path: '/servers/cache', title: '刷新预热', icon: 'refresh', perm: 'user:server:cache' },
      { name: 'MyCerts', path: '/servers/certs', title: '证书管理', icon: 'certificate', perm: 'user:cert:list' },
      { name: 'MyCertEvents', path: '/servers/cert-events', title: '证书签发日志', icon: 'scroll-text', perm: 'user:cert:list' },
      { name: 'MyTrafficStats', path: '/servers/traffic-stats', title: '用量统计', icon: 'chart-area', perm: 'user:stat:list' },
    ],
    负载均衡: [{ name: 'LoadBalance', path: '/lb', title: '负载均衡', icon: 'arrows-split-2', perm: 'user:lb:list' }],
    WAF安全: [
      { name: 'WafOverview', path: '/waf', title: '概览', icon: 'shield-half', perm: 'user:waf:list' },
      { name: 'WafLogs', path: '/waf/logs', title: '拦截日志', icon: 'file-text', perm: 'user:waf:list' },
      { name: 'WafIpLists', path: '/waf/iplists', title: '拦截IP', icon: 'list-details', perm: 'user:waf:list' },
      { name: 'WafSettings', path: '/waf/settings', title: '全局规则', icon: 'settings', perm: 'user:waf:list' },
    ],
    套餐管理: [{ name: 'UserPlans', path: '/plans', title: '套餐管理', icon: 'gift', perm: 'user:plan:list' }],
    DDoS高防: [
      { name: 'AntiDdosInstances', path: '/anti-ddos/instances', title: '实例列表', icon: 'shield-lock', perm: 'user:antiddos:list' },
      { name: 'AntiDdosPackages', path: '/anti-ddos/packages', title: '购买实例', icon: 'shopping-cart', perm: 'user:antiddos:list' },
    ],
    域名解析: [
      { name: 'NsAccess', path: '/ns/access', title: 'NS托管接入', icon: 'cloud', perm: 'user:ns:list' },
      { name: 'NsDomains', path: '/ns/domains', title: '我的域名', icon: 'world', perm: 'user:ns:list' },
      { name: 'NsDomainGroups', path: '/ns/domains/groups', title: '域名分组', icon: 'folder', perm: 'user:ns:list' },
      { name: 'NsBatch', path: '/ns/domains/batch', title: '批量操作', icon: 'stack', perm: 'user:ns:list' },
      { name: 'NsRoutes', path: '/ns/routes', title: '线路管理', icon: 'route', perm: 'user:ns:list' },
      { name: 'NsPlans', path: '/ns/plans', title: '套餐', icon: 'ticket', perm: 'user:ns:list' },
    ],
    财务管理: [
      { name: 'FinanceOverview', path: '/finance', title: '概览', icon: 'wallet', perm: 'user:finance:list' },
      { name: 'FinanceBills', path: '/finance/bills', title: '费用账单', icon: 'receipt', perm: 'user:finance:list' },
      { name: 'FinanceLogs', path: '/finance/logs', title: '收支明细', icon: 'arrows-left-right', perm: 'user:finance:list' },
      { name: 'FinanceCharge', path: '/finance/charge', title: '充值', icon: 'coin', perm: 'user:finance:list' },
      { name: 'FinanceCoupons', path: '/finance/coupons', title: '优惠券', icon: 'discount', perm: 'user:finance:list' },
    ],
    工单系统: [{ name: 'MyTickets', path: '/tickets', title: '工单系统', icon: 'messages', perm: 'user:ticket:list' }],
    访问控制: [{ name: 'AccessControl', path: '/acl', title: '访问控制', icon: 'lock', perm: 'user:acl:list' }],
    其他: [
      { name: 'MyMessages', path: '/messages', title: '消息', icon: 'bell', perm: 'user:message:list' },
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

onMounted(() => {
  themeStore.init()
  siteStore.load()
  if (userStore.token) startNotificationHub()
})
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
