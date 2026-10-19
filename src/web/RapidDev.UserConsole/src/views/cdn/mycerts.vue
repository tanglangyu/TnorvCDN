<script setup lang="ts">
/** 证书管理（用户端）—— U1 卡片化：来源徽标 + 到期环形进度 + 剩余天数 + 临期提醒条 + 一键续期（auto 站点） */
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const renewing = ref(0)

// 手动上传证书（PEM 文本粘贴或文件导入 → POST /user/certs/upload）
const uploadDlg = ref(false)
const uploading = ref(false)
const uploadForm = ref({ domains: '', certPem: '', keyPem: '' })
const readFile = (file: File, target: 'certPem' | 'keyPem') => {
  const reader = new FileReader()
  reader.onload = () => { uploadForm.value[target] = String(reader.result || '') }
  reader.readAsText(file)
  return false // 阻止 el-upload 自动上传
}
const submitUpload = async () => {
  if (!uploadForm.value.certPem.trim() || !uploadForm.value.keyPem.trim()) return ElMessage.warning('证书与私钥均必填')
  uploading.value = true
  try {
    const res: any = await cdnApi.post('/user/certs/upload', { certPem: uploadForm.value.certPem, keyPem: uploadForm.value.keyPem })
    if (res?.code === 200) {
      ElMessage.success(res.message || '证书已上传并下发')
      uploadDlg.value = false
      uploadForm.value = { domains: '', certPem: '', keyPem: '' }
      gate.value?.refresh?.()
    } else ElMessage.warning(res?.message || '上传失败')
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.message || '上传失败')
  } finally { uploading.value = false }
}

const loadData = async () => {
  const r = (await cdnApi.get('/user/certs')) as any
  const d = r?.data ?? r
  const items = Array.isArray(d) ? d : (d?.items ?? [])
  return items
}

// 临期证书（14 天内）
const expiring = computed(() =>
  ((gate.value?.data as any[]) || []).filter((c) => {
    const days = remainDays(c.expiresAt)
    return days >= 0 && days < 14
  })
)

function remainDays(t?: string) {
  if (!t) return -1
  return Math.ceil((new Date(t).getTime() - Date.now()) / 86400000)
}
const fmt = (t: string) => (t ? new Date(t).toLocaleDateString() : '-')

// 到期环形进度（剩余天数 / 90 天为一个签发周期上限）
const ringPct = (c: any) => Math.max(0, Math.min(100, Math.round((remainDays(c.expiresAt) / 90) * 100)))
const ringColor = (c: any) => {
  const d = remainDays(c.expiresAt)
  if (d < 0) return '#F56C6C'
  if (d < 7) return '#F56C6C'
  if (d < 14) return '#E6A23C'
  return '#67C23A'
}

