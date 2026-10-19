<script setup lang="ts">
/**
 * 个人中心 -- 基本信息展示 + 我的登录设备管理。
 * 设备数据来源 GET /api/auth/session/list(当前用户活跃会话)。
 * 下线当前会话 -> 后端推 session_kicked -> useNotification 自动登出跳转。
 */
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { useUserStore } from '@/store/user'
import { authApi } from '@/api/auth'
import { formatTime } from '@/utils/time'
import type { SessionInfo } from '@/types'

const userStore = useUserStore()

const loading = ref(false)
const devices = ref<SessionInfo[]>([])

async function loadDevices() {
  loading.value = true
  try { devices.value = (await authApi.session.list()) as SessionInfo[] } finally { loading.value = false }
}

function isCurrent(s: SessionInfo) {
  return !!userStore.sessionId && s.sessionId === userStore.sessionId
}

function deviceName(s: SessionInfo) {
  return s.device || s.ip || s.sessionId.slice(0, 8)
}

async function revoke(s: SessionInfo) {
  const tip = isCurrent(s)
    ? '这将下线当前设备,您会被立即登出。确定继续?'
    : `确定下线设备「${deviceName(s)}」?`
  try {
    await ElMessageBox.confirm(tip, '下线设备', { type: 'warning', confirmButtonText: '确定', cancelButtonText: '取消' })
  } catch { return }
  try { await authApi.session.revoke(s.sessionId); await loadDevices() } catch {}
}

async function revokeOthers() {
  try {
    await ElMessageBox.confirm('确定下线其他所有设备?当前设备保留。', '批量下线', { type: 'warning', confirmButtonText: '确定', cancelButtonText: '取消' })
  } catch { return }
  try { await authApi.session.revokeOthers(); await loadDevices(); ElMessage.success('已下线其他设备') } catch {}
}

onMounted(() => { loadDevices(); loadIdentity() })

