<script setup lang="ts">
/**
 * 数据看板（概况）—— 通用设计
 * 统计卡（集群/节点/用户/站点/流量含环比）+ 实时指标 + 趋势切换（24h/15d 流量/访问量）
 * + 排行（域名/节点/路径/独立IP）+ 状态码分布 + 地区分布。全部来自主控真实聚合端点。
 */
import { ref, onMounted, onBeforeUnmount, computed } from 'vue'
import { useUserStore } from '@/store/user'
import { useSiteStore } from '@/store/site'
import { useRouter } from 'vue-router'
import { TCard } from '@/components/t'
import { cdnApi } from '@/api/cdn'

const userStore = useUserStore()
const siteStore = useSiteStore()

const stats = ref<any>({})
const realtime = ref<any>({})
const trendMode = ref<'bytes24h' | 'bytes15d' | 'req24h' | 'req15d'>('bytes24h')
const trend24h = ref<any[]>([])
const trend15d = ref<any[]>([])
const geo = ref<any[]>([])
const geoTotal = ref(0)
const clients = ref<any>({ sample: 0, browsers: [], systems: [] })
const statusCodes = ref<any>({ total: 0, byStatus: [], byMethod: [] })
const rankDim = ref<'domain' | 'node' | 'path' | 'ip'>('domain')
const rankRows = ref<any[]>([])
let timer: any = null

const fmtBytes = (b: any) => {
  const n = Number(b) || 0
  if (n >= 1 << 30) return (n / (1 << 30)).toFixed(2) + ' GB'
  if (n >= 1 << 20) return (n / (1 << 20)).toFixed(1) + ' MB'
  if (n >= 1 << 10) return (n / (1 << 10)).toFixed(1) + ' KB'
  return n + ' B'
}
const fmtKbps = (k: any) => {
  const n = Number(k) || 0
  if (n >= 1e6) return (n / 1e6).toFixed(2) + ' Gbps'
  if (n >= 1e3) return (n / 1e3).toFixed(1) + ' Mbps'
  return n + ' Kbps'
}
const statusColor = (s: string) =>
  s.startsWith('2') ? 'success' : s.startsWith('3') ? 'info' : s.startsWith('4') ? 'warning' : s.startsWith('5') ? 'danger' : 'info'

const trendData = computed(() => {
  if (trendMode.value === 'bytes24h') return trend24h.value.map((t) => ({ label: t.label, value: t.bytes, text: fmtBytes(t.bytes) }))
  if (trendMode.value === 'bytes15d') return trend15d.value.map((t) => ({ label: t.date, value: t.bytes, text: fmtBytes(t.bytes) }))
  if (trendMode.value === 'req24h') return trend24h.value.map((t) => ({ label: t.label, value: t.requests, text: String(t.requests) }))
  return trend15d.value.map((t) => ({ label: t.date, value: t.requests, text: String(t.requests) }))
})
const trendMax = computed(() => Math.max(1, ...trendData.value.map((d) => d.value)))

async function loadRank() {
  try {
    rankRows.value = (await cdnApi.get('/admin/stats/top', { dim: rankDim.value, hours: 24, limit: 10 })) || []
  } catch { rankRows.value = [] }
}

// —— A3 下钻：排行行点击 → 该域名近 24h 小时趋势弹层 ——
const drillVisible = ref(false)
const drillHost = ref('')
const drillRows = ref<any[]>([])
const drillMax = computed(() => Math.max(1, ...drillRows.value.map((d) => d.bytes || 0)))
const drillReqMax = computed(() => Math.max(1, ...drillRows.value.map((d) => d.requests || 0)))

async function openDrill(row: any) {
  if (rankDim.value !== 'domain' || !row?.key) return
  drillHost.value = row.key
  drillRows.value = []
  drillVisible.value = true
  try {
    const r = await cdnApi.get('/admin/traffic/hourly/server', { host: row.key, hours: 24 })
    drillRows.value = (r || []).map((t: any) => ({ label: (t.hour || '').slice(5, 16), requests: t.requests, bytes: t.bytes }))
  } catch { drillRows.value = [] }
}

