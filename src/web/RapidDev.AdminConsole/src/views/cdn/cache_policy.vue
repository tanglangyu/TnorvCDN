<script setup lang="ts">
/** 缓存策略 —— 各站点缓存配置（来自 /admin/servers） */
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
  const list = await cdnApi.get('/admin/servers')
  rows.value = (list as any[]).map(s => ({
    id: s.id, domains: s.domains,
    cacheEnabled: s.cache?.enabled ?? false,
    cacheRules: s.cache?.rules?.length ?? 0,
  }))
  return rows.value
}
function fmtVal(row: any, col: any) {
  const v = row[col.k]
  if (col.fmt === 'bool') return v ? '是' : '否'
  if (col.fmt === 'join') return Array.isArray(v) ? v.join(', ') : v
  return v ?? '-'
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">缓存策略</h2><div class="text-muted">网站列表 · 各站点缓存配置</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="id" label="ID" width="70" />
            <el-table-column label="域名" min-width="210"><template #default="{ row }">{{ (row.domains||[]).join(', ') }}</template></el-table-column>
            <el-table-column label="缓存" width="100"><template #default="{ row }">{{ row.cacheEnabled ? '启用' : '停用' }}</template></el-table-column>
            <el-table-column prop="cacheRules" label="规则数" width="100" />
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