// ---- 实名认证通用设计----
import { cdnApi } from '@/api/cdn'
const identity = ref<any>(null)
const idVisible = ref(false)
const idSaving = ref(false)
const idForm = ref({ type: 'personal', realName: '', idNumber: '', enterpriseName: '' })
async function loadIdentity() {
  try { identity.value = await cdnApi.get('/user/identity').catch(() => null) } catch { }
}
async function submitIdentity() {
  if (!idForm.value.realName || !idForm.value.idNumber) { ElMessage.warning('请填写完整'); return }
  idSaving.value = true
  try {
    await cdnApi.post('/user/identity', idForm.value)
    ElMessage.success('已提交，等待管理员审核')
    idVisible.value = false
    await loadIdentity()
  } catch (e: any) {
    ElMessage.error(typeof e?.response?.data === 'string' ? e.response.data : '提交失败')
  } finally {
    idSaving.value = false
  }
}
</script>
<template>
  <div class="profile-page">
    <!-- 顶部用户横幅 -->
    <el-card shadow="never" class="profile-hero" :body-style="{ padding: 0 }">
      <div class="hero-banner">
        <div class="hero-avatar">{{ userStore.userInfo?.nickName?.charAt(0) || 'U' }}</div>
        <div class="hero-info">
          <div class="hero-name">
            {{ userStore.userInfo?.nickName }}
            <el-tag v-for="r in userStore.roles" :key="r" type="primary" effect="light" round size="small" class="hero-role">{{ r }}</el-tag>
          </div>
          <div class="hero-sub">
            <span class="hero-user"><i class="ti ti-user"></i>{{ userStore.userInfo?.userName }}</span>
            <span class="hero-user"><i class="ti ti-shield"></i>租户 {{ userStore.userInfo?.tenantId ?? 0 }}</span>
            <span class="hero-user"><i class="ti ti-key"></i>权限 {{ userStore.permissions.length }} 项</span>
          </div>
        </div>
      </div>
    </el-card>

    <el-row :gutter="16" class="mt-3">
      <!-- 基本信息 -->
      <el-col :xs="24" :md="8">
        <el-card shadow="never" class="h-full">
          <template #header>
            <div class="card-head"><i class="ti ti-id"></i><span>基本信息</span></div>
          </template>
          <el-descriptions :column="1" border size="large">
            <el-descriptions-item label="用户名">{{ userStore.userInfo?.userName }}</el-descriptions-item>
            <el-descriptions-item label="昵称">{{ userStore.userInfo?.nickName }}</el-descriptions-item>
            <el-descriptions-item label="租户">{{ userStore.userInfo?.tenantId }}</el-descriptions-item>
            <el-descriptions-item label="权限数">{{ userStore.permissions.length }}</el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-col>

      <!-- 登录设备 -->
      <el-col :xs="24" :md="16">
        <el-card shadow="never" class="h-full">
          <template #header>
            <div class="card-head">
              <span><i class="ti ti-devices"></i>我的登录设备</span>
              <div class="card-actions">
                <el-button size="small" circle :loading="loading" @click="loadDevices">
                  <i class="ti ti-refresh"></i>
                </el-button>
                <el-button size="small" type="warning" plain @click="revokeOthers">
                  <i class="ti ti-logout" style="margin-right: 4px"></i>下线其他设备
                </el-button>
              </div>
            </div>
          </template>
          <el-table :data="devices" v-loading="loading" stripe size="default" empty-text="暂无活跃设备">
            <el-table-column label="设备" min-width="130">
              <template #default="{ row }">
                <span class="device-name"><i class="ti ti-device-desktop"></i>{{ row.device || '-' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="IP" prop="ip" min-width="120">
              <template #default="{ row }">{{ row.ip || '-' }}</template>
            </el-table-column>
            <el-table-column label="登录时间" min-width="150">
              <template #default="{ row }">{{ formatTime(row.loginAt) }}</template>
            </el-table-column>
            <el-table-column label="最后活跃" min-width="150">
              <template #default="{ row }">{{ formatTime(row.lastActiveAt) }}</template>
            </el-table-column>
            <el-table-column label="过期时间" min-width="150">
              <template #default="{ row }">{{ formatTime(row.expireAt) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="95">
              <template #default="{ row }">
                <el-tag v-if="isCurrent(row)" type="success" effect="light" size="small">当前设备</el-tag>
                <el-tag v-else type="info" effect="plain" size="small">在线</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="90" fixed="right">
              <template #default="{ row }">
                <el-button link type="danger" size="small" @click="revoke(row)">
                  <i class="ti ti-logout" style="margin-right: 2px"></i>下线
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>

    <!-- 实名认证通用设计 -->
    <el-row class="mt-3">
      <el-col :span="24">
        <el-card shadow="never">
          <template #header>
            <div class="card-head"><i class="ti ti-certificate"></i><span>实名认证</span></div>
          </template>
          <div v-if="identity" class="d-flex align-items-center gap-2">
            <el-tag :type="identity.status === 'approved' ? 'success' : identity.status === 'pending' ? 'warning' : 'danger'" size="small">
              {{ identity.status === 'approved' ? '已通过' : identity.status === 'pending' ? '审核中' : '已驳回' }}
            </el-tag>
            <span class="me-2">{{ identity.type === 'enterprise' ? '企业认证' : '个人认证' }} · {{ identity.realName }}</span>
            <span v-if="identity.type === 'enterprise'" class="text-muted small">{{ identity.enterpriseName }}</span>
            <span v-if="identity.status === 'rejected' && identity.note" class="text-danger small">（{{ identity.note }}）</span>
          </div>
          <div v-else class="d-flex align-items-center gap-2">
            <span class="text-muted me-2">尚未实名认证</span>
            <el-button size="small" type="primary" plain @click="idVisible = true">提交认证</el-button>
          </div>
          <div v-if="identity && identity.status === 'rejected'" class="mt-2">
            <el-button size="small" type="primary" plain @click="idVisible = true">重新提交</el-button>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-dialog v-model="idVisible" title="提交实名认证" width="480px">
      <el-form label-width="90px">
        <el-form-item label="认证类型">
          <el-radio-group v-model="idForm.type">
            <el-radio value="personal">个人</el-radio>
            <el-radio value="enterprise">企业</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="idForm.type === 'enterprise' ? '联系人' : '真实姓名'">
          <el-input v-model="idForm.realName" maxlength="50" />
        </el-form-item>
        <el-form-item :label="idForm.type === 'enterprise' ? '信用代码' : '证件号码'">
          <el-input v-model="idForm.idNumber" maxlength="30" />
        </el-form-item>
        <el-form-item v-if="idForm.type === 'enterprise'" label="企业名称">
          <el-input v-model="idForm.enterpriseName" maxlength="100" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="idVisible = false">取消</el-button>
        <el-button type="primary" :loading="idSaving" @click="submitIdentity">提交</el-button>
      </template>
    </el-dialog>
  </div>
</template>
<style scoped>
.profile-page { max-width: 1200px; }

/* 顶部横幅 */
.hero-banner {
  display: flex;
  align-items: center;
  gap: 20px;
  padding: 24px 28px;
  background: linear-gradient(120deg, var(--el-color-primary-light-9) 0%, var(--el-bg-color) 70%);
}
.hero-avatar {
  flex-shrink: 0;
  width: 72px;
  height: 72px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 30px;
  font-weight: 600;
  color: #fff;
  background: linear-gradient(135deg, var(--el-color-primary) 0%, var(--el-color-primary-dark-2) 100%);
  box-shadow: 0 4px 12px var(--el-color-primary-light-5);
}
.hero-name {
  font-size: 20px;
  font-weight: 600;
  color: var(--el-text-color-primary);
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.hero-sub {
  margin-top: 8px;
  display: flex;
  gap: 18px;
  flex-wrap: wrap;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.hero-user i { margin-right: 4px; }

/* 卡片头 */
.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.card-head i { margin-right: 6px; color: var(--el-color-primary); }
.card-actions { display: flex; align-items: center; gap: 8px; }

/* 设备列表 */
.device-name { display: inline-flex; align-items: center; }
.device-name i { margin-right: 6px; color: var(--el-text-color-secondary); }

.h-full { height: 100%; }
</style>