async function renew(row: any) {
  await ElMessageBox.confirm(`确认为站点「${row.domains}」发起证书续期？自动签发站点将重新走 ACME 流程。`, '续期证书', { type: 'info' })
  renewing.value = row.id
  try {
    // 找到该域名归属站点发起续期（后端 /user/servers/{id}/ssl/renew 归属校验）
    const servers = (await cdnApi.get('/user/servers')) as any
    const list = servers?.data ?? servers
    const arr = Array.isArray(list) ? list : (list?.items ?? [])
    const apex = String(row.domains || '').split(',')[0]
    const hit = arr.find((s: any) => (s.domains || []).some((d: string) => apex.includes(d.replace('*.', '')) || d === apex))
    if (!hit) return ElMessage.warning('未找到该证书对应的站点，请确认域名归属')
    await cdnApi.post(`/user/servers/${hit.id}/ssl/renew`)
    ElMessage.success('续期请求已提交，证书签发完成后自动更新')
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || e?.message || '续期失败')
  } finally { renewing.value = 0 }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">证书管理</h2><div class="text-muted">CDN加速 · 我的 HTTPS 证书</div></div>
          <div class="col-auto ms-auto"><el-button type="primary" size="small" @click="uploadDlg = true">上传证书</el-button></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ data }">
        <!-- 临期提醒条 -->
        <el-alert v-if="expiring.length" type="warning" show-icon :closable="false" class="mb-3">
          <template #title>
            有 {{ expiring.length }} 张证书将于 14 天内到期：
            <el-tag v-for="c in expiring" :key="c.id" size="small" class="ms-1" type="warning">
              {{ c.domains }}（剩 {{ remainDays(c.expiresAt) }} 天）
            </el-tag>
            <el-button v-if="expiring.some(c => c.source === 'acme')" size="small" type="warning" plain class="ms-2"
              @click="expiring.filter(c => c.source === 'acme').forEach(c => renew(c))">一键续期</el-button>
          </template>
        </el-alert>

        <!-- 证书卡片 -->
        <el-row v-if="(data || []).length" :gutter="16">
          <el-col v-for="c in data" :key="c.id" :xs="24" :sm="12" :lg="8" class="mb-3">
            <el-card shadow="hover">
              <div class="d-flex align-items-start">
                <el-progress type="dashboard" :percentage="ringPct(c)" :color="ringColor(c)" :width="86" :stroke-width="8">
                  <template #default>
                    <div class="text-center">
                      <div class="h3 mb-0" :style="{ color: ringColor(c) }">{{ remainDays(c.expiresAt) < 0 ? '已过期' : remainDays(c.expiresAt) }}</div>
                      <div class="text-muted" style="font-size: 11px">{{ remainDays(c.expiresAt) < 0 ? '' : '剩余天数' }}</div>
                    </div>
                  </template>
                </el-progress>
                <div class="flex-fill ms-3" style="min-width: 0">
                  <div class="font-weight-medium text-truncate" :title="c.domains">{{ c.domains }}</div>
                  <div class="mt-1">
                    <el-tag size="small" :type="c.source === 'acme' ? 'success' : 'info'">{{ c.sourceLabel || (c.source === 'acme' ? '自动签发' : '手动上传') }}</el-tag>
                  </div>
                  <div class="text-muted small mt-2">到期：{{ fmt(c.expiresAt) }}</div>
                  <div class="text-muted small">上传：{{ fmt(c.createdAt) }}</div>
                  <el-button v-if="c.source === 'acme'" size="small" type="primary" plain class="mt-2" :loading="renewing === c.id" @click="renew(c)">
                    续期
                  </el-button>
                  <div v-else class="text-muted small mt-2">手动证书请在源站更新后重新上传</div>
                </div>
              </div>
            </el-card>
          </el-col>
        </el-row>
        <el-card v-else>
          <el-empty description="暂无证书：接入站点后开启 HTTPS 即可自动签发" />
        </el-card>

        <el-card class="mt-1">
          <div class="text-muted small">
            「自动签发」证书由系统自动申请与续期，只需保持域名解析到节点即可；如需停用，请在「我的网站 → 站点配置」中关闭 HTTPS。
          </div>
        </el-card>
      </template>
    </CdnGate>

    <!-- 上传证书对话框 -->
    <el-dialog v-model="uploadDlg" title="上传证书（PEM）" width="560px">
      <el-form label-width="90px">
        <el-form-item label="证书文件">
          <el-upload :auto-upload="false" :show-file-list="false" accept=".pem,.crt,.cer" :on-change="(f: any) => readFile(f.raw, 'certPem')">
            <el-button size="small">选择 .pem 证书文件</el-button>
          </el-upload>
        </el-form-item>
        <el-form-item label="证书 PEM">
          <el-input v-model="uploadForm.certPem" type="textarea" :rows="5" placeholder="-----BEGIN CERTIFICATE----- ..." />
        </el-form-item>
        <el-form-item label="私钥文件">
          <el-upload :auto-upload="false" :show-file-list="false" accept=".pem,.key" :on-change="(f: any) => readFile(f.raw, 'keyPem')">
            <el-button size="small">选择 .key/.pem 私钥文件</el-button>
          </el-upload>
        </el-form-item>
        <el-form-item label="私钥 PEM">
          <el-input v-model="uploadForm.keyPem" type="textarea" :rows="4" placeholder="-----BEGIN PRIVATE KEY----- ..." />
        </el-form-item>
      </el-form>
      <div class="text-muted small mb-2">证书域名须属于你名下站点；同域名重复上传将替换旧证书。上传后自动下发到所有节点。</div>
      <template #footer>
        <el-button @click="uploadDlg = false">取消</el-button>
        <el-button type="primary" :loading="uploading" @click="submitUpload">上传并下发</el-button>
      </template>
    </el-dialog>
  </div>
</template>
