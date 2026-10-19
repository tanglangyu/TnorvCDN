<script setup lang="ts">
/** 拦截日志（U4）—— 原因分类 chips 筛选 + IP 点击弹出该 IP 全部拦截记录 */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const page = ref(1)
const pageSize = 20
const reasonFilter = ref('')
const ipDialog = ref(false)
const ipRows = ref<any[]>([])
const ipCurrent = ref('')

const loadData = async () => {
  const r = (await cdnApi.get('/user/waf/logs', { page: page.value, pageSize })) as any
  const d = r?.data ?? r
  return d
}

// 原因分类（把 blockReason 归到大类 chip）
const reasonChip = (reason?: string) => {
  const r = (reason || '其他').toLowerCase()
  if (r.includes('cc') || r.includes('rate')) return 'CC限流'
  if (r.includes('ua') || r.includes('agent')) return 'UA拦截'
  if (r.includes('geo') || r.includes('country') || r.includes('region') || r.includes('prov')) return '地域封禁'
  if (r.includes('referer') || r.includes('防盗链') || r.includes('hotlink')) return '防盗链'
  if (r.includes('ip')) return 'IP黑名单'
  if (r.includes('js') || r.includes('shield') || r.includes('challenge')) return '5秒盾'
  return '其他'
}
const chips = computed(() => {
  const items = (gateData.value?.items || []) as any[]
  const set = new Map<string, number>()
  for (const l of items) { const c = reasonChip(l.blockReason); set.set(c, (set.get(c) || 0) + 1) }
  return [...set.entries()].map(([name, count]) => ({ name, count }))
})
const gateData = ref<any>(null)
const onData = (d: any) => { gateData.value = d; return d }

const chipMatch = (l: any) => !reasonFilter.value || reasonChip(l.blockReason) === reasonFilter.value
const filtered = computed(() => ((gateData.value?.items || []) as any[]).filter(chipMatch))

async function showIp(ip: string) {
  ipCurrent.value = ip
  ipDialog.value = true
  const r = (await cdnApi.get('/user/waf/logs', { page: 1, pageSize: 200 })) as any
  const d = r?.data ?? r
  ipRows.value = (d?.items || []).filter((l: any) => l.remoteAddr === ip).slice(0, 50)
}

const go = (p: number) => { page.value = p }

// 快捷加黑：将 IP 加入本人站点黑名单（POST /user/waf/blacklist）
const adding = ref('')
const quickBan = async (ip: string) => {
  adding.value = ip
  try {
    const res: any = await cdnApi.post('/user/waf/blacklist', { ip, note: '来自拦截日志快捷加黑' })
    if (res?.code === 200 || res?.ok) ElMessage.success(res?.message || `已将 ${ip} 加入黑名单`)
    else ElMessage.warning(res?.message || '加黑失败')
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.message || '加黑失败')
  } finally { adding.value = '' }
}

// 导出 CSV（当前筛选结果，前端生成下载）
const exportCsv = () => {
  const rows = filtered.value
  if (!rows.length) return ElMessage.warning('当前没有可导出的记录')
  const head = ['时间', '域名', 'URI', '状态', '拦截原因', '原始原因', '来源IP']
  const lines = rows.map(r => [
    r.timestamp ? new Date(r.timestamp).toLocaleString() : '-',
    r.host || '', r.uri || '', r.status || '',
    reasonChip(r.blockReason), r.blockReason || '', r.remoteAddr || '',
  ].map(v => `"${String(v).replace(/"/g, '""')}"`).join(','))
  const csv = '﻿' + [head.join(','), ...lines].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `waf-拦截日志-${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(a.href)
  ElMessage.success(`已导出 ${rows.length} 条记录`)
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">拦截日志</h2><div class="text-muted">WAF安全 · WAF/CC 拦截记录，点击原因 chip 筛选、点击 IP 查看详情</div></div>
    <CdnGate :load-data="loadData" @data="onData">
      <template #default="{ data, refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center flex-wrap">
            <el-check-tag :checked="reasonFilter === ''" @change="reasonFilter = ''">全部</el-check-tag>
            <el-check-tag v-for="c in chips" :key="c.name" :checked="reasonFilter === c.name" @change="reasonFilter = c.name">
              {{ c.name }} ({{ c.count }})
            </el-check-tag>
            <el-button size="small" class="ms-auto" @click="exportCsv">导出CSV</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="filtered" stripe>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ row.timestamp ? new Date(row.timestamp).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column prop="host" label="域名" width="150" />
            <el-table-column prop="uri" label="URI" min-width="200" show-overflow-tooltip />
            <el-table-column prop="status" label="状态" width="70" />
            <el-table-column label="拦截原因" min-width="140">
              <template #default="{ row }"><el-tag size="small" type="danger">{{ reasonChip(row.blockReason) }}</el-tag>
                <span class="ms-1 text-muted small" v-if="row.blockReason">{{ row.blockReason }}</span></template>
            </el-table-column>
            <el-table-column label="来源IP" width="200">
              <template #default="{ row }">
                <el-link type="primary" :underline="false" @click="showIp(row.remoteAddr)">{{ row.remoteAddr }}</el-link>
                <el-button class="ms-1" size="small" type="danger" plain link :loading="adding === row.remoteAddr"
                  @click="quickBan(row.remoteAddr)">加黑</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-empty v-if="!filtered.length" description="暂无拦截记录（网站运行正常）" />
          <el-pagination v-if="(data?.total || 0) > pageSize" class="mt-3" layout="prev, pager, next"
            :total="data?.total || 0" :page-size="pageSize" :current-page="page"
            @current-change="(p: number) => { go(p); refresh() }" />
        </el-card>

        <el-dialog v-model="ipDialog" :title="`IP ${ipCurrent} 的拦截记录`" width="720px">
          <el-table :data="ipRows" size="small" stripe max-height="420">
            <el-table-column label="时间" width="160">
              <template #default="{ row }">{{ row.timestamp ? new Date(row.timestamp).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column prop="host" label="域名" width="140" />
            <el-table-column prop="uri" label="URI" min-width="180" show-overflow-tooltip />
            <el-table-column prop="blockReason" label="原因" min-width="140" show-overflow-tooltip />
          </el-table>
          <el-empty v-if="!ipRows.length" description="无记录" :image-size="50" />
          <template #footer>
            <el-button type="danger" size="small" :loading="adding === ipCurrent" @click="quickBan(ipCurrent); ipDialog = false">
              将 {{ ipCurrent || '该IP' }} 加入黑名单
            </el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
