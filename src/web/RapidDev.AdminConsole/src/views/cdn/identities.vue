<script setup lang="ts">
/**
 * 实名认证审核 —— 通用设计：
 * 用户提交的个人/企业认证列表，管理员通过/驳回。
 */
import { ref, onMounted } from 'vue'
import { http } from '@/utils/request'
import { ElMessage, ElMessageBox } from 'element-plus'

const items = ref<any[]>([])
const loading = ref(false)
const statusFilter = ref('')
const load = async () => {
  loading.value = true
  try {
    items.value = await http.get('/admin/identities', statusFilter.value ? { status: statusFilter.value } : {})
  } finally {
    loading.value = false
  }
}
onMounted(load)

const review = async (row: any, status: 'approved' | 'rejected') => {
  let note = ''
  if (status === 'rejected') {
    try {
      const { value } = await ElMessageBox.prompt('请填写驳回原因', '驳回认证', { inputPlaceholder: '如：证件号模糊' })
      note = value || ''
    } catch { return }
  } else {
    await ElMessageBox.confirm(`通过用户「${row.username}」的${row.type === 'enterprise' ? '企业' : '个人'}认证？`, '确认', { type: 'warning' })
  }
  await http.post(`/admin/identities/${row.id}/review`, { status, note })
  ElMessage.success(status === 'approved' ? '已通过' : '已驳回')
  await load()
}
const fmtTime = (t: string) => (t ? new Date(t).toLocaleString() : '—')
const statusTag: Record<string, string> = { pending: 'warning', approved: 'success', rejected: 'danger' }
const statusLabel: Record<string, string> = { pending: '待审核', approved: '已通过', rejected: '已驳回' }
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">实名认证</h2>
          <div class="text-muted">用户提交的个人/企业实名信息审核</div>
        </div>
        <div class="col-auto ms-auto">
          <el-radio-group v-model="statusFilter" size="small" @change="load">
            <el-radio-button value="">全部</el-radio-button>
            <el-radio-button value="pending">待审核</el-radio-button>
            <el-radio-button value="approved">已通过</el-radio-button>
            <el-radio-button value="rejected">已驳回</el-radio-button>
          </el-radio-group>
        </div>
      </div>
    </div>

    <el-card v-loading="loading">
      <el-table :data="items" stripe>
        <el-table-column prop="username" label="用户" width="130" />
        <el-table-column label="类型" width="90">
          <template #default="{ row }">
            <el-tag :type="row.type === 'enterprise' ? 'primary' : 'info'" size="small">{{ row.type === 'enterprise' ? '企业' : '个人' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="realName" label="姓名/联系人" width="140" />
        <el-table-column prop="idNumber" label="证件号/信用代码" min-width="180" show-overflow-tooltip />
        <el-table-column prop="enterpriseName" label="企业名称" min-width="160" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="statusTag[row.status] || 'info'" size="small">{{ statusLabel[row.status] || row.status }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="note" label="备注" min-width="120" show-overflow-tooltip />
        <el-table-column label="提交时间" width="170">
          <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" fixed="right">
          <template #default="{ row }">
            <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
          <template v-if="row.status === 'pending'">
          <el-button link type="success" size="small" @click="review(row, 'approved')">通过</el-button>
          <el-button link type="danger" size="small" @click="review(row, 'rejected')">驳回</el-button>
          </template>
          <span v-else class="text-muted small">{{ row.reviewedAt ? fmtTime(row.reviewedAt) : '' }}</span>
            </div>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && !items.length" description="暂无认证申请" />
    </el-card>
  </div>
</template>
