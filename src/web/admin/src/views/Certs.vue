<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>证书管理</span>
        <el-button type="primary" @click="openAdd"><el-icon><Plus /></el-icon>上传证书</el-button>
      </div>
    </template>
    <el-alert type="info" :closable="false" style="margin-bottom:12px"
      title="HTTPS 网站保存后，节点自动通过 ACME (Let's Encrypt) 签发并续期证书；也可在此上传自有证书。" />
    <el-table :data="certs" v-loading="loading">
      <el-table-column prop="domains" label="域名" min-width="200" />
      <el-table-column label="类型" width="120">
        <template #default="{ row }"><el-tag :type="row.type === 'acme' ? 'success' : 'info'" size="small">{{ row.type === 'acme' ? 'ACME 自动' : '手动上传' }}</el-tag></template>
      </el-table-column>
      <el-table-column label="到期时间" width="170">
        <template #default="{ row }">
          <span :style="expiring(row.expiresAt) ? 'color:var(--el-color-danger)' : ''">{{ fmtTime(row.expiresAt) }}</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="120">
        <template #default="{ row }">
          <el-popconfirm v-if="row.type !== 'acme'" title="确认删除？" @confirm="del(row)">            <template #reference><el-button size="small" type="danger">删除</el-button></template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialog" title="上传证书" width="640px">
      <el-form label-width="90px">
        <el-form-item label="证书(PEM)"><el-input type="textarea" :rows="6" v-model="form.cert" placeholder="-----BEGIN CERTIFICATE-----" /></el-form-item>
        <el-form-item label="私钥(PEM)"><el-input type="textarea" :rows="6" v-model="form.key" placeholder="-----BEGIN PRIVATE KEY-----" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import api from '../api'

const certs = ref([])
const loading = ref(false)
const dialog = ref(false)
const form = ref({ cert: '', key: '' })

const fmtTime = (t) => t ? new Date(t).toLocaleString() : '-'
const expiring = (t) => t && (new Date(t) - Date.now()) < 14 * 86400e3

// 手动证书从后端读取；ACME 证书从 HTTPS 网站推导展示
async function load() {
  loading.value = true
  try {
    const [servers, manualCerts] = await Promise.all([
      api.get('/admin/servers'), api.get('/admin/certificates'),
    ])
    const manual = manualCerts.map(c => ({
      id: 'c' + c.id, domains: c.domains, type: 'manual', expiresAt: c.expiresAt, _raw: c,
    }))
    const acmeCerts = servers
      .filter(s => (s.httpsPorts || []).length)
      .map(s => ({ id: 'a' + s.id, domains: (s.domains || []).join(', '), type: 'acme', expiresAt: null }))
    certs.value = [...manual, ...acmeCerts]
  } finally { loading.value = false }
}
onMounted(load)

function openAdd() { form.value = { cert: '', key: '' }; dialog.value = true }

async function save() {
  if (!form.value.cert || !form.value.key) return ElMessage.warning('请填写证书与私钥')
  try {
    await api.post('/admin/certificates', { clusterId: 1, certPem: form.value.cert, keyPem: form.value.key })
    ElMessage.success('证书已保存并下发到节点（≤10s 生效）')
    dialog.value = false
    load()
  } catch (e) { ElMessage.error(e.response?.data || '保存失败：证书/私钥格式错误') }
}

async function del(row) {
  if (row._raw) {
    await api.delete(`/admin/certificates/${row._raw.id}`)
    ElMessage.success('已删除')
    load()
  }
}
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
</style>
