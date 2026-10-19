<script setup lang="ts">
/** 消息 —— 通知中心（站内通知列表 + 已读/删除） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()

const pager = ref({ page: 1, pageSize: 20, total: 0 })
const unreadOnly = ref(false)
const unreadCount = ref(0)

const loadData = async () => {
  const r = await cdnApi.get('/notification/list', { page: pager.value.page, pageSize: pager.value.pageSize }) as any
  const d = r?.data ?? r
  pager.value.total = d?.total ?? 0
  const items = (d?.items || []).filter((n: any) => !unreadOnly.value || !n.isRead)
  try {
    const c = await cdnApi.get('/notification/unread-count') as any
    unreadCount.value = (c?.data ?? c) || 0
  } catch { unreadCount.value = 0 }
  return items.map((n: any) => ({ ...n, time: n.createdAt ? new Date(n.createdAt).toLocaleString() : '' }))
}

const onSearch = () => { pager.value.page = 1; gate.value?.refresh?.() }

async function markRead(row: any) {
  try {
    await cdnApi.put(`/notification/${row.id}/read`, {})
    ElMessage.success('已标记已读')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '操作失败') }
}

async function readAll() {
  try { await ElMessageBox.confirm('将全部未读消息标记为已读？', '全部已读', { type: 'info' }) } catch { return }
  try {
    await cdnApi.put('/notification/read-all', {})
    ElMessage.success('已全部标记已读')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '操作失败') }
}

async function del(row: any) {
  try { await ElMessageBox.confirm('确定删除该条消息？', '删除消息', { type: 'warning' }) } catch { return }
  try {
    await cdnApi.delete(`/notification/${row.id}`)
    ElMessage.success('已删除')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '删除失败') }
}

async function clearRead() {
  try { await ElMessageBox.confirm('将清除全部已读消息，不可恢复。确认？', '清除已读', { type: 'warning' }) } catch { return }
  try {
    await cdnApi.delete('/notification/read')
    ElMessage.success('已读消息已清除')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '操作失败') }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">消息</h2><div class="text-muted">系统消息通知 · 未读 {{ unreadCount }} 条</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center flex-wrap">
            <el-checkbox v-model="unreadOnly" @change="onSearch">只看未读</el-checkbox>
            <el-button size="small" type="primary" plain @click="readAll">全部已读</el-button>
            <el-button size="small" type="danger" plain @click="clearRead">清除已读</el-button>
            <el-button size="small" @click="gate?.refresh?.()">刷新</el-button>
          </div>
          <el-table v-if="(data || []).length" :data="data" stripe>
            <el-table-column width="60">
              <template #default="{ row }">
                <el-tag v-if="!row.isRead" type="danger" size="small">未读</el-tag>
                <span v-else class="text-muted small">已读</span>
              </template>
            </el-table-column>
            <el-table-column prop="title" label="标题" min-width="160" />
            <el-table-column prop="content" label="内容" min-width="260" show-overflow-tooltip />
            <el-table-column prop="time" label="时间" width="170" />
            <el-table-column label="操作" width="170">
              <template #default="{ row }">
                <el-button v-if="!row.isRead" size="small" @click="markRead(row)">已读</el-button>
                <el-button size="small" type="danger" plain @click="del(row)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
          <el-empty v-else description="暂无消息" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