// —— A3 下钻：4xx/5xx 点击 → 访问日志页带 status 预筛选 ——
const router = useRouter()
function gotoLogsWithStatus(s: string) {
  router.push({ path: '/cdn/servers/logs', query: { status: s.charAt(0) + 'xx' === s ? Number(s.charAt(0) + '00') : undefined } })
}

async function loadAll() {
  const [metrics, r24, r15d, rt, sc, g, cl] = await Promise.all([
    cdnApi.get('/admin/overview').catch(() => null),
    cdnApi.get('/admin/traffic/hourly', { hours: 24 }).catch(() => null),
    cdnApi.get('/admin/traffic/daily', { days: 15 }).catch(() => null),
    cdnApi.get('/admin/metrics/realtime').catch(() => null),
    cdnApi.get('/admin/stats/status-codes', { hours: 24 }).catch(() => null),
    cdnApi.get('/admin/geo/summary', { hours: 24 }).catch(() => null),
    cdnApi.get('/admin/stats/clients', { hours: 24 }).catch(() => null),
  ])
  if (metrics) stats.value = metrics
  trend24h.value = (r24 || []).map((t: any) => ({ label: (t.hour || '').slice(5, 16), requests: t.requests, bytes: t.bytes }))
  trend15d.value = r15d || []
  realtime.value = rt || {}
  statusCodes.value = sc || { total: 0, byStatus: [], byMethod: [] }
  geo.value = (g?.items || []).slice(0, 8)
  geoTotal.value = g?.totalIps || 0
  clients.value = cl || { sample: 0, browsers: [], systems: [] }
  await loadRank()
}

function switchTrend(m: any) { trendMode.value = m }

