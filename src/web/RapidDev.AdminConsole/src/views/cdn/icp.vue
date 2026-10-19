<script setup lang="ts">
/** 备案审核 —— 域名备案人工审核（接入备案 API 前的过渡页面）
 *  数据源：/api/admin/servers 中 IcpStatus != approved 的站点
 *  操作：通过 / 驳回（备注可填）；通过后站点恢复正常加速资格。
 */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const statusFilter = ref('pending')

const loadData = async () => {
  const all = await cdnApi.get('/admin/servers')
  rows.value = (Array.isArray(all) ? all : all?.data || []).filter(
    (s: any) => (s.icpStatus || 'none') !== 'approved',
  )
  return rows.value
}

const filtered = computed(() =>
  statusFilter.value === 'all'
    ? rows.value
    : rows.value.filter((r) => (r.icpStatus || 'none') === statusFilter.value),
)

const regionLabel = (r: string) =>
  ({ global: '全球', cn: '中国大陆', overseas: '非中国大陆' } as any)[r] || r
const statusLabel = (st: string) =>
  ({ none: '未提交', pending: '待审核', approved: '已备案', rejected: '已驳回' } as any)[st] || st
const statusTag = (st: string) =>
  ({ none: 'info', pending: 'warning', approved: 'success', rejected: 'danger' } as any)[st] || 'info'

async function review(row: any, status: 'approved' | 'rejected') {
  let note = ''
  if (status === 'rejected') {
    const { value } = await ElMessageBox.prompt('请填写驳回原因（会展示给提交方）', '驳回备案', {
      confirmButtonText: '确认驳回',
      cancelButtonText: '取消',
      inputPlaceholder: '例如：备案号与域名不一致',
    })
    note = value || ''
  } else {
    await ElMessageBox.confirm(
      `确认域名 ${(row.domains || []).join(', ')} 备案号 ${row.icpNumber || '（未填写）'} 审核通过？`,
      '通过备案',
      { confirmButtonText: '确认通过', cancelButtonText: '取消' },
    )
  }
  try {
    await cdnApi.post(`/admin/servers/icp/${row.id}/review`, {
      status,
      icpNumber: row.icpNumber || null,
      note,
    })
    ElMessage.success(status === 'approved' ? '已通过备案审核' : '已驳回')
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || e?.message || '操作失败')
  }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">备案审核</h2><div class="text-muted">域名解析 · 接入备案 API 前的人工审核</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default>
        <el-card>
          <el-alert type="info" :closable="false" class="mb-3"
            title="中国大陆节点的域名必须完成备案才能加速。备案 API 接入前，由管理员在此人工核对备案号并审核；审核通过后站点自动恢复加速资格。" />
          <div class="mb-3">
            <el-radio-group v-model="statusFilter" size="small">
              <el-radio-button value="pending">待审核</el-radio-button>
              <el-radio-button value="rejected">已驳回</el-radio-button>
              <el-radio-button value="none">未提交</el-radio-button>
              <el-radio-button value="all">全部</el-radio-button>
            </el-radio-group>
          </div>
          <el-table :data="filtered" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column label="域名" min-width="200">
              <template #default="{ row }">{{ (row.domains || []).join(', ') }}</template>
            </el-table-column>
            <el-table-column label="加速区域" width="110">
              <template #default="{ row }">
                <el-tag size="small" :type="row.region === 'cn' ? 'warning' : 'info'">{{ regionLabel(row.region) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="备案号" min-width="180">
              <template #default="{ row }">{{ row.icpNumber || '—' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTag(row.icpStatus || 'none')">{{ statusLabel(row.icpStatus || 'none') }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="备注" min-width="160">
              <template #default="{ row }">{{ row.icpNote || '—' }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" type="success" plain @click="review(row, 'approved')">通过</el-button>
              <el-button size="small" type="danger" plain @click="review(row, 'rejected')">驳回</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
