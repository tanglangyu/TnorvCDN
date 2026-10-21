<script setup lang="ts">
/** 首次安装引导 —— 全新系统（库中无任何用户）时由路由守卫自动带入：
 *  环境检测 → 设置管理员账号 → 完成。已在管理端保存后本页永久跳回登录。 */
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { http } from '@/utils/request'
import { ElMessage } from 'element-plus'
import { markSetupDone } from '@/router'

const router = useRouter()
const step = ref(0)
const needed = ref(false)
const dbOk = ref(false)
const clusterOk = ref(true) // 默认集群由启动逻辑保证
const checking = ref(true)
const form = ref({ username: '', password: '', confirm: '' })
const submitting = ref(false)
const adminName = ref('')

onMounted(async () => {
  try {
    const r: any = await http.get('/setup/status')
    needed.value = !!(r?.data?.needed ?? r?.needed)
    dbOk.value = true
    if (!needed.value) router.replace('/login')
  } catch {
    dbOk.value = false
  } finally { checking.value = false }
})

const submit = async () => {
  const u = form.value.username.trim()
  if (!/^[a-zA-Z0-9_一-龥]{3,32}$/.test(u)) return ElMessage.warning('用户名需 3-32 位，仅限字母/数字/下划线/中文')
  if (form.value.password.length < 8) return ElMessage.warning('密码至少 8 位')
  if (form.value.password !== form.value.confirm) return ElMessage.warning('两次输入的密码不一致')
  submitting.value = true
  try {
    await http.post('/setup/complete', { username: u, password: form.value.password })
    adminName.value = u
    markSetupDone()
    step.value = 2
  } finally { submitting.value = false }
}
const goLogin = () => router.replace('/login')
</script>

<template>
  <div class="setup-page">
    <div class="setup-card">
      <div class="text-center mb-4">
        <div class="setup-logo"><i class="ti ti-bolt"></i></div>
        <h1 class="setup-title">TnorvCDN</h1>
        <div class="text-muted">首次安装引导 · 检测到全新系统，请完成初始化</div>
      </div>

      <el-steps :active="step" align-center finish-status="success" class="mb-4">
        <el-step title="环境检测" />
        <el-step title="管理员账号" />
        <el-step title="完成" />
      </el-steps>

      <!-- Step 0 环境检测 -->
      <div v-if="step === 0" v-loading="checking" class="step-body">
        <div class="check-row"><span>数据库连接</span><el-tag :type="dbOk ? 'success' : 'danger'" size="small">{{ dbOk ? '正常' : '失败' }}</el-tag></div>
        <div class="check-row"><span>默认集群</span><el-tag :type="clusterOk ? 'success' : 'danger'" size="small">{{ clusterOk ? '已就绪（默认集群）' : '未就绪' }}</el-tag></div>
        <div class="check-row"><span>初始化状态</span><el-tag :type="needed ? 'warning' : 'info'" size="small">{{ needed ? '待初始化（尚无管理员账号）' : '已完成' }}</el-tag></div>
        <el-button type="primary" class="w-100 mt-4" size="large" :disabled="!dbOk || !needed" @click="step = 1">开始配置</el-button>
      </div>

      <!-- Step 1 管理员账号 -->
      <div v-else-if="step === 1" class="step-body">
        <el-alert type="info" show-icon :closable="false" class="mb-3"
          title="设置平台管理员账号。为安全起见系统不再内置默认账号，该账号拥有全部管理权限。" />
        <el-form label-position="top" @submit.prevent>
          <el-form-item label="管理员用户名">
            <el-input v-model="form.username" placeholder="3-32 位字母/数字/下划线/中文" maxlength="32" clearable />
          </el-form-item>
          <el-form-item label="密码">
            <el-input v-model="form.password" type="password" placeholder="至少 8 位" show-password maxlength="64" />
          </el-form-item>
          <el-form-item label="确认密码">
            <el-input v-model="form.confirm" type="password" placeholder="再次输入密码" show-password maxlength="64"
              @keyup.enter="submit" />
          </el-form-item>
        </el-form>
        <el-button type="primary" class="w-100" size="large" :loading="submitting" @click="submit">完成初始化</el-button>
      </div>

      <!-- Step 2 完成 -->
      <div v-else class="step-body text-center">
        <el-result icon="success" title="初始化完成"
          :sub-title="`管理员「${adminName}」已创建，默认集群已就绪。接下来可登录管理端，接入边缘节点与网站。`">
          <template #extra>
            <el-button type="primary" size="large" @click="goLogin">前往登录</el-button>
          </template>
        </el-result>
      </div>
    </div>
    <div class="setup-foot text-muted">Copyright © 2026 TnorvCDN</div>
  </div>
</template>

<style scoped>
.setup-page {
  min-height: 100vh;
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  background: linear-gradient(160deg, #e8f1ff 0%, #f5f8ff 60%, #eef4ff 100%);
  padding: 24px;
}
.setup-card {
  width: 520px; max-width: 100%;
  background: #fff; border-radius: 12px;
  box-shadow: 0 8px 40px rgba(64, 158, 255, .12);
  padding: 32px 36px;
}
.setup-logo {
  width: 56px; height: 56px; margin: 0 auto 12px;
  border-radius: 14px; background: var(--el-color-primary, #409EFF);
  display: flex; align-items: center; justify-content: center;
  color: #fff; font-size: 30px;
}
.setup-title { font-size: 24px; margin: 0 0 4px; }
.step-body { min-height: 220px; }
.check-row {
  display: flex; justify-content: space-between; align-items: center;
  padding: 12px 4px; border-bottom: 1px dashed var(--el-border-color-lighter);
}
.setup-foot { margin-top: 20px; font-size: 13px; }
</style>
