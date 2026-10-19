<script setup lang="ts">
/** 操作日志 —— 主控审计日志（/api/admin/audit） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 20

const loadData = async () => {
  const resp = await cdnApi.get('/admin/audit', { page: page.value, size: pageSize }) as any
  rows.value = resp?.items ?? []
  total.value = resp?.total ?? 0
  return resp
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">操作日志</h2><div class="text-muted">日志审计 · 管理端敏感操作审计</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="rows" stripe>
            <el-table-column prop="timestamp" label="时间" width="170">
              <template #default="{ row }">{{ row.timestamp ? new Date(row.timestamp).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column prop="userId" label="操作人ID" width="100" />
            <el-table-column prop="action" label="操作" min-width="200" />
            <el-table-column prop="detail" label="详情" min-width="140" />
            <el-table-column prop="ip" label="来源IP" width="140" />
          </el-table>
          <el-empty v-if="!rows.length" description="暂无审计记录" />
          <el-pagination v-if="total > pageSize" class="mt-3" layout="prev, pager, next"
            :total="total" :page-size="pageSize" :current-page="page"
            @current-change="(p: number) => { page = p; gate?.refresh?.() }" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
