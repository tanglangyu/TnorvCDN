<script setup lang="ts">
/**
 * 个人中心 -- 基本信息展示 + 我的登录设备管理。
 * 设备数据来源 GET /api/auth/session/list(当前用户活跃会话)。
 * 下线当前会话 -> 后端推 session_kicked -> useNotification 自动登出跳转。
 */
import { ref, onMounted } from 'vue'
import { useUserStore } from '@/store/user'
import { TCard, TButton } from '@/components/t'
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

async function revoke(s: SessionInfo) {
  if (isCurrent(s)) {
    if (!confirm('这将下线当前设备,您会被立即登出。确定继续?')) return
  } else {
    if (!confirm(`确定下线设备「${s.device || s.ip || s.sessionId.slice(0, 8)}」?`)) return
  }
  try { await authApi.session.revoke(s.sessionId); await loadDevices() } catch {}
}

async function revokeOthers() {
  if (!confirm('确定下线其他所有设备?当前设备保留。')) return
  try { await authApi.session.revokeOthers(); await loadDevices() } catch {}
}

onMounted(loadDevices)
</script>
<template>
  <div class="row">
    <div class="col-md-4">
      <TCard>
        <div class="text-center mb-3">
          <span class="avatar avatar-xl rounded-circle bg-primary-lt">{{ userStore.userInfo?.nickName?.charAt(0) || 'U' }}</span>
          <h3 class="mt-3 mb-0">{{ userStore.userInfo?.nickName }}</h3>
          <div class="text-muted">{{ userStore.userInfo?.userName }}</div>
        </div>
        <div class="d-flex flex-wrap gap-1 justify-content-center">
          <span v-for="r in userStore.roles" :key="r" class="badge bg-primary-lt">{{ r }}</span>
        </div>
      </TCard>
    </div>
    <div class="col-md-8">
      <TCard title="基本信息">
        <div class="datagrid">
          <div class="datagrid-item"><div class="datagrid-title">用户名</div><div class="datagrid-content">{{ userStore.userInfo?.userName }}</div></div>
          <div class="datagrid-item"><div class="datagrid-title">昵称</div><div class="datagrid-content">{{ userStore.userInfo?.nickName }}</div></div>
          <div class="datagrid-item"><div class="datagrid-title">租户</div><div class="datagrid-content">{{ userStore.userInfo?.tenantId }}</div></div>
          <div class="datagrid-item"><div class="datagrid-title">权限数</div><div class="datagrid-content">{{ userStore.permissions.length }}</div></div>
        </div>
      </TCard>
    </div>
  </div>

  <TCard title="我的登录设备" class="mt-3">
    <template #actions>
      <TButton size="sm" variant="ghost-secondary" :loading="loading" @click="loadDevices"><i class="ti ti-refresh"></i></TButton>
      <TButton size="sm" variant="ghost-warning" @click="revokeOthers"><i class="ti ti-logout me-1"></i>下线其他设备</TButton>
    </template>
    <div class="table-responsive">
      <table class="table table-vcenter card-table">
        <thead>
          <tr><th>设备</th><th>IP</th><th>登录时间</th><th>最后活跃</th><th>过期时间</th><th>状态</th><th>操作</th></tr>
        </thead>
        <tbody>
          <tr v-if="loading">
            <td colspan="7" class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div>加载中...</td>
          </tr>
          <tr v-else-if="devices.length === 0">
            <td colspan="7" class="text-center text-muted py-4">暂无活跃设备</td>
          </tr>
          <tr v-for="s in devices" :key="s.sessionId">
            <td>{{ s.device || '-' }}</td>
            <td>{{ s.ip || '-' }}</td>
            <td>{{ formatTime(s.loginAt) }}</td>
            <td>{{ formatTime(s.lastActiveAt) }}</td>
            <td>{{ formatTime(s.expireAt) }}</td>
            <td>
              <span v-if="isCurrent(s)" class="badge bg-success-lt">当前设备</span>
              <span v-else class="badge bg-secondary-lt">在线</span>
            </td>
            <td><TButton variant="ghost-danger" size="sm" @click="revoke(s)"><i class="ti ti-logout"></i>下线</TButton></td>
          </tr>
        </tbody>
      </table>
    </div>
  </TCard>
</template>
