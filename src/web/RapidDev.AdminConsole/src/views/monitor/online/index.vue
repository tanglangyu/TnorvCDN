<script setup lang="ts">
/**
 * 在线用户 -- 来源 GET /api/monitor/server/online。
 * 数据从 sys_session 实时派生(IsActive && ExpireAt>now,按用户聚合),
 * 不再依赖登录时写入的 Redis SET(旧实现无心跳/TTL/清理,下线用户残留累积)。
 * 强制下线:DELETE /api/monitor/server/online/{userId}
 */
import { ref, onMounted } from 'vue'
import { TCard, TButton } from '@/components/t'
import { monitorApi, type OnlineUser } from '@/api/monitor'
import { formatTime } from '@/utils/time'

const loading = ref(false)
const list = ref<OnlineUser[]>([])

async function load() {
  loading.value = true
  try { list.value = (await monitorApi.onlineUsers()) as OnlineUser[] } finally { loading.value = false }
}

async function forceLogout(row: OnlineUser) {
  if (!confirm(`确定强制下线用户「${row.userName}」?将撤销其全部活跃会话。`)) return
  try { await monitorApi.forceLogout(row.id); load() } catch {}
}

onMounted(load)
</script>

<template>
  <TCard title="在线用户">
    <template #actions>
      <TButton size="sm" variant="ghost-secondary" :loading="loading" @click="load"><i class="ti ti-refresh"></i></TButton>
    </template>
    <div class="text-muted small mb-2">
      <i class="ti ti-info-circle me-1"></i>数据从活跃会话(sys_session)实时派生,会话过期或被踢后自动移除。
    </div>
    <div class="table-responsive">
      <table class="table table-vcenter card-table">
        <thead>
          <tr>
            <th>用户ID</th><th>用户名</th><th>昵称</th><th>设备</th><th>IP</th>
            <th>会话数</th><th>最近登录</th><th>最后活跃</th><th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="loading">
            <td colspan="9" class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div>加载中...</td>
          </tr>
          <tr v-else-if="list.length === 0">
            <td colspan="9" class="text-center text-muted py-4">暂无在线用户</td>
          </tr>
          <tr v-for="u in list" :key="u.id">
            <td>{{ u.id }}</td>
            <td>{{ u.userName }}</td>
            <td>{{ u.nickName }}</td>
            <td>{{ u.device || '-' }}</td>
            <td>{{ u.ip || '-' }}</td>
            <td><span class="badge bg-primary-lt">{{ u.sessionCount }}</span></td>
            <td>{{ u.loginAt ? formatTime(u.loginAt) : '-' }}</td>
            <td>{{ u.lastActiveAt ? formatTime(u.lastActiveAt) : '-' }}</td>
            <td><TButton variant="ghost-danger" size="sm" @click="forceLogout(u)"><i class="ti ti-logout"></i>强制下线</TButton></td>
          </tr>
        </tbody>
      </table>
    </div>
  </TCard>
</template>
