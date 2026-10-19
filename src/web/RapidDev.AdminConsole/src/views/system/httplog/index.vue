<script setup lang="ts">
/**
 * HTTP 访问日志 —— 记录所有 HTTP 请求的完整数据(方法/路径/状态/耗时/真实IP/用户/请求体/响应体/Header)。
 * 支持按 路径/方法/状态码/IP/用户 筛选 + 导出 CSV + 清空 + 详情查看(请求/响应完整内容)。
 */
import { TTable, TCard, TButton, TSearchForm, TModal, TTag } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { ref } from 'vue'
import { logApi } from '@/api/system'
import { formatTime } from '@/utils/time'
import type { SysHttpLog } from '@/types'

const loading = ref(false)
const data = ref<any[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(10)
const currentFilters = ref<any[]>([])

const detail = ref<SysHttpLog | null>(null)
const detailVisible = ref(false)

const columns: TableColumn[] = [
  { prop: 'method', label: '方法', width: 80, slot: 'method' },
  { prop: 'path', label: '路径', minWidth: 200 },
  { prop: 'statusCode', label: '状态', width: 80, slot: 'status', align: 'center' },
  { prop: 'costTimeMs', label: '耗时(ms)', width: 100, align: 'right' },
  { prop: 'clientIp', label: '客户端IP', width: 140 },
  { prop: 'userName', label: '用户', width: 110 },
  { prop: 'requestTime', label: '时间', minWidth: 160 },
]

const searchSchemas: FormItemSchema[] = [
  { prop: 'path', field: 'Path', label: '路径', type: 'input', placeholder: '路径(模糊)' },
  { prop: 'method', field: 'Method', label: '方法', type: 'select', placeholder: '方法', options: [{ label: 'GET', value: 'GET' }, { label: 'POST', value: 'POST' }, { label: 'PUT', value: 'PUT' }, { label: 'DELETE', value: 'DELETE' }] },
  { prop: 'clientIp', field: 'ClientIp', label: 'IP', type: 'input', placeholder: 'IP(模糊)' },
  { prop: 'userName', field: 'UserName', label: '用户', type: 'input', placeholder: '用户名(模糊)' },
  { prop: 'statusCode', field: 'StatusCode', label: '状态码', type: 'input', props: { op: 'between' }, placeholder: '如 400 ~ 599' },
  { prop: 'requestTime', field: 'RequestTime', label: '请求时间', type: 'datetime', props: { op: 'between' } },
]

async function load() {
  loading.value = true
  try {
    const res: any = await logApi.httpList({ pageIndex: pageIndex.value, pageSize: pageSize.value, filters: currentFilters.value })
    data.value = res.list || []
    total.value = res.total || 0
  } finally { loading.value = false }
}

function onSearch(q: Record<string, any>) {
  if (q.filters && Array.isArray(q.filters)) { currentFilters.value = q.filters }
  else {
    currentFilters.value = []
    if (q.path) currentFilters.value.push({ field: 'Path', op: 'like', value: q.path })
    if (q.method) currentFilters.value.push({ field: 'Method', op: 'eq', value: q.method })
    if (q.clientIp) currentFilters.value.push({ field: 'ClientIp', op: 'like', value: q.clientIp })
    if (q.userName) currentFilters.value.push({ field: 'UserName', op: 'like', value: q.userName })
  }
  pageIndex.value = 1
  load()
}
function onReset() { currentFilters.value = []; pageIndex.value = 1; load() }

function viewDetail(row: any) { detail.value = row as SysHttpLog; detailVisible.value = true }

async function exportCsv() {
  try {
    const blob: any = await logApi.exportHttp({ filters: currentFilters.value })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a'); a.href = url; a.download = 'http日志.csv'; a.click()
    URL.revokeObjectURL(url)
  } catch {}
}
async function clean() {
  if (!confirm('确定清空全部 HTTP 访问日志?此操作不可恢复!')) return
  try { await logApi.cleanHttp(); load() } catch {}
}

function methodVariant(m: string): any {
  return { GET: 'ghost-info', POST: 'ghost-success', PUT: 'ghost-warning', DELETE: 'ghost-danger' }[m] || 'secondary'
}
function statusClass(s: number): string {
  if (s >= 500) return 'bg-danger-lt'
  if (s >= 400) return 'bg-warning-lt'
  if (s >= 300) return 'bg-info-lt'
  return 'bg-success-lt'
}
function prettyJson(s?: string): string {
  if (!s) return ''
  try { return JSON.stringify(JSON.parse(s), null, 2) } catch { return s }
}

load()
</script>

<template>
  <TCard title="HTTP 访问日志">
    <template #actions>
      <TButton variant="ghost-secondary" size="sm" @click="exportCsv"><i class="ti ti-download me-1"></i>导出</TButton>
      <TButton variant="ghost-danger" size="sm" @click="clean"><i class="ti ti-trash me-1"></i>清空</TButton>
    </template>
    <TSearchForm :schemas="searchSchemas" :advanced="true" @search="onSearch" @reset="onReset" />
    <div class="table-responsive">
      <TTable :columns="columns" :data="data" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load">
        <template #body-method="{ row }"><TTag :variant="methodVariant(row.method)">{{ row.method }}</TTag></template>
        <template #body-status="{ row }"><span class="badge" :class="statusClass(row.statusCode)">{{ row.statusCode }}</span></template>
        <template #action="{ row }">
          <button class="btn btn-ghost-primary btn-sm" @click="viewDetail(row)"><i class="ti ti-eye"></i>详情</button>
        </template>
      </TTable>
    </div>
  </TCard>

  <TModal v-model="detailVisible" title="请求详情" size="xl">
    <template v-if="detail">
      <div class="mb-3">
        <TTag :variant="methodVariant(detail.method)">{{ detail.method }}</TTag>
        <span class="ms-2" style="word-break: break-all">{{ detail.path }}{{ detail.queryString || '' }}</span>
      </div>
      <table class="table table-sm table-vcenter mb-3">
        <tbody>
          <tr><td class="text-muted" style="width:120px">状态码</td><td><span class="badge" :class="statusClass(detail.statusCode)">{{ detail.statusCode }}</span></td></tr>
          <tr><td class="text-muted">耗时</td><td>{{ detail.costTimeMs }} ms</td></tr>
          <tr><td class="text-muted">客户端IP</td><td>{{ detail.clientIp }} <span class="text-muted">({{ detail.clientIps }})</span></td></tr>
          <tr><td class="text-muted">用户</td><td>{{ detail.userName || '-' }} ({{ detail.userId || '-' }})</td></tr>
          <tr><td class="text-muted">User-Agent</td><td style="word-break: break-all">{{ detail.userAgent }}</td></tr>
          <tr><td class="text-muted">TraceId</td><td>{{ detail.traceId }}</td></tr>
          <tr><td class="text-muted">时间</td><td>{{ formatTime(detail.requestTime) }}</td></tr>
          <tr v-if="detail.exception"><td class="text-muted">异常</td><td class="text-danger" style="word-break: break-all">{{ detail.exception }}</td></tr>
        </tbody>
      </table>

      <h6 class="mb-2">请求头</h6>
      <pre class="code-block p-2 rounded mb-3">{{ prettyJson(detail.requestHeaders) }}</pre>

      <h6 class="mb-2">请求体</h6>
      <pre class="code-block p-2 rounded mb-3">{{ prettyJson(detail.requestBody) }}</pre>

      <h6 class="mb-2">响应头</h6>
      <pre class="code-block p-2 rounded mb-3">{{ prettyJson(detail.responseHeaders) }}</pre>

      <h6 class="mb-2">响应体</h6>
      <pre class="code-block p-2 rounded">{{ prettyJson(detail.responseBody) }}</pre>
    </template>
  </TModal>
</template>

<style scoped>
/* 代码块:固定深色背景+浅色文字,与系统明暗主题解耦,确保任何主题下都清晰可读 */
.code-block {
  background-color: #1f2937;
  color: #e5e7eb;
  max-height: 250px;
  overflow: auto;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-all;
  margin: 0;
}
</style>
