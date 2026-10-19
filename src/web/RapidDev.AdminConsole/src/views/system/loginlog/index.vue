<script setup lang="ts">
/**
 * 登录日志 —— 记录每次登录(成功/失败)、IP、浏览器/OS、消息。支持筛选 + 导出 + 清空。
 */
import { TTable, TCard, TButton, TSearchForm } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { ref } from 'vue'
import { logApi } from '@/api/system'

const loading = ref(false)
const data = ref<any[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(10)
const currentFilters = ref<any[]>([])
const withLocation = ref(false)  // 开启时后端对当前页 IP 批量查 IP 库补全空 LoginLocation

const columns: TableColumn[] = [
  { prop: 'userName', label: '用户名', width: 120 },
  { prop: 'loginIp', label: 'IP', width: 130 },
  { prop: 'loginLocation', label: '登录地点', minWidth: 120 },
  { prop: 'browser', label: '浏览器', width: 120 },
  { prop: 'os', label: '操作系统', width: 120 },
  { prop: 'status', label: '状态', width: 80, slot: 'status', align: 'center' },
  { prop: 'message', label: '消息', minWidth: 150 },
  { prop: 'loginTime', label: '登录时间', minWidth: 160 },
]

const searchSchemas: FormItemSchema[] = [
  { prop: 'userName', field: 'UserName', label: '用户名', type: 'input', placeholder: '用户名(模糊)' },
  { prop: 'loginIp', field: 'LoginIp', label: 'IP', type: 'input', placeholder: 'IP(模糊)' },
  { prop: 'status', field: 'Status', label: '状态', type: 'select', placeholder: '状态', options: [
    { label: '成功', value: 0 }, { label: '失败', value: 1 },
  ] },
  { prop: 'loginTime', field: 'LoginTime', label: '登录时间', type: 'datetime', props: { op: 'between' } },
]

async function load() {
  loading.value = true
  try {
    const res: any = await logApi.loginList({ pageIndex: pageIndex.value, pageSize: pageSize.value, filters: currentFilters.value, withLocation: withLocation.value })
    data.value = res.list || []
    total.value = res.total || 0
  } finally { loading.value = false }
}

function onSearch(q: Record<string, any>) {
  if (q.filters && Array.isArray(q.filters)) { currentFilters.value = q.filters }
  else {
    currentFilters.value = []
    if (q.userName) currentFilters.value.push({ field: 'UserName', op: 'like', value: q.userName })
    if (q.loginIp) currentFilters.value.push({ field: 'LoginIp', op: 'like', value: q.loginIp })
  }
  pageIndex.value = 1
  load()
}
function onReset() { currentFilters.value = []; pageIndex.value = 1; load() }

async function exportCsv() {
  try {
    const blob: any = await logApi.exportLogin({ filters: currentFilters.value })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a'); a.href = url; a.download = '登录日志.csv'; a.click()
    URL.revokeObjectURL(url)
  } catch {}
}
async function clean() {
  if (!confirm('确定清空全部登录日志?此操作不可恢复!')) return
  try { await logApi.cleanLogin(); load() } catch {}
}

load()
</script>

<template>
  <TCard title="登录日志">
    <template #actions>
      <div class="d-flex align-items-center gap-2">
        <label class="form-check form-switch m-0 d-flex align-items-center">
          <input class="form-check-input me-2" type="checkbox" v-model="withLocation" @change="load" />
          <span class="small text-muted">补全归属地</span>
        </label>
        <TButton variant="ghost-secondary" size="sm" @click="exportCsv"><i class="ti ti-download me-1"></i>导出</TButton>
        <TButton variant="ghost-danger" size="sm" @click="clean"><i class="ti ti-trash me-1"></i>清空</TButton>
      </div>
    </template>
    <TSearchForm :schemas="searchSchemas" :advanced="true" @search="onSearch" @reset="onReset" />
    <div class="table-responsive">
      <TTable :columns="columns" :data="data" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load">
        <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '成功' : '失败' }}</span></template>
      </TTable>
    </div>
  </TCard>
</template>
