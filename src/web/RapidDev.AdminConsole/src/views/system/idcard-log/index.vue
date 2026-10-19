<script setup lang="ts">
/**
 * 身份证验证日志 -- 创蓝二要素验证记录。身份证号默认脱敏,有 export 权限可开"显示原始"查看完整信息。
 */
import { TTable, TCard, TSearchForm, TButton } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { idCardApi } from '@/api/system'
import { useUserStore } from '@/store/user'

const router = useRouter()
const userStore = useUserStore()
const canExport = userStore.hasPermission('system:idcard:export')

const loading = ref(false)
const data = ref<any[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(10)
const currentFilters = ref<any[]>([])
// 显示原始身份证号(默认脱敏)。仅 export 权限可见此开关
const showRaw = ref(false)

const columns: TableColumn[] = [
  { prop: 'name', label: '姓名', width: 100 },
  { prop: 'idCard', label: '身份证号', width: 190, slot: 'idCard' },
  { prop: 'userName', label: '关联用户', width: 120, slot: 'userName' },
  { prop: 'result', label: '结果', width: 100, slot: 'result', align: 'center' },
  { prop: 'remark', label: '备注', width: 100 },
  { prop: 'province', label: '省市区', minWidth: 160, slot: 'region' },
  { prop: 'gender', label: '性别', width: 70, slot: 'gender', align: 'center' },
  { prop: 'age', label: '年龄', width: 70, align: 'center' },
  { prop: 'orderNo', label: '订单号', width: 180 },
  { prop: 'costMs', label: '耗时(ms)', width: 100, align: 'right' },
  { prop: 'clientIp', label: 'IP', width: 130 },
  { prop: 'createdAt', label: '验证时间', minWidth: 160 },
]

const searchSchemas: FormItemSchema[] = [
  { prop: 'name', field: 'Name', label: '姓名', type: 'input', placeholder: '姓名(模糊)' },
  { prop: 'idCard', field: 'IdCard', label: '身份证号', type: 'input', placeholder: '身份证号(模糊)' },
  { prop: 'result', field: 'Result', label: '结果', type: 'select', placeholder: '验证结果', options: [
    { label: '一致', value: '01' }, { label: '不一致', value: '02' }, { label: '不确定', value: '03' }, { label: '失败', value: '04' },
  ] },
  { prop: 'createdAt', field: 'CreatedAt', label: '验证时间', type: 'datetime', props: { op: 'between' } },
]

async function load() {
  loading.value = true
  try {
    const res: any = await idCardApi.logList({ pageIndex: pageIndex.value, pageSize: pageSize.value, filters: currentFilters.value })
    data.value = res.list || []
    total.value = res.total || 0
  } finally { loading.value = false }
}

function onSearch(q: Record<string, any>) {
  if (q.filters && Array.isArray(q.filters)) { currentFilters.value = q.filters }
  else {
    currentFilters.value = []
    if (q.name) currentFilters.value.push({ field: 'Name', op: 'like', value: q.name })
    if (q.idCard) currentFilters.value.push({ field: 'IdCard', op: 'like', value: q.idCard })
    if (q.result) currentFilters.value.push({ field: 'Result', op: 'eq', value: q.result })
  }
  pageIndex.value = 1
  load()
}
function onReset() { currentFilters.value = []; pageIndex.value = 1; load() }

// 导出(管局/审计)。includeIdCard=true 导出完整身份证号
const exportLoading = ref(false)
const includeIdCard = ref(false)
async function exportLog() {
  exportLoading.value = true
  try {
    const blob: any = await idCardApi.logExport({ filters: currentFilters.value, includeIdCard: includeIdCard.value })
    const url = window.URL.createObjectURL(new Blob([blob]))
    const a = document.createElement('a')
    a.href = url
    a.download = `身份证验证日志_${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '')}.csv`
    a.click()
    window.URL.revokeObjectURL(url)
  } finally { exportLoading.value = false }
}

function maskIdCard(v?: string) {
  if (!v || v.length < 11) return v || '-'
  return v.slice(0, 3) + '***********' + v.slice(-4)
}
function gotoUser(userId?: number) {
  if (!userId) return
  router.push(`/system/user/${userId}`)
}
function resultText(r?: string) {
  return { '01': '一致', '02': '不一致', '03': '不确定', '04': '失败' }[r || ''] || '-'
}
function resultClass(r?: string) {
  return { '01': 'bg-success-lt', '02': 'bg-danger-lt', '03': 'bg-warning-lt', '04': 'bg-secondary-lt' }[r || ''] || 'bg-secondary-lt'
}

load()
</script>

<template>
  <TCard title="身份证验证日志">
    <template #actions>
      <div class="d-flex align-items-center gap-2">
        <label v-if="canExport" class="form-check form-switch m-0 d-flex align-items-center">
          <input class="form-check-input me-2" type="checkbox" v-model="showRaw" />
          <span class="small text-muted">显示原始信息</span>
        </label>
        <label class="form-check form-switch m-0 d-flex align-items-center">
          <input class="form-check-input me-2" type="checkbox" v-model="includeIdCard" />
          <span class="small text-muted">导出含完整身份证号</span>
        </label>
        <TButton v-perm="'system:idcard:export'" size="sm" variant="outline-primary" :loading="exportLoading" @click="exportLog">
          <i class="ti ti-download me-1"></i>导出
        </TButton>
      </div>
    </template>
    <TSearchForm :schemas="searchSchemas" :advanced="true" @search="onSearch" @reset="onReset" />
    <div class="table-responsive">
      <TTable :columns="columns" :data="data" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load">
        <template #body-idCard="{ row }">
          <span :class="{ 'text-danger': showRaw }" :title="showRaw ? '正在显示完整身份证号' : ''">
            {{ showRaw ? (row.idCard || '-') : maskIdCard(row.idCard) }}
          </span>
        </template>
        <template #body-userName="{ row }">
          <a v-if="row.userId" href="javascript:;" class="badge bg-azure-lt text-decoration-none" @click="gotoUser(row.userId)">
            <i class="ti ti-user me-1"></i>{{ row.userName || `#${row.userId}` }}
          </a>
          <span v-else class="text-muted">-</span>
        </template>
        <template #body-result="{ row }"><span class="badge" :class="resultClass(row.result)">{{ resultText(row.result) }}</span></template>
        <template #body-region="{ row }">{{ [row.province, row.city, row.country].filter(Boolean).join(' ') || '-' }}</template>
        <template #body-gender="{ row }">{{ row.gender === '1' ? '男' : row.gender === '2' ? '女' : '-' }}</template>
      </TTable>
    </div>
  </TCard>
</template>
