<script setup lang="ts">
/** 工单分类 —— 工单按优先级聚合 */
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
  const list = await cdnApi.get('/admin/tickets')
  const m = new Map<string, number>()
  for (const t of list as any[]) m.set(t.priority || 'medium', (m.get(t.priority || 'medium') || 0) + 1)
  rows.value = [...m.entries()].map(([priority, count]) => ({ priority, count }))
  return rows.value
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">分类</h2><div class="text-muted">工单系统 · 工单按优先级统计</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="priority" label="优先级" width="140" />
            <el-table-column prop="count" label="数量" width="110" />
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无工单" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
