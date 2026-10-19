<script setup lang="ts">
/** 域名分组 —— 我的 DNS 记录按分组聚合（/api/user/ns/domains） */
import { computed, ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const pg = ref({ page: 1, size: 20 })
const filterQ = ref('')
const viewRows = (data: any[]) => {
  const start = (pg.value.page - 1) * pg.value.size
  const kw = (filterQ.value || '').trim().toLowerCase()
  const list = kw ? (data || []).filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw)) : (data || [])
  return list.slice(start, start + pg.value.size)
}
const loadData = async () => {
  const r = await cdnApi.get('/user/ns/domains') as any
  const items = r?.data || []
  const byGroup: any = {}
  for (const d of items) {
    const g = d.group || '（未分组）'
    byGroup[g] = byGroup[g] || { group: g, count: 0, domains: [] }
    byGroup[g].count++
    byGroup[g].domains.push(d.name)
  }
  return Object.values(byGroup)
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">域名分组</h2><div class="text-muted">域名解析 · 我的 DNS 记录分组</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
          <el-table v-if="(data || []).length" :data="viewRows(data)" stripe>
            <el-table-column prop="group" label="分组" width="180" />
            <el-table-column prop="count" label="记录数" width="100" />
            <el-table-column label="包含域名" min-width="360">
              <template #default="{ row }">{{ (row.domains || []).join(', ') }}</template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(data || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
          <el-empty v-if="!(data || []).length" description="暂无分组（在「我的域名」编辑记录时填写分组即可归类）" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
