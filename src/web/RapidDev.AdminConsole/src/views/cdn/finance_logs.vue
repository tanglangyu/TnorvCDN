<script setup lang="ts">
/** 收支明细 —— 用量记录（/admin/usage 按用户聚合） */
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
const period = ref('')
const loadData = async () => {
  const data = await cdnApi.get('/admin/usage') as any
  period.value = `${data.year}-${String(data.month).padStart(2,'0')}`
  rows.value = data.usage || []
  return rows.value
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">收支明细</h2><div class="text-muted">财务管理 · 本月用量（{{ period }}）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="userId" label="用户ID" width="110" />
            <el-table-column prop="requests" label="请求数" width="120" />
            <el-table-column prop="trafficMB" label="流量(MB)" width="120" />
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="本月暂无用量" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
