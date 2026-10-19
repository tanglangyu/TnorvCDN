<script setup lang="ts">
/** 证书签发日志（用户端，只读）—— 展示自己站点的签发/续期/失败时间线，排障用 */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const page = ref(1)
const total = ref(0)
const rows = ref<any[]>([])

const statusLabel = (s: string) => ({
  pending_dns: '未解析', pending_issuing: '签发中', issued: '签发成功', failed: '签发失败',
  renew_requested: '发起续期', timeout: '签发超时',
} as any)[s] || s
const statusTag = (s: string) => ({
  pending_dns: 'warning', pending_issuing: 'primary', issued: 'success',
  failed: 'danger', renew_requested: 'info', timeout: 'danger',
} as any)[s] || 'info'

const loadData = async () => {
  const r = await cdnApi.get(`/user/cert-events?page=${page.value}&pageSize=20`) as any
  rows.value = r.items || []
  total.value = r.total || 0
  return rows.value
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">证书签发日志</h2><div class="text-muted">CDN加速 · 我的证书签发记录（只读）</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="rows" stripe>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ new Date(row.createdAt).toLocaleString() }}</template>
            </el-table-column>
            <el-table-column prop="domain" label="域名" min-width="200" />
            <el-table-column label="事件" width="110">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="error" label="详情" min-width="280" />
          </el-table>
          <el-pagination class="mt-3" layout="prev, pager, next, total" :total="total" :page-size="20"
            :current-page="page" @current-change="(p: number) => { page = p; refresh() }" />
          <div class="text-muted small mt-2">
            签发失败/未解析时，请确认域名已 CNAME 或 A 记录解析到节点；解析生效后系统会自动重试，也可在「我的网站 → 站点配置」中手动操作。
          </div>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
