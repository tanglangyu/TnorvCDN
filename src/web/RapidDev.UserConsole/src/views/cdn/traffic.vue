<script setup lang="ts">
/** 用量统计 —— 对接 TnorvCDN 主控 /api/user/traffic-stats（近7天） */
import { computed, ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'


const pg = ref({ page: 1, size: 20 })
const fmtBytes = (n?: number) => {
  const b = n || 0
  if (b >= 1073741824) return (b / 1073741824).toFixed(2) + ' GB'
  if (b >= 1048576) return (b / 1048576).toFixed(2) + ' MB'
  if (b >= 1024) return (b / 1024).toFixed(1) + ' KB'
  return b + ' B'
}
const filterQ = ref('')
const viewRows = (data: any[]) => {
  const start = (pg.value.page - 1) * pg.value.size
  const kw = (filterQ.value || '').trim().toLowerCase()
  const list = kw ? (data || []).filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw)) : (data || [])
  return list.slice(start, start + pg.value.size)
}
const loadData = async () => (await cdnApi.get('/user/traffic-stats')) as any

// ---- 图表（无 echarts，纯 SVG 柱状/折线混合） ----
const chartData = computed(() => {
  const rows = ((data as any).value || []) as any[]  // 由 onData 填充
  return [...rows].sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
})
const data = ref<any[]>([])
const onData = (d: any) => { data.value = Array.isArray(d) ? d : (d?.items ?? []); return d }
const maxBytes = computed(() => Math.max(1, ...chartData.value.map(r => r.bytes || 0)))
const maxReq = computed(() => Math.max(1, ...chartData.value.map(r => r.requests || 0)))
const W = 900, H = 220, PAD_L = 60, PAD_B = 28, PAD_T = 10
const bw = computed(() => chartData.value.length ? (W - PAD_L - 10) / chartData.value.length : 0)
const xC = (i: number) => PAD_L + i * bw.value + bw.value / 2
const yB = (v: number) => PAD_T + (H - PAD_B - PAD_T) * (1 - v / maxBytes.value)
const yR = (v: number) => PAD_T + (H - PAD_B - PAD_T) * (1 - v / maxReq.value)
const linePts = computed(() => chartData.value.map((r, i) => `${xC(i)},${yR(r.requests || 0)}`).join(' '))
const fmtAxis = (v: number) => v >= 1073741824 ? (v / 1073741824).toFixed(0) + 'G' : v >= 1048576 ? (v / 1048576).toFixed(0) + 'M' : v >= 1024 ? (v / 1024).toFixed(0) + 'K' : String(v)
const tip = ref('')
const tipX = ref(0)
const showTip = (r: any, i: number) => { tip.value = `${new Date(r.date).toLocaleDateString()}：${fmtBytes(r.bytes)} · ${r.requests} 请求 · 命中 ${r.hits || 0}`; tipX.value = xC(i) }
const clearTip = () => { tip.value = '' }
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">用量统计</h2><div class="text-muted">CDN加速 · 近 7 天</div></div>
    <CdnGate :load-data="loadData" @data="onData">
      <template #default="{ data: tableData }">
        <!-- 近7天流量图表（SVG：柱=流量，折线=请求数） -->
        <el-card class="mb-3" v-if="chartData.length">
          <div class="mb-2" style="font-weight:600">近 7 天趋势</div>
          <svg :viewBox="`0 0 ${W} ${H}`" style="width:100%;height:auto">
            <line :x1="PAD_L" :y1="PAD_T" :x2="PAD_L" :y2="H - PAD_B" stroke="#ddd" />
            <line :x1="PAD_L" :y1="H - PAD_B" :x2="W - 10" :y2="H - PAD_B" stroke="#ddd" />
            <text :x="8" :y="yB(maxBytes) + 4" font-size="10" fill="#999">{{ fmtAxis(maxBytes) }}</text>
            <line :x1="PAD_L" :y1="yB(maxBytes)" :x2="W - 10" :y2="yB(maxBytes)" stroke="#eee" />
            <g v-for="(r, i) in chartData" :key="i">
              <rect :x="xC(i) - bw * 0.3" :y="yB(r.bytes || 0)" :width="bw * 0.6"
                :height="Math.max(1, H - PAD_B - yB(r.bytes || 0))" fill="#409EFF" opacity="0.75"
                @mouseenter="showTip(r, i)" @mouseleave="clearTip" style="cursor:pointer" />
              <text :x="xC(i)" :y="H - 10" font-size="10" fill="#666" text-anchor="middle">{{ new Date(r.date).getMonth() + 1 }}/{{ new Date(r.date).getDate() }}</text>
            </g>
            <polyline :points="linePts" fill="none" stroke="#E6A23C" stroke-width="2" />
            <circle v-for="(r, i) in chartData" :key="'p' + i" :cx="xC(i)" :cy="yR(r.requests || 0)" r="3" fill="#E6A23C" />
            <text v-if="tip" :x="Math.min(tipX + 6, W - 220)" :y="PAD_T + 12" font-size="11" fill="#333">{{ tip }}</text>
          </svg>
          <div class="text-muted small">
            <span style="color:#409EFF">■</span> 流量&nbsp;&nbsp;<span style="color:#E6A23C">●</span> 请求数
          </div>
        </el-card>

        <el-card>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows(tableData)" stripe>
            <el-table-column label="日期" width="130">
              <template #default="{ row }">{{ new Date(row.date).toLocaleDateString() }}</template>
            </el-table-column>
            <el-table-column prop="requests" label="请求数" width="110" />
            <el-table-column label="缓存命中" width="110">
              <template #default="{ row }">{{ row.hits }}（{{ row.requests ? Math.round(row.hits * 100 / row.requests) : 0 }}%）</template>
            </el-table-column>
            <el-table-column prop="blocked" label="拦截数" width="100" />
            <el-table-column label="流量" width="120">
              <template #default="{ row }">{{ fmtBytes(row.bytes) }}</template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(tableData || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
          <el-empty v-if="!(tableData || []).length" description="暂无用量数据" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
