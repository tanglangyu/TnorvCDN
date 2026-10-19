<script setup lang="ts">
/** 日志策略 —— 日志统计（/admin/logs/stats 近7天） */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'
const gate = ref()

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = rows.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})
const rows = ref<any[]>([])
const loadData = async () => {
  const to = new Date(); const from = new Date(Date.now() - 7*86400000)
  const data = await cdnApi.get('/admin/logs/stats', { from: from.toISOString(), to: to.toISOString() }) as any
  rows.value = [
    { metric: '总请求数', value: data.totalRequests },
    { metric: '总流量', value: (data.totalBytes/1073741824).toFixed(2)+' GB' },
    { metric: '缓存命中率', value: data.cacheHitRate + '%' },
    { metric: '4xx 响应', value: data.status4xx },
    { metric: '5xx 响应', value: data.status5xx },
  ]
  return rows.value
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">日志策略</h2><div class="text-muted">网站列表 · 日志统计（近7天）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="metric" label="指标" min-width="180" />
            <el-table-column prop="value" label="值" width="180" />
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