onMounted(() => {
  loadAll()
  timer = setInterval(loadAll, 30000) // 30s 自动刷新（实时指标跟随心跳节奏）
})
onBeforeUnmount(() => clearInterval(timer))
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">数据看板</h2>
          <div class="text-muted">{{ siteStore.name }} · 概况 · 实时指标每 30 秒刷新</div>
        </div>
      </div>
    </div>

    <!-- 图诺弗推广（开源版赞助信息） -->
    <el-alert type="info" :closable="true" class="mb-4">
      <template #title>
        由 <a href="https://www.tunuofu.com/" target="_blank" rel="noopener"><strong>图诺弗</strong></a>
        提供技术支持 ——
        <a href="https://sms.tnorv.com/" target="_blank" rel="noopener"><strong>图诺弗通信</strong></a>：全球短信、全球语音
      </template>
    </el-alert>

    <!-- 统计卡片 -->
    <div class="row row-deck row-cards mb-1 stat-stagger">
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">加速站点 / 用户</div>
          <div class="h1 mb-0 mt-2">{{ stats.servers ?? '—' }}<span class="text-muted" style="font-size:14px"> / {{ stats.users ?? '—' }}</span></div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-world me-1"></i>站点数 / 用户数</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">边缘节点</div>
          <div class="h1 mb-0 mt-2">{{ stats.nodesOnline ?? '—' }}<span class="text-muted" style="font-size:14px"> / {{ stats.nodesTotal ?? '—' }}</span></div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-server me-1"></i>在线 / 总数</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">当天流量 <el-tag v-if="stats.bytesGrowth != null" size="small" :type="stats.bytesGrowth >= 0 ? 'danger' : 'success'" class="ms-1">{{ stats.bytesGrowth >= 0 ? '+' : '' }}{{ stats.bytesGrowth }}%</el-tag></div>
          <div class="h1 mb-0 mt-2">{{ fmtBytes(stats.bytes24h) }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-chart-arrows-vertical me-1"></i>环比前 24h {{ fmtBytes(stats.bytesPrev24h) }}</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">当日独立 IP / 24h 请求</div>
          <div class="h1 mb-0 mt-2">{{ stats.todayUniqueIps ?? '—' }}<span class="text-muted" style="font-size:14px"> / {{ stats.requests24h ?? '—' }}</span></div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-users me-1"></i>缓存命中率 {{ stats.cacheHitRate ?? 0 }}%</div>
        </TCard>
      </div>
    </div>
    <div class="row row-deck row-cards mb-4 stat-stagger">
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">本周流量</div>
          <div class="h1 mb-0 mt-2">{{ fmtBytes(stats.weekBytes) }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-calendar-week me-1"></i>近 7 天下行</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">昨日流量</div>
          <div class="h1 mb-0 mt-2">{{ fmtBytes(stats.yesterdayBytes) }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-history me-1"></i>自然日统计</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">24h 拦截</div>
          <div class="h1 mb-0 mt-2">{{ stats.blocked24h ?? 0 }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-shield-half me-1"></i>WAF / CC 防护</div>
        </TCard>
      </div>
      <div class="col-sm-6 col-lg-3">
        <TCard bordered>
          <div class="subheader">实时总带宽</div>
          <div class="h1 mb-0 mt-2">{{ fmtKbps(realtime.totalBandwidthKbps) }}</div>
          <div class="d-flex mt-2 text-muted"><i class="ti ti-activity me-1"></i>{{ realtime.nodeCount ?? 0 }} 个节点上报中</div>
        </TCard>
      </div>
    </div>

    <!-- 实时指标条 -->
    <el-card class="mb-4">
      <template #header><span>实时指标（节点心跳聚合）</span></template>
      <div class="row text-center">
        <div class="col"><div class="text-muted small">总带宽</div><div class="h3 mb-0">{{ fmtKbps(realtime.totalBandwidthKbps) }}</div></div>
        <div class="col"><div class="text-muted small">平均 CPU</div><div class="h3 mb-0">{{ realtime.avgCpu ?? 0 }}%</div></div>
        <div class="col"><div class="text-muted small">平均内存</div><div class="h3 mb-0">{{ realtime.avgMem ?? 0 }}%</div></div>
        <div class="col"><div class="text-muted small">平均负载</div><div class="h3 mb-0">{{ realtime.avgLoad ?? 0 }}</div></div>
        <div class="col"><div class="text-muted small">连接数</div><div class="h3 mb-0">{{ realtime.connections ?? 0 }}</div></div>
      </div>
      <el-table v-if="(realtime.nodes || []).length" class="mt-3" :data="realtime.nodes" size="small" stripe>
        <el-table-column label="节点" width="90"><template #default="{ row }">节点 {{ row.nodeId }}</template></el-table-column>
        <el-table-column label="CPU" min-width="180"><template #default="{ row }"><el-progress :percentage="Math.min(100, Math.round(row.cpuUsage))" :stroke-width="8" :status="row.cpuUsage > 85 ? 'exception' : ''" /></template></el-table-column>
        <el-table-column label="内存" min-width="180"><template #default="{ row }"><el-progress :percentage="Math.min(100, Math.round(row.memUsage))" :stroke-width="8" :status="row.memUsage > 85 ? 'exception' : ''" /></template></el-table-column>
        <el-table-column prop="load" label="负载" width="90" />
        <el-table-column prop="connections" label="连接数" width="100" />
      </el-table>
      <div v-else class="text-muted small mt-2">暂无近期心跳（节点离线或刚启动）</div>
    </el-card>

    <!-- 趋势切换 + 状态码 -->
    <div class="row row-deck mb-4">
      <div class="col-lg-8">
        <TCard bordered>
          <template #header>
            <div class="d-flex justify-content-between align-items-center">
              <span>流量 / 访问量趋势</span>
              <el-radio-group size="small" :model-value="trendMode" @change="switchTrend">
                <el-radio-button value="bytes24h">24h 流量</el-radio-button>
                <el-radio-button value="bytes15d">15天 流量</el-radio-button>
                <el-radio-button value="req24h">24h 访问量</el-radio-button>
                <el-radio-button value="req15d">15天 访问量</el-radio-button>
              </el-radio-group>
            </div>
          </template>
          <div v-if="trendData.length" class="d-flex align-items-end gap-1" style="height: 200px">
            <div v-for="d in trendData" :key="d.label" class="flex-fill d-flex flex-column justify-content-end text-center" :title="`${d.label}: ${d.text}`">
              <div class="bg-primary-lt rounded-top" :style="{ height: Math.max(4, d.value * 100 / trendMax) + '%' }"></div>
              <div class="text-muted" style="font-size: 10px; writing-mode: horizontal-tb; transform: scale(0.85)">{{ d.label.slice(-5) }}</div>
            </div>
          </div>
          <el-empty v-else description="暂无趋势数据" :image-size="60" />
        </TCard>
      </div>
      <div class="col-lg-4">
        <TCard bordered title="状态码分布（24h）">
          <template v-if="statusCodes.byStatus?.length">
            <div v-for="s in statusCodes.byStatus" :key="s.key" class="d-flex align-items-center mb-2"
              style="cursor: pointer" title="点击筛选访问日志"
              @click="gotoLogsWithStatus(s.key)">
              <el-tag size="small" :type="statusColor(s.key)" style="width:52px">{{ s.key }}</el-tag>
              <el-progress class="flex-fill ms-2" :percentage="statusCodes.total ? Math.round(s.count * 100 / statusCodes.total) : 0" :stroke-width="10" :show-text="false" />
              <span class="small text-muted ms-2">{{ s.count }}</span>
            </div>
            <el-divider class="my-2" />
            <div class="small text-muted">请求方法：<el-tag v-for="m in statusCodes.byMethod" :key="m.key" size="small" class="me-1">{{ m.key }} {{ m.count }}</el-tag></div>
          </template>
          <el-empty v-else description="暂无数据" :image-size="50" />
        </TCard>
      </div>
    </div>

    <!-- 排行 -->
    <el-card class="mb-4">
      <template #header>
        <div class="d-flex justify-content-between align-items-center">
          <span>访问排行（24h）</span>
          <el-radio-group size="small" v-model="rankDim" @change="loadRank">
            <el-radio-button value="domain">域名</el-radio-button>
            <el-radio-button value="node">节点</el-radio-button>
            <el-radio-button value="path">路径</el-radio-button>
            <el-radio-button value="ip">独立IP</el-radio-button>
          </el-radio-group>
        </div>
      </template>
      <el-table v-if="rankRows.length" :data="rankRows" size="small" stripe
        :row-style="{ cursor: rankDim === 'domain' ? 'pointer' : 'default' }"
        @row-click="openDrill">
        <el-table-column type="index" label="#" width="60" />
        <el-table-column prop="key" label="名称" min-width="240" show-overflow-tooltip />
        <el-table-column prop="requests" label="请求数" width="110" />
        <el-table-column label="流量" width="120"><template #default="{ row }">{{ fmtBytes(row.bytes) }}</template></el-table-column>
        <el-table-column label="占比" min-width="200">
          <template #default="{ row }">
            <el-progress :percentage="rankRows[0]?.requests ? Math.round(row.requests * 100 / rankRows[0].requests) : 0" :stroke-width="10" :show-text="false" />
          </template>
        </el-table-column>
        <el-table-column label="下钻" width="80">
          <template #default="{ row }">
            <el-button v-if="rankDim === 'domain'" size="small" text type="primary" @click.stop="openDrill(row)">
              <i class="ti ti-arrows-diff"></i> 趋势
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-else description="近 24 小时暂无访问数据" :image-size="60" />
    </el-card>

    <!-- 域名下钻弹层：该站点近 24h 小时趋势 -->
    <el-dialog v-model="drillVisible" :title="`下钻 · ${drillHost} · 近 24h 小时趋势`" width="760px">
      <div v-if="drillRows.length">
        <div class="small text-muted mb-2">流量柱（灰）+ 请求量柱（蓝），悬停查看数值</div>
        <div class="d-flex align-items-end gap-1" style="height: 180px">
          <div v-for="d in drillRows" :key="d.label" class="flex-fill d-flex flex-column justify-content-end text-center"
            :title="`${d.label}: ${fmtBytes(d.bytes)} / ${d.requests} 请求`">
            <div class="bg-secondary-lt rounded-top" :style="{ height: Math.max(2, d.bytes * 100 / drillMax) + '%', opacity: 0.45 }"></div>
            <div class="bg-primary-lt" :style="{ height: Math.max(2, d.requests * 100 / drillReqMax) + '%' }"></div>
            <div class="text-muted" style="font-size: 10px; transform: scale(0.82)">{{ d.label.slice(-5) }}</div>
          </div>
        </div>
      </div>
      <el-empty v-else description="该站点近 24h 无访问数据" :image-size="60" />
      <template #footer>
        <el-button @click="router.push({ path: '/cdn/servers/logs', query: { status: '' } })">查看全部日志</el-button>
        <el-button type="primary" @click="drillVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 地区分布 -->
    <el-card class="mb-4">
      <template #header>
        <div class="d-flex justify-content-between align-items-center">
          <span>访客地区分布（近 24h）</span>
          <span class="text-muted small">独立 IP {{ geoTotal }}</span>
        </div>
      </template>
      <el-table v-if="geo.length" :data="geo" size="small" stripe>
        <el-table-column label="国家/地区" min-width="200"><template #default="{ row }">{{ row.country }}</template></el-table-column>
        <el-table-column label="独立 IP 数" width="120"><template #default="{ row }">{{ row.count }}</template></el-table-column>
        <el-table-column label="占比" min-width="240">
          <template #default="{ row }"><el-progress :percentage="geoTotal ? Math.round(row.count * 100 / geoTotal) : 0" :stroke-width="10" /></template></el-table-column>
      </el-table>
      <el-empty v-else description="正在解析访客 IP 归属（后台每 5 分钟批量解析），稍后刷新可见" :image-size="60" />
    </el-card>

    <!-- 客户端统计通用设计 -->
    <el-card class="mb-4">
      <template #header>
        <div class="d-flex justify-content-between align-items-center">
          <span>客户端统计（近 24h）</span>
          <span class="text-muted small">采样 {{ clients.sample }} 条请求</span>
        </div>
      </template>
      <div class="row" v-if="clients.sample > 0">
        <div class="col-md-6">
          <div class="small text-muted mb-2">浏览器</div>
          <div v-for="b in clients.browsers" :key="b.name" class="d-flex align-items-center mb-2">
            <span style="width: 80px" class="small">{{ b.name }}</span>
            <el-progress class="flex-fill ms-2" :percentage="Math.round(b.count * 100 / clients.sample)" :stroke-width="10" :show-text="false" />
            <span class="small text-muted ms-2">{{ b.count }}</span>
          </div>
        </div>
        <div class="col-md-6">
          <div class="small text-muted mb-2">操作系统</div>
          <div v-for="s in clients.systems" :key="s.name" class="d-flex align-items-center mb-2">
            <span style="width: 80px" class="small">{{ s.name }}</span>
            <el-progress class="flex-fill ms-2" :percentage="Math.round(s.count * 100 / clients.sample)" :stroke-width="10" :show-text="false" />
            <span class="small text-muted ms-2">{{ s.count }}</span>
          </div>
        </div>
      </div>
      <el-empty v-else description="暂无客户端数据" :image-size="60" />
    </el-card>

    <TCard title="快捷入口">
      <div class="row g-3">
        <div class="col-md-6 col-lg-3" v-for="f in features" :key="f.title">
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
  { title: '网站接入', desc: '添加站点、配置源站与缓存', icon: 'world', to: '/cdn/servers' },
  { title: '边缘节点', desc: '集群与节点在线监控', icon: 'server', to: '/cdn/clusters' },
  { title: '备案审核', desc: '域名 ICP 备案人工审核', icon: 'clipboard-check', to: '/cdn/dns/icp' },
  { title: '订单管理', desc: '充值/套餐/流量包订单', icon: 'receipt', to: '/cdn/finance/orders' },
  { title: '套餐管理', desc: '套餐与优惠券配置', icon: 'gift', to: '/cdn/plans' },
  { title: '用量统计', desc: '流量与请求趋势', icon: 'chart-area', to: '/cdn/servers/traffic-stats' },
  { title: 'WAF 防护', desc: 'CC/WAF 拦截事件', icon: 'shield-half', to: '/cdn/servers/cc' },
  { title: '证书管理', desc: '手动上传证书下发节点', icon: 'certificate', to: '/cdn/servers/certs' },
]
export default { data() { return { features } } }
</script>
