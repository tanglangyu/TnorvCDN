<script setup lang="ts">
/** 证书管理 —— 对接 TnorvCdn 主控 /api/admin/certificates */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const certs = ref<any[]>([])
const dialog = ref(false)
const saving = ref(false)
const form = ref<any>({})

const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const data = await cdnApi.get('/admin/certificates', {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    q: filterQ.value || undefined,
  }) as any
  certs.value = Array.isArray(data) ? data : (data?.items || [])
  pager.value.total = Array.isArray(data) ? certs.value.length : (data?.total ?? certs.value.length)
  return certs.value
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

// A7：到期进度条（90 天为一个签发周期）+ 续期
function remainDays(t?: string) {
  if (!t) return -1
  return Math.ceil((new Date(t).getTime() - Date.now()) / 86400000)
}
const ringPct = (t?: string) => Math.max(0, Math.min(100, Math.round((remainDays(t) / 90) * 100)))
const ringColor = (t?: string) => {
  const d = remainDays(t)
  if (d < 0) return '#F56C6C'
  if (d < 7) return '#F56C6C'
  if (d < 14) return '#E6A23C'
  return '#67C23A'
}
const renewing = ref(0)
async function renew(row: any) {
  renewing.value = row.id
  try {
    await cdnApi.post(`/admin/certificates/${row.id}/renew`)
    ElMessage.success('续期请求已提交')
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || '续期失败')
  } finally { renewing.value = 0 }
}

function openAdd() {
  form.value = { domains: '', certPem: '', keyPem: '' }
  dialog.value = true
}

async function upload() {
  if (!form.value.domains || !form.value.certPem || !form.value.keyPem) return ElMessage.warning('请完整填写域名、证书和私钥')
  saving.value = true
  try {
    await cdnApi.post('/admin/certificates', form.value)
    ElMessage.success('证书已上传并下发节点')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || e?.message || '上传失败')
  } finally {
    saving.value = false
  }
}

async function del(row: any) {
  await cdnApi.delete(`/admin/certificates/${row.id}`)
  ElMessage.success('已删除')
  gate.value?.refresh?.()
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">证书管理</h2><div class="text-muted">网站列表 · 手动上传证书（PEM），自动下发边缘节点</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openAdd()">+ 上传证书</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="域名关键词" size="small" clearable style="width: 220px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
          </div>
          <el-table :data="certs" stripe>
            <el-table-column prop="domains" label="域名" min-width="200" />
            <el-table-column label="来源" width="100">
              <template #default="{ row }">
                <el-tag size="small" :type="row.source === 'acme' ? 'success' : 'info'">{{ row.source === 'acme' ? '自动签发' : '手动上传' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="到期" width="150">
              <template #default="{ row }">
                <el-progress type="dashboard" :percentage="ringPct(row.expiresAt)" :color="ringColor(row.expiresAt)" :width="56" :stroke-width="5">
                  <template #default>
                    <span style="font-size: 12px" :style="{ color: ringColor(row.expiresAt) }">{{ remainDays(row.expiresAt) < 0 ? '过期' : remainDays(row.expiresAt) + '天' }}</span>
                  </template>
                </el-progress>
              </template>
            </el-table-column>
            <el-table-column prop="expiresAt" label="到期时间" width="170">
              <template #default="{ row }">{{ row.expiresAt ? new Date(row.expiresAt).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column prop="createdAt" label="上传时间" width="170">
              <template #default="{ row }">{{ row.createdAt ? new Date(row.createdAt).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
                  <el-button v-if="row.source === 'acme'" size="small" type="primary" plain :loading="renewing === row.id" @click="renew(row)">续期</el-button>
                  <el-button size="small" type="danger" @click="del(row)">删除</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
        </el-card>

        <el-dialog v-model="dialog" title="上传证书" width="640px">
          <el-form label-width="100px">
            <el-form-item label="域名"><el-input v-model="form.domains" placeholder="example.com,www.example.com" /></el-form-item>
            <el-form-item label="证书 PEM"><el-input v-model="form.certPem" type="textarea" :rows="6" placeholder="-----BEGIN CERTIFICATE-----" /></el-form-item>
            <el-form-item label="私钥 PEM"><el-input v-model="form.keyPem" type="textarea" :rows="6" placeholder="-----BEGIN PRIVATE KEY-----" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" :loading="saving" @click="upload">上传</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
