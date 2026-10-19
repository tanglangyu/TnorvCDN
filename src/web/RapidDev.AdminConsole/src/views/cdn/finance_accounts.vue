<script setup lang="ts">
/** 用户账户（A2）—— 用户余额列表 + 余额调整弹窗（原因必填，写审计） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const pager = ref({ page: 1, pageSize: 50, total: 0 })
const filterQ = ref('')
const adjDialog = ref(false)
const adjForm = ref({ userId: 0, username: '', delta: 0, reason: '' })
const saving = ref(false)

const loadData = async () => {
  const r = (await cdnApi.get('/admin/users', { page: pager.value.page, pageSize: pager.value.pageSize, q: filterQ.value || undefined })) as any
  const d = r?.data ?? r
  if (d && !Array.isArray(d) && 'total' in d) {
    pager.value.total = d.total
    rows.value = d.items || []
  } else {
    rows.value = Array.isArray(d) ? d : []
    pager.value.total = rows.value.length
  }
  return rows.value
}

const money = (cents: any) => `¥${(Number(cents || 0) / 100).toFixed(2)}`
const onSearch = () => { pager.value.page = 1; gate.value?.refresh?.() }

function openAdjust(row: any) {
  adjForm.value = { userId: row.id, username: row.username, delta: 0, reason: '' }
  adjDialog.value = true
}

async function saveAdjust() {
  const f = adjForm.value
  const deltaCents = Math.round(Number(f.delta) * 100)
  if (!deltaCents) return ElMessage.warning('调整额度不能为 0')
  if (!f.reason || f.reason.trim().length < 2) return ElMessage.warning('调整原因必填（至少 2 字）')
  await ElMessageBox.confirm(`确认为用户 ${f.username} ${deltaCents > 0 ? '增加' : '扣减'} ¥${Math.abs(deltaCents / 100).toFixed(2)}？将写入审计日志。`, '余额调整', { type: 'warning' })
  saving.value = true
  try {
    await cdnApi.post('/admin/finance/adjust-balance', { userId: f.userId, deltaCents, reason: f.reason.trim() })
    ElMessage.success('余额已调整')
    adjDialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.message || '调整失败')
  } finally { saving.value = false }
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">用户账户</h2><div class="text-muted">财务管理 · 用户余额列表与余额调整（写审计）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center">
            <el-input v-model="filterQ" placeholder="用户名关键词" size="small" clearable style="width: 200px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column prop="id" label="ID" width="70" />
            <el-table-column prop="username" label="账号" min-width="140" />
            <el-table-column label="角色" width="90">
              <template #default="{ row }"><el-tag size="small" :type="row.isAdmin ? 'danger' : 'info'">{{ row.isAdmin ? '管理员' : '用户' }}</el-tag></template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }"><el-tag size="small" :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag></template>
            </el-table-column>
            <el-table-column label="余额" width="120">
              <template #default="{ row }"><b :class="Number(row.balanceCents) < 0 ? 'text-danger' : ''">{{ money(row.balanceCents) }}</b></template>
            </el-table-column>
            <el-table-column label="创建时间" width="170">
              <template #default="{ row }">{{ row.createdAt ? new Date(row.createdAt).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column label="操作" width="120" fixed="right">
              <template #default="{ row }">
                <el-button size="small" type="warning" plain @click="openAdjust(row)">余额调整</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }" />
          <el-empty v-if="!rows.length" description="暂无用户" />
        </el-card>

        <el-dialog v-model="adjDialog" :title="`余额调整：${adjForm.username}`" width="440px">
          <el-form label-width="90px">
            <el-form-item label="调整额度">
              <el-input-number v-model="adjForm.delta" :step="10" :precision="2" controls-position="right" style="width: 180px" />
              <div class="text-muted small mt-1">正数为增加，负数为扣减（元）</div>
            </el-form-item>
            <el-form-item label="调整原因" required>
              <el-input v-model="adjForm.reason" type="textarea" :rows="2" placeholder="必填，将写入审计日志（如：活动赠送 / 误充退回）" />
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="adjDialog = false">取消</el-button>
            <el-button type="primary" :loading="saving" @click="saveAdjust">确认调整</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
