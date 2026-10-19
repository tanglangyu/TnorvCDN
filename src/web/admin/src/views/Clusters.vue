<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>集群管理</span>
        <el-button type="primary" @click="openEdit(null)"><el-icon><Plus /></el-icon>创建集群</el-button>
      </div>
    </template>
    <el-table :data="clusters" v-loading="loading">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="name" label="名称" />
      <el-table-column label="DNS 子域名">
        <template #default="{ row }">{{ row.dnsDomain || '-' }}</template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'danger'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag></template>
      </el-table-column>
      <el-table-column label="创建时间" width="170">
        <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="180">
        <template #default="{ row }">
          <el-button size="small" @click="openEdit(row)">编辑</el-button>
          <el-popconfirm title="确认删除该集群？" @confirm="del(row)">
            <template #reference><el-button size="small" type="danger">删除</el-button></template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialog" :title="form.id ? '编辑集群' : '创建集群'" width="480px">
      <el-form label-width="110px">
        <el-form-item label="名称"><el-input v-model="form.name" placeholder="华东集群" /></el-form-item>
        <el-form-item label="DNS 子域名"><el-input v-model="form.dnsDomain" placeholder="cdn1.example.com（智能 DNS CNAME 目标）" /></el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
        <el-divider>DDoS 防护（nftables 内核层限速）</el-divider>
        <el-form-item label="启用防护"><el-switch v-model="form.ddos.enabled" /></el-form-item>
        <template v-if="form.ddos.enabled">
          <el-form-item label="并发连接数"><el-input-number v-model="form.ddos.connLimit" :min="10" :max="65535" /> 单IP最大并发连接</el-form-item>
          <el-form-item label="新建速率"><el-input-number v-model="form.ddos.connRate" :min="5" :max="10000" /> 连接/秒（突发 <el-input-number v-model="form.ddos.burstRate" :min="5" :max="65535" />）</el-form-item>
          <el-form-item label="SYN防护"><el-input-number v-model="form.ddos.synFloodLimit" :min="10" :max="100000" /> SYN/秒</el-form-item>
          <el-form-item label="生效端口"><el-input :model-value="(form.ddos.ports||[]).join(',')" @update:model-value="v => form.ddos.ports = v.split(',').map(x=>parseInt(x.trim())).filter(n=>n>0)" placeholder="80,443" /></el-form-item>
        </template>
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

const clusters = ref([])
const loading = ref(false)
const dialog = ref(false)
const form = ref({})

const fmtTime = (t) => t ? new Date(t).toLocaleString() : '-'

async function load() {
  loading.value = true
  try { clusters.value = await api.get('/admin/clusters') } finally { loading.value = false }
}
onMounted(load)

function openEdit(row) {
  const emptyDdos = () => ({ enabled: false, connLimit: 100, connRate: 50, burstRate: 100, synFloodLimit: 200, ports: [80, 443] })
  form.value = row ? Object.assign({ ddos: emptyDdos() }, JSON.parse(JSON.stringify(row))) : { name: '', dnsDomain: '', enabled: true, ddos: emptyDdos() }
  if (!form.value.ddos) form.value.ddos = emptyDdos()
  dialog.value = true
}

async function save() {
  if (!form.value.name) return ElMessage.warning('请填写名称')
  if (form.value.id) await api.put(`/admin/clusters/${form.value.id}`, form.value)
  else await api.post('/admin/clusters', form.value)
  ElMessage.success('已保存')
  dialog.value = false
  load()
}

async function del(row) {
  try {
    await api.delete(`/admin/clusters/${row.id}`)
    ElMessage.success('已删除')
    load()
  } catch (e) { ElMessage.error(e.response?.data || '删除失败') }
}
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
</style>
