<script setup lang="ts">
/**
 * 操作日志 —— 支持按模块/操作人/状态/时间范围筛选 + 导出 CSV + 清空。
 * 后端 /api/system/log/oper/list 已自动支持高级查询(filters/sorts)。
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

const columns: TableColumn[] = [
  { prop: 'module', label: '模块', width: 120 },
  { prop: 'title', label: '操作描述', minWidth: 150 },
  { prop: 'requestMethod', label: '方法', width: 80 },
  { prop: 'operUserName', label: '操作人', width: 120 },
  { prop: 'operIp', label: 'IP', width: 130 },
  { prop: 'status', label: '状态', width: 80, slot: 'status', align: 'center' },
  { prop: 'costTime', label: '耗时(ms)', width: 100, align: 'right' },
  { prop: 'operTime', label: '操作时间', minWidth: 160 },
]

const searchSchemas: FormItemSchema[] = [
  { prop: 'module', field: 'Module', label: '模块', type: 'input', placeholder: '模块名(模糊)' },
  { prop: 'operUserName', field: 'OperUserName', label: '操作人', type: 'input', placeholder: '操作人(模糊)' },
  { prop: 'status', field: 'Status', label: '状态', type: 'select', placeholder: '状态', options: [
    { label: '成功', value: 0 }, { label: '失败', value: 1 },
  ] },
  { prop: 'operTime', field: 'OperTime', label: '操作时间', type: 'datetime', props: { op: 'between' } },
]

async function load() {
  loading.value = true
  try {
    const res: any = await logApi.operList({ pageIndex: pageIndex.value, pageSize: pageSize.value, filters: currentFilters.value })
    data.value = res.list || []
    total.value = res.total || 0
  } finally { loading.value = false }
}

function onSearch(q: Record<string, any>) {
  // 把搜索字段转成 filters(后端 ApplyFilters 支持 like)
  if (q.filters && Array.isArray(q.filters)) { currentFilters.value = q.filters }
  else {
    currentFilters.value = []
    if (q.module) currentFilters.value.push({ field: 'Module', op: 'like', value: q.module })
    if (q.operUserName) currentFilters.value.push({ field: 'OperUserName', op: 'like', value: q.operUserName })
  }
  pageIndex.value = 1
  load()
}
function onReset() { currentFilters.value = []; pageIndex.value = 1; load() }

async function exportCsv() {
  try {
    const blob: any = await logApi.exportOper({ filters: currentFilters.value })
    downloadBlob(blob, '操作日志.csv')
  } catch {}
}

async function clean() {
  if (!confirm('确定清空全部操作日志?此操作不可恢复!')) return
  try { await logApi.cleanOper(); load() } catch {}
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url; a.download = filename; a.click()
  URL.revokeObjectURL(url)
}

load()
</script>

<template>
  <TCard title="操作日志">
    <template #actions>
      <TButton variant="ghost-secondary" size="sm" @click="exportCsv"><i class="ti ti-download me-1"></i>导出</TButton>
      <TButton variant="ghost-danger" size="sm" @click="clean"><i class="ti ti-trash me-1"></i>清空</TButton>
    </template>
    <TSearchForm :schemas="searchSchemas" :advanced="true" @search="onSearch" @reset="onReset" />
    <div class="table-responsive">
      <TTable :columns="columns" :data="data" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load">
        <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '成功' : '失败' }}</span></template>
      </TTable>
    </div>
  </TCard>
</template>
