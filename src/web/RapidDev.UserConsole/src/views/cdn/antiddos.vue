<script setup lang="ts">
/** 高防实例列表（生效中） */
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
const loadData = async () => (await cdnApi.get('/user/anti-ddos/instances')) as any
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">实例列表</h2><div class="text-muted">DDoS高防 · 生效中的高防实例</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows(data)" stripe>
            <el-table-column prop="name" label="实例名" min-width="160" />
            <el-table-column prop="ip" label="高防 IP" width="140" />
            <el-table-column prop="line" label="线路" width="90" />
            <el-table-column label="保底/峰值 (Mbps)" width="150">
              <template #default="{ row }">{{ row.protectedMbps }} / {{ row.peakMbps }}</template>
            </el-table-column>
            <el-table-column prop="protectedTargets" label="防护对象" show-overflow-tooltip />
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(data || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
          <el-empty v-if="!(data || []).length" description="暂无高防实例" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
