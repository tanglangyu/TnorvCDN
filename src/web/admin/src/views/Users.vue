<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>用户管理</span>
        <el-button type="primary" @click="openEdit(null)"><el-icon><Plus /></el-icon>创建用户</el-button>
      </div>
    </template>
    <el-table :data="users" v-loading="loading">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="username" label="用户名" />
      <el-table-column label="角色" width="100">
        <template #default="{ row }"><el-tag :type="row.isAdmin ? 'warning' : 'info'" size="small">{{ row.isAdmin ? '管理员' : '租户' }}</el-tag></template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'danger'" size="small">{{ row.enabled ? '启用' : '禁用' }}</el-tag></template>
      </el-table-column>
      <el-table-column label="创建时间" width="170">
        <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="220">
        <template #default="{ row }">
          <el-button size="small" @click="resetPwd(row)">改密</el-button>
          <el-button size="small" :type="row.enabled ? 'warning' : 'success'" @click="toggle(row)">{{ row.enabled ? '禁用' : '启用' }}</el-button>
          <el-popconfirm title="确认删除该用户？" @confirm="del(row)">
            <template #reference><el-button size="small" type="danger">删除</el-button></template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialog" title="创建用户" width="440px">
      <el-form label-width="90px">
        <el-form-item label="用户名"><el-input v-model="form.username" /></el-form-item>
        <el-form-item label="密码"><el-input v-model="form.password" type="password" show-password /></el-form-item>
        <el-form-item label="角色"><el-switch v-model="form.isAdmin" active-text="管理员" inactive-text="租户" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" @click="create">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="pwdDialog" title="修改密码" width="400px">
      <el-form label-width="90px">
        <el-form-item label="新密码"><el-input v-model="newPassword" type="password" show-password /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="pwdDialog = false">取消</el-button>
        <el-button type="primary" @click="savePwd">确定</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import api from '../api'

const users = ref([])
const loading = ref(false)
const dialog = ref(false)
const pwdDialog = ref(false)
const form = ref({ username: '', password: '', isAdmin: false })
const newPassword = ref('')
const editingUser = ref(null)

const fmtTime = (t) => t ? new Date(t).toLocaleString() : '-'

async function load() {
  loading.value = true
  try { users.value = await api.get('/admin/users') } finally { loading.value = false }
}
onMounted(load)

function openEdit() { form.value = { username: '', password: '', isAdmin: false }; dialog.value = true }

async function create() {
  if (!form.value.username || !form.value.password) return ElMessage.warning('请填写用户名与密码')
  if (form.value.password.length < 6) return ElMessage.warning('密码至少 6 位')
  await api.post('/admin/users', form.value)
  ElMessage.success('已创建')
  dialog.value = false
  load()
}

function resetPwd(row) { editingUser.value = row; newPassword.value = ''; pwdDialog.value = true }

async function savePwd() {
  if (!newPassword.value || newPassword.value.length < 6) return ElMessage.warning('密码至少 6 位')
  await api.put(`/admin/users/${editingUser.value.id}`, { password: newPassword.value })
  ElMessage.success('密码已修改')
  pwdDialog.value = false
}

async function toggle(row) {
  await api.put(`/admin/users/${row.id}`, { enabled: !row.enabled })
  ElMessage.success('已更新')
  load()
}

async function del(row) {
  try {
    await api.delete(`/admin/users/${row.id}`)
    ElMessage.success('已删除')
    load()
  } catch (e) { ElMessage.error(e.response?.data || '删除失败') }
}
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
</style>
