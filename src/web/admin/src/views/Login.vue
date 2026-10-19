<template>
  <div class="login-page">
    <el-card class="login-card">
      <h2>TnorvCDN 管理平台</h2>
      <el-form :model="form" @keyup.enter="submit">
        <el-form-item>
          <el-input v-model="form.username" placeholder="用户名" size="large">
            <template #prefix><el-icon><User /></el-icon></template>
          </el-input>
        </el-form-item>
        <el-form-item>
          <el-input v-model="form.password" type="password" placeholder="密码" size="large" show-password>
            <template #prefix><el-icon><Lock /></el-icon></template>
          </el-input>
        </el-form-item>
        <el-button type="primary" size="large" style="width:100%" :loading="loading" @click="submit">登 录</el-button>
      </el-form>
    </el-card>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import api from '../api'

const router = useRouter()
const form = ref({ username: '', password: '' })
const loading = ref(false)

const submit = async () => {
  if (!form.value.username || !form.value.password) return ElMessage.warning('请输入用户名和密码')
  loading.value = true
  try {
    const res = await api.post('/auth/login', form.value)
    localStorage.setItem('token', res.token)
    router.push('/dashboard')
  } catch {
    ElMessage.error('用户名或密码错误')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page { height: 100vh; display: flex; align-items: center; justify-content: center; background: linear-gradient(135deg, #001529, #003a70); }
.login-card { width: 380px; padding: 12px 8px; }
.login-card h2 { text-align: center; margin-bottom: 24px; }
</style>
