<script setup lang="ts">
import { ElMessage } from 'element-plus'
/**
 * 文件管理 —— 上传、列表、生成签名链接(按次数/按时间)、删除。
 * 签名链接可复制,匿名访问(签名即凭证)。
 */
import { ref, onMounted } from 'vue'
import { fileApi } from '@/api/file'
import { TCard, TButton, TTable, TModal, TForm, TTag } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'

const loading = ref(false)
const data = ref<any[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(10)
const uploadVisible = ref(false)
const sigVisible = ref(false)
const uploading = ref(false)
const currentFileId = ref(0)
const sigResult = ref('')
const sigForm = ref({ mode: 'time', expirySeconds: 3600, maxCount: 1 })
const fileInput = ref<HTMLInputElement | null>(null)
const uploadOpts = ref({ signatureEnabled: true, maxAccessCount: 0 })

const columns: TableColumn[] = [
  { prop: 'originalName', label: '文件名', minWidth: 200 },
  { prop: 'contentType', label: '类型', width: 140 },
  { prop: 'fileSize', label: '大小', width: 100, align: 'right' },
  { prop: 'accessCount', label: '访问次数', width: 90, align: 'center' },
  { prop: 'signatureEnabled', label: '签名', width: 80, slot: 'sig', align: 'center' },
  { prop: 'status', label: '状态', width: 80, slot: 'status', align: 'center' },
  { prop: 'createdAt', label: '上传时间', minWidth: 160 },
]

const sigSchemas: FormItemSchema[] = [
  { prop: 'mode', label: '签名方式', type: 'select', required: true, span: 12, options: [
    { label: '限时(time)', value: 'time' },
    { label: '限次(count)', value: 'count' },
    { label: '组合(time,count)', value: 'time,count' },
  ] },
  { prop: 'expirySeconds', label: '有效秒数', type: 'number', span: 6, defaultValue: 3600 },
  { prop: 'maxCount', label: '最大次数', type: 'number', span: 6, defaultValue: 1 },
]

async function load() {
  loading.value = true
  try {
    const res: any = await fileApi.list({ pageIndex: pageIndex.value, pageSize: pageSize.value })
    data.value = res.list || []
    total.value = res.total || 0
  } finally { loading.value = false }
}

function openUpload() { uploadVisible.value = true }
function triggerFile() { fileInput.value?.click() }

async function doUpload(e: Event) {
  const input = e.target as HTMLInputElement
  if (!input.files || input.files.length === 0) return
  uploading.value = true
  try {
    const fd = new FormData()
    fd.append('file', input.files[0])
    fd.append('signatureEnabled', String(uploadOpts.value.signatureEnabled))
    fd.append('maxAccessCount', String(uploadOpts.value.maxAccessCount))
    await fileApi.upload(fd)
    uploadVisible.value = false
    input.value = ''
    load()
  } catch {} finally { uploading.value = false }
}

async function genSignature(row: any) {
  currentFileId.value = row.id
  sigForm.value = { mode: 'time', expirySeconds: 3600, maxCount: 1 }
  sigResult.value = ''
  sigVisible.value = true
}

async function doGenSignature() {
  try {
    const res: any = await fileApi.signature(currentFileId.value, sigForm.value)
    sigResult.value = res.url
  } catch {}
}

async function remove(row: any) {
  if (!confirm(`确定删除文件「${row.originalName}」?`)) return
  try { await fileApi.remove(row.id); load() } catch {}
}

function copyUrl() {
  if (!sigResult.value) return
  navigator.clipboard.writeText(sigResult.value).then(() => ElMessage.error('已复制到剪贴板'))
}

function fmtSize(b: number): string {
  if (b < 1024) return b + ' B'
  if (b < 1048576) return (b / 1024).toFixed(1) + ' KB'
  return (b / 1048576).toFixed(1) + ' MB'
}

onMounted(load)
</script>

<template>
  <TCard title="文件管理">
    <template #actions>
      <TButton variant="primary" size="sm" @click="openUpload"><i class="ti ti-upload me-1"></i>上传文件</TButton>
      <TButton variant="ghost-secondary" size="sm" @click="load"><i class="ti ti-refresh"></i></TButton>
    </template>
    <div class="table-responsive">
      <TTable :columns="columns" :data="data" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load">
        <template #body-fileSize="{ row }">{{ fmtSize(row.fileSize) }}</template>
        <template #body-sig="{ row }"><TTag :variant="row.signatureEnabled ? 'success' : 'secondary'">{{ row.signatureEnabled ? '是' : '否' }}</TTag></template>
        <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '正常' : '禁用' }}</span></template>
        <template #action="{ row }">
          <button class="btn btn-ghost-primary btn-sm" @click="genSignature(row)"><i class="ti ti-link"></i>签名链接</button>
          <button class="btn btn-ghost-danger btn-sm" @click="remove(row)"><i class="ti ti-trash"></i>删除</button>
        </template>
      </TTable>
    </div>
  </TCard>

  <TModal v-model="uploadVisible" title="上传文件">
    <div class="mb-3">
      <label class="form-label">选择文件</label>
      <input ref="fileInput" type="file" class="form-control" @change="doUpload" />
      <div class="form-hint text-muted">支持:图片/PDF/Office/压缩包等,最大 100MB</div>
    </div>
    <div class="form-check form-switch mb-2">
      <input class="form-check-input" type="checkbox" id="sigEnabled" v-model="uploadOpts.signatureEnabled" />
      <label class="form-check-label" for="sigEnabled">启用签名访问(下载需签名链接)</label>
    </div>
    <div class="mb-2">
      <label class="form-label">最大访问次数(0=不限,仅 count 签名时生效)</label>
      <input type="number" class="form-control" v-model.number="uploadOpts.maxAccessCount" />
    </div>
    <template #footer>
      <TButton variant="ghost-secondary" @click="uploadVisible = false">取消</TButton>
      <TButton variant="primary" :loading="uploading" @click="triggerFile">选择并上传</TButton>
    </template>
  </TModal>

  <TModal v-model="sigVisible" title="生成签名访问链接" size="lg">
    <TForm v-model="sigForm" :schemas="sigSchemas" />
    <template #footer>
      <TButton variant="ghost-secondary" @click="sigVisible = false">关闭</TButton>
      <TButton variant="primary" @click="doGenSignature">生成</TButton>
    </template>
    <div v-if="sigResult" class="mt-3">
      <label class="form-label">签名链接(可复制,匿名访问)</label>
      <div class="input-group">
        <input type="text" class="form-control" :value="sigResult" readonly />
        <button class="btn btn-primary" @click="copyUrl"><i class="ti ti-copy"></i>复制</button>
      </div>
      <div class="form-hint text-muted mt-1">直接在浏览器打开即可下载文件;次数/时间签名失效后返回 403。</div>
    </div>
  </TModal>
</template>
