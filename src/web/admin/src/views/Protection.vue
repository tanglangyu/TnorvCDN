<template>
  <el-card shadow="never">
    <template #header>
      <div class="card-header">
        <span>高防 IP 管理</span>
        <el-button type="primary" size="small" @click="openEdit(null)">+ 新增高防实例</el-button>
      </div>
    </template>
    <el-table :data="instances" v-loading="loading" size="small">
      <el-table-column prop="name" label="实例名" />
      <el-table-column prop="ip" label="高防 IP" />
      <el-table-column prop="line" label="线路" width="90" />
      <el-table-column label="保底/峰值 (Mbps)" width="150">
        <template #default="{ row }">{{ row.protectedMbps }} / {{ row.peakMbps }}</template>
      </el-table-column>
      <el-table-column prop="protectedTargets" label="防护对象" show-overflow-tooltip />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.status === 'active' ? 'success' : 'danger'" size="small">{{ row.status === 'active' ? '生效中' : '已到期' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="到期时间" width="160">
        <template #default="{ row }">{{ fmtTime(row.expireAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="140">
        <template #default="{ row }">
          <el-button size="small" @click="openEdit(row)">编辑</el-button>
          <el-button size="small" type="danger" @click="del(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialog" :title="form.id ? '编辑高防实例' : '新增高防实例'" width="520px">
      <el-form label-width="120px">
        <el-form-item label="实例名"><el-input v-model="form.name" placeholder="高防-华东-100G" /></el-form-item>
        <el-form-item label="高防 IP"><el-input v-model="form.ip" placeholder="1.2.3.4" /></el-form-item>
        <el-form-item label="线路">
          <el-select v-model="form.line" style="width:200px">
            <el-option value="BGP" /><el-option value="三线" /><el-option value="海外" />
          </el-select>
        </el-form-item>
        <el-form-item label="保底带宽 (Mbps)"><el-input-number v-model="form.protectedMbps" :min="10" /></el-form-item>
        <el-form-item label="峰值带宽 (Mbps)"><el-input-number v-model="form.peakMbps" :min="100" /></el-form-item>
        <el-form-item label="防护对象"><el-input v-model="form.protectedTargets" placeholder="网站域名，逗号分隔" /></el-form-item>
        <el-form-item label="状态">
          <el-select v-model="form.status" style="width:200px">
            <el-option value="active" label="生效中" /><el-option value="expired" label="已到期" />
          </el-select>
        </el-form-item>
        <el-form-item label="到期时间">
          <el-date-picker v-model="form.expireAt" type="datetime" />
        </el-form-item>
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

const instances = ref([])
const loading = ref(false)
const dialog = ref(false)
const form = ref({})

const fmtTime = (t) => t ? new Date(t).toLocaleString() : '-'

async function load() {
  loading.value = true
  try { instances.value = await api.get('/admin/protection') } finally { loading.value = false }
}
onMounted(load)

function openEdit(row) {
  form.value = row ? JSON.parse(JSON.stringify(row)) : { name: '', ip: '', line: 'BGP', protectedMbps: 100, peakMbps: 1000, protectedTargets: '', status: 'active', expireAt: null }
  dialog.value = true
}

async function save() {
  if (!form.value.name || !form.value.ip) return ElMessage.warning('请填写名称和高防 IP')
  if (form.value.id) await api.put(`/admin/protection/${form.value.id}`, form.value)
  else await api.post('/admin/protection', form.value)
  ElMessage.success('已保存')
  dialog.value = false
  load()
}

async function del(row) {
  await api.delete(`/admin/protection/${row.id}`)
  ElMessage.success('已删除')
  load()
}
</script>

<style scoped>
.card-header { display: flex; justify-content: space-between; align-items: center; }
</style>
