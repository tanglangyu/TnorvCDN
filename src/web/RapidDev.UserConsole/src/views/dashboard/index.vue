<script setup lang="ts">
/**
 * 仪表盘 —— 汇总关键指标:用户数、定时任务数、启用流控策略数、在线用户数。
 */
import { ref, onMounted } from 'vue'
import { useUserStore } from '@/store/user'
import { useSiteStore } from '@/store/site'
import { TCard } from '@/components/t'
import { cdnApi } from '@/api/cdn'
import { ElMessageBox } from 'element-plus'

const userStore = useUserStore()
const siteStore = useSiteStore()


// ---- 我的 CDN 概览（/api/user/*，失败静默） ----
const cdn = ref<any>({ servers: '—', todayBytes: '—', todayRequests: '—', todayBlocked: '—', hitRate: '—', geo: [], geoTotal: 0 })
const fmtBytes = (n: number) => {
  if (n >= 1073741824) return (n / 1073741824).toFixed(2) + ' GB'
  if (n >= 1048576) return (n / 1048576).toFixed(1) + ' MB'
  if (n >= 1024) return (n / 1024).toFixed(1) + ' KB'
  return String(n)
}

async function loadCdnStats() {
  try {
    const [overview, geo] = await Promise.all([
      cdnApi.get('/user/overview').catch(() => null),
      cdnApi.get('/user/geo/summary', { hours: 24 }).catch(() => null),
    ])
    if (overview) {
      cdn.value.servers = overview.servers ?? '—'
      cdn.value.todayBytes = fmtBytes(overview.todayBytes || 0)
      cdn.value.todayRequests = overview.todayRequests ?? '—'
      cdn.value.todayBlocked = overview.todayBlocked ?? 0
      cdn.value.hitRate = overview.cacheHitRate != null ? overview.cacheHitRate + '%' : '—'
    }
    if (geo?.data) {
      cdn.value.geo = (geo.data.items || []).slice(0, 8)
      cdn.value.geoTotal = geo.data.totalIps || 0
    }
  } catch { /* 概览失败不阻塞看板 */ }
}

onMounted(() => { loadCdnStats(); loadPosts() })

// ---- 公告通用设计----
const posts = ref<any[]>([])
const popupPost = ref<any>(null)
const POSTS_DISMISS_KEY = 'tnorv_popup_dismissed'
async function loadPosts() {
  try {
    const list = await cdnApi.get('/user/posts').catch(() => [])
    posts.value = (list || []).slice(0, 5)
    // 弹窗公告：当天只弹一次（localStorage 记录，预览/隐私窗口静默降级）
    const popup = (list || []).find((p: any) => p.popup)
    if (popup) {
      let dismissed = ''
      try { dismissed = localStorage.getItem(POSTS_DISMISS_KEY) || '' } catch { }
      const today = new Date().toISOString().slice(0, 10)
      if (dismissed !== today) {
        popupPost.value = popup
        try { localStorage.setItem(POSTS_DISMISS_KEY, today) } catch { }
      }
    }
  } catch { /* 公告失败不阻塞看板 */ }
}
const showPopup = () => {
  if (!popupPost.value) return
  ElMessageBox.alert(popupPost.value.content, popupPost.value.title, { confirmButtonText: '我知道了' })
    .catch(() => { })
    .finally(() => { popupPost.value = null })
}
showPopup()
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">欢迎回来,{{ userStore.userInfo?.nickName }} 👋</h2>
          <div class="text-muted">{{ siteStore.name }} · {{ siteStore.subtitle || '基于 .NET 10 + Vue3 + Tabler' }}</div>
        </div>
      </div>
    </div>

    <div class="row row-deck row-cards mb-4 stat-stagger">
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">我的网站</div>
          <div class="h1 mb-0 mt-2">{{ cdn.servers }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-world me-1"></i>CDN 加速站点</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">今日流量</div>
          <div class="h1 mb-0 mt-2">{{ cdn.todayBytes }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-chart-arrows-vertical me-1"></i>全部站点合计</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">今日请求数</div>
          <div class="h1 mb-0 mt-2">{{ cdn.todayRequests }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-activity me-1"></i>命中率 {{ cdn.hitRate }}</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">今日拦截</div>
          <div class="h1 mb-0 mt-2">{{ cdn.todayBlocked }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-shield-half me-1"></i>WAF / CC 防护</div>        </TCard>
      </div>
    </div>

    <TCard v-if="posts.length" title="平台公告" class="mb-4">
      <div v-for="p in posts" :key="p.id" class="d-flex align-items-start py-2" style="border-bottom: 1px solid var(--el-border-color-lighter)">
        <el-tag size="small" type="danger" effect="plain" class="me-2" style="flex-shrink: 0">公告</el-tag>
        <div>
          <div class="font-weight-medium">{{ p.title }}</div>
          <div class="text-muted small" style="white-space: pre-wrap">{{ p.content }}</div>
        </div>
      </div>
    </TCard>

    <TCard title="我的访客地区分布（近 24h）" class="mb-4">
      <el-table v-if="cdn.geo.length" :data="cdn.geo" size="small" stripe>
        <el-table-column label="国家/地区" min-width="200"><template #default="{ row }">{{ row.country }}</template></el-table-column>
        <el-table-column label="独立 IP 数" width="120"><template #default="{ row }">{{ row.count }}</template></el-table-column>
        <el-table-column label="占比" min-width="240">
          <template #default="{ row }"><el-progress :percentage="cdn.geoTotal ? Math.round(row.count * 100 / cdn.geoTotal) : 0" :stroke-width="10" /></template>
        </el-table-column>
      </el-table>
      <el-empty v-else description="暂无访客数据（IP 归属地解析在后台异步进行）" :image-size="60" />
    </TCard>

    <TCard title="框架能力一览">
      <div class="row g-3">
        <div class="col-md-6 col-lg-4" v-for="f in features" :key="f.title">
          <div class="d-flex align-items-start">
            <span class="avatar bg-primary-lt me-3"><i class="ti" :class="`ti-${f.icon}`"></i></span>
            <div>
              <div class="font-weight-medium">{{ f.title }}</div>
              <div class="text-muted small">{{ f.desc }}</div>
            </div>
          </div>
        </div>
      </div>
    </TCard>
  </div>
</template>

<script lang="ts">
const features = [
  { title: '多租户隔离', desc: '租户字段 + 全局查询过滤器,默认开启', icon: 'building-arch' },
  { title: 'RBAC 权限', desc: '用户-角色-菜单/按钮,数据权限范围', icon: 'shield' },
  { title: 'JWT 鉴权', desc: '短令牌 + 刷新令牌 + Garnet 黑名单', icon: 'key' },
  { title: '定时任务', desc: 'Quartz + 可视化任务管理 + 执行日志', icon: 'clock' },
  { title: '流控策略', desc: '分区限流 + 滑动窗口 + 令牌桶', icon: 'gauge' },
  { title: '代码生成器', desc: '选表 → 自动生成前后端 CRUD', icon: 'code' },
  { title: 'SignalR 实时', desc: '任务进度、系统通知实时推送', icon: 'broadcast' },
  { title: '暗色模式', desc: '一键切换,持久化,防闪烁', icon: 'moon' },
  { title: 'Vibe Coding', desc: '完整 docs + 模板,AI 照着改即可', icon: 'robot' },
]
export default { data() { return { features } } }
</script>
