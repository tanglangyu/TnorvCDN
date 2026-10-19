<script setup lang="ts">
/** 证书签发日志 —— 对接 TnorvCdn 主控 /api/admin/cert-events。
 *  管理员可查看的签发/续期/失败时间线 + 当前各站点证书状态总览。 */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(30)
const filterStatus = ref('')
const filterDomain = ref('')
const statuses = ref<any[]>([])
const renewingIds = ref<number[]>([])

const statusLabel = (s: string) => ({
  pending_dns: '未解析', pending_issuing: '签发中', issued: '已签发', failed: '签发失败',
  renew_requested: '发起续期', timeout: '签发超时',
} as any)[s] || s
const statusTag = (s: string) => ({
  pending_dns: 'warning', pending_issuing: 'primary', issued: 'success',
  failed: 'danger', renew_requested: 'info', timeout: 'danger',
} as any)[s] || 'info'
const stateLabel = (s: string) => ({ pending_dns: '未解析', pending_issuing: '签发中', issued: '已签发', failed: '签发失败' } as any)[s] || s
const stateTag = (s: string) => ({ pending_dns: 'warning', pending_issuing: 'primary', issued: 'success', failed: 'danger' } as any)[s] || 'info'

const loadData = async () => {
  const [ev, st] = await Promise.all([
    cdnApi.get(`/admin/cert-events?page=${page.value}&size=${size.value}` +
      (filterStatus.value ? `&status=${filterStatus.value}` : '') +
      (filterDomain.value ? `&domain=${encodeURIComponent(filterDomain.value)}` : '')),
    cdnApi.get('/admin/ssl/status'),
  ])
  rows.value = ev.items || []
  total.value = ev.total || 0
  statuses.value = st || []
  return rows.value
}

function search() { page.value = 1; gate.value?.refresh?.() }

async function retry(row: any) {
  renewingIds.value.push(row.serverId)
  try {
    await cdnApi.post(`/admin/servers/${row.serverId}/ssl/retry`)
    ElMessage.success('重试请求已下发')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '发起重试失败') }
  finally { renewingIds.value = renewingIds.value.filter((x: number) => x !== row.serverId) }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">证书签发日志</h2><div class="text-muted">网站列表 · 证书签发/续期/失败时间线与状态总览</div></div>
      </div>
    </div>

    <el-card class="mb-4">
      <template #header><span>证书状态总览</span></template>
      <el-table :data="statuses" stripe>
        <el-table-column prop="serverId" label="站点ID" width="80" />
        <el-table-column label="域名" min-width="220">
          <template #default="{ row }">{{ (row.domains || []).join(', ') }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag size="small" :type="stateTag(row.sslState)">{{ stateLabel(row.sslState) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="到期时间" width="120">
          <template #default="{ row }">{{ row.expiresAt ? new Date(row.expiresAt).toLocaleDateString() : '—' }}</template>
        </el-table-column>
        <el-table-column label="最近尝试" width="160">
          <template #default="{ row }">{{ row.lastAttemptAt ? new Date(row.lastAttemptAt).toLocaleString() : '—' }}</template>
        </el-table-column>
        <el-table-column label="最近错误" min-width="240">
          <template #default="{ row }"><span class="small">{{ row.lastError || '—' }}</span></template>
        </el-table-column>
        <el-table-column label="操作" fixed="right">
          <template #default="{ row }">
            <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
          <el-button size="small" type="warning" plain :loading="renewingIds.includes(row.serverId)"
          @click="retry(row)">重试</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-select v-model="filterStatus" placeholder="状态" clearable style="width:140px" size="small" @change="search">
              <el-option v-for="(label, val) in { pending_dns: '未解析', pending_issuing: '签发中', issued: '已签发', failed: '签发失败', renew_requested: '发起续期', timeout: '签发超时' }" :key="val" :value="val" :label="label" />
            </el-select>
            <el-input v-model="filterDomain" placeholder="域名过滤" clearable style="width:220px" size="small" @change="search" />
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column prop="id" label="ID" width="70" />
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ new Date(row.createdAt).toLocaleString() }}</template>
            </el-table-column>
            <el-table-column prop="serverId" label="站点" width="70" />
            <el-table-column prop="domain" label="域名" min-width="200" />
            <el-table-column label="事件" width="110">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="nodeId" label="节点" width="70">
              <template #default="{ row }">{{ row.nodeId || '主控' }}</template>
            </el-table-column>
            <el-table-column prop="error" label="详情" min-width="260" />
          </el-table>
          <el-pagination class="mt-3" layout="prev, pager, next, total" :total="total"
            :page-size="size" :current-page="page" @current-change="(p: number) => { page = p; gate?.refresh?.() }" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
