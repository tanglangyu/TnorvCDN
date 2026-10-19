<script setup lang="ts">
/** 负载均衡 —— 我的 L4 站点（/api/user/lb） */
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
const loadData = async () => (await cdnApi.get('/user/lb')) as any
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">负载均衡</h2><div class="text-muted">负载均衡 · 四层转发（TCP/TLS/UDP）</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows(data)" stripe>
            <el-table-column label="域名" min-width="200"><template #default="{ row }">{{ (row.domains||[]).join(', ') }}</template></el-table-column>
            <el-table-column prop="serverType" label="协议" width="90" />
            <el-table-column label="端口" width="120"><template #default="{ row }">{{ (row.l4Ports||[]).join(', ') }}</template></el-table-column>
            <el-table-column label="后端" min-width="180"><template #default="{ row }">{{ (row.origin||[]).join(', ') }}</template></el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }"><el-tag :type="row.enabled?'success':'info'" size="small">{{ row.enabled?'运行中':'停用' }}</el-tag></template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(data || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
          <el-empty v-if="!(data || []).length" description="暂无四层转发站点" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
