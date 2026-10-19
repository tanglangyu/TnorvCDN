<script setup lang="ts">
/** 访问日志 —— 对接 TnorvCdn 主控 /api/admin/logs
 * A4 增强：状态码彩色 chip · URI 一键复制 · 筛选记忆(localStorage) ·
 * 看板跳转预筛选(status/serverId/remoteAddr) · 实时尾随(3s 轮询, 最多 200 条缓冲) */
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute } from 'vue-router'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const route = useRoute()
const gate = ref()
const logs = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 20
const MEM_KEY = 'tnorv.accesslogs.filter'
const tailOn = ref(false)
let tailTimer: any = null

const filter = ref({
  serverId: undefined as number | undefined,
  status: undefined as number | undefined,
  cacheStatus: '',
  remoteAddr: '',
})

// —— 筛选记忆：localStorage 持久化 + 看板跳转参数优先 ——
function loadFilter() {
  try {
    const saved = JSON.parse(localStorage.getItem(MEM_KEY) || '{}')
    if (saved.serverId) filter.value.serverId = saved.serverId
    if (saved.status) filter.value.status = saved.status
    if (saved.cacheStatus) filter.value.cacheStatus = saved.cacheStatus
    if (saved.remoteAddr) filter.value.remoteAddr = saved.remoteAddr
  } catch { /* ignore */ }
  // 看板下钻带参（优先级最高）
  const q: any = route.query || {}
  if (q.status !== undefined && q.status !== '') filter.value.status = Number(q.status)
  if (q.serverId !== undefined && q.serverId !== '') filter.value.serverId = Number(q.serverId)
  if (q.remoteAddr) filter.value.remoteAddr = String(q.remoteAddr)
}
watch(filter, (f) => {
  try { localStorage.setItem(MEM_KEY, JSON.stringify(f)) } catch { /* ignore */ }
}, { deep: true })

const loadData = async () => {
  const params: any = { page: page.value, pageSize }
  if (filter.value.serverId) params.serverId = filter.value.serverId
  if (filter.value.status) params.status = filter.value.status
  if (filter.value.cacheStatus) params.cacheStatus = filter.value.cacheStatus
  if (filter.value.remoteAddr) params.remoteAddr = filter.value.remoteAddr
  const resp = await cdnApi.get('/admin/logs', params)
  logs.value = (resp as any).logs ?? (resp as any).items ?? resp ?? []
  total.value = (resp as any).total ?? logs.value.length
  return resp
}

function search() { page.value = 1; gate.value?.refresh?.() }
function resetFilter() {
  filter.value = { serverId: undefined, status: undefined, cacheStatus: '', remoteAddr: '' }
  page.value = 1
  gate.value?.refresh?.()
}

// —— 状态码彩色 chip ——
const statusTag = (s: number) =>
  s >= 500 ? 'danger' : s >= 400 ? 'warning' : s >= 300 ? 'info' : 'success'

// —— URI 一键复制 ——
async function copyUri(row: any) {
  const text = (row.uri || '').toString()
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('URI 已复制')
  } catch {
    const ta = document.createElement('textarea')
    ta.value = text
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    ElMessage.success('URI 已复制')
  }
}

// —— 实时尾随：3s 轮询最新一页，追加去重，最多 200 条 ——
async function tailTick() {
  try {
    const resp: any = await cdnApi.get('/admin/logs', { page: 1, pageSize: 20 })
    const items = resp?.logs ?? resp?.items ?? []
    if (items.length) {
      const seen = new Set(logs.value.slice(0, 40).map((l: any) => `${l.timestamp}|${l.host}|${l.uri}|${l.remoteAddr}`))
      const fresh = items.filter((l: any) => !seen.has(`${l.timestamp}|${l.host}|${l.uri}|${l.remoteAddr}`))
      if (fresh.length) {
        logs.value = [...fresh, ...logs.value].slice(0, 200)
        ElMessage.info({ message: `实时尾随 +${fresh.length} 条`, duration: 1500 })
      }
    }
  } catch { /* 静默：尾随失败不打断页面 */ }
}
function toggleTail(v: boolean) {
  tailOn.value = v
  if (tailTimer) { clearInterval(tailTimer); tailTimer = null }
  if (v) { ElMessage.success('实时尾随已开启（每 3 秒）'); tailTimer = setInterval(tailTick, 3000) }
}
onMounted(() => { loadFilter() })
onBeforeUnmount(() => { if (tailTimer) clearInterval(tailTimer) })
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">访问日志</h2><div class="text-muted">网站列表 · 边缘节点实时上报 · 筛选条件自动记忆</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2 flex-wrap align-items-center">
            <el-input-number v-model="filter.serverId" placeholder="网站ID" :min="1" size="small" style="width:140px" @change="search" />
            <el-select v-model="filter.status" placeholder="状态码" size="small" style="width:130px" clearable filterable allow-create @change="search">
              <el-option v-for="s in [200, 204, 206, 301, 302, 304, 403, 404, 429, 500, 502, 503]" :key="s" :value="s" :label="String(s)" />
            </el-select>
            <el-select v-model="filter.cacheStatus" placeholder="缓存状态" size="small" style="width:130px" clearable @change="search">
              <el-option value="HIT" /><el-option value="MISS" /><el-option value="STALE" /><el-option value="PEER-HIT" /><el-option value="BYPASS" />
            </el-select>
            <el-input v-model="filter.remoteAddr" placeholder="客户端IP" size="small" style="width:160px" clearable @keyup.enter="search" @clear="search" />
            <el-button size="small" type="primary" @click="search()">查询</el-button>
            <el-button size="small" @click="resetFilter">重置</el-button>
            <div class="ms-auto d-flex align-items-center gap-2">
              <span class="small" :class="tailOn ? 'text-success' : 'text-muted'">
                <i class="ti" :class="tailOn ? 'ti-broadcast text-success' : 'ti-player-play'"></i>
                实时尾随（3s · 缓冲 {{ logs.length }}/200）
              </span>
              <el-switch v-model="tailOn" @change="toggleTail" />
            </div>
          </div>
          <el-table :data="logs" stripe size="small">
            <el-table-column prop="timestamp" label="时间" width="165">
              <template #default="{ row }">{{ row.timestamp ? new Date(row.timestamp).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column prop="host" label="域名" width="150" show-overflow-tooltip />
            <el-table-column prop="uri" label="URI" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="d-inline-flex align-items-center" style="cursor: pointer" title="点击复制 URI" @click="copyUri(row)">
                  <span class="me-1 text-truncate">{{ row.uri }}</span>
                  <i class="ti ti-copy text-muted"></i>
                </span>
              </template>
            </el-table-column>
            <el-table-column prop="status" label="状态" width="80">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTag(row.status)" effect="dark" style="width:52px; text-align:center">{{ row.status }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="cacheStatus" label="缓存" width="95">
              <template #default="{ row }">
                <el-tag v-if="row.cacheStatus" size="small" :type="row.cacheStatus === 'HIT' ? 'success' : row.cacheStatus === 'MISS' ? 'info' : 'warning'">{{ row.cacheStatus }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="remoteAddr" label="客户端IP" width="135" />
            <el-table-column prop="requestTimeMs" label="耗时(ms)" width="90">
              <template #default="{ row }">{{ row.requestTimeMs?.toFixed?.(1) ?? row.requestTimeMs }}</template>
            </el-table-column>
            <el-table-column prop="userAgent" label="UA" min-width="150" show-overflow-tooltip />
          </el-table>
          <el-pagination class="mt-3" layout="prev, pager, next" :total="total" :page-size="pageSize"
            :current-page="page" @current-change="(p: number) => { page = p; gate?.refresh?.() }" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
