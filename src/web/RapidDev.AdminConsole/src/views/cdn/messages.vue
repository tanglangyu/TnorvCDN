<script setup lang="ts">
/**
 * 消息中心（管理端）—— 通用设计
 * 站内通知列表（阈值告警/系统通知/手动发送），支持未读筛选、批量已读、删除、发送通知（广播/指定用户）。
 */
import { ref, onMounted, computed } from 'vue'
import { notificationApi } from '@/api/notification'
import { ElMessage, ElMessageBox } from 'element-plus'

const items = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const unreadOnly = ref(false)
const loading = ref(false)

const typeMap: Record<number, { label: string; type: 'warning' | 'info' | 'primary' }> = {
  1: { label: '告警', type: 'warning' },
  2: { label: '系统', type: 'primary' },
  0: { label: '通知', type: 'info' },
}

const load = async () => {
  loading.value = true
  try {
    // 未读筛选在端点无参数，取大页后前端过滤（数据量为管理端通知级别）
    const res = await notificationApi.list({ page: 1, pageSize: 100 })
    let list: any[] = res.items || []
    if (unreadOnly.value) list = list.filter((x) => !x.isRead)
    items.value = list
    total.value = list.length
  } finally {
    loading.value = false
  }
}
onMounted(load)

const sendVisible = ref(false)
const sendForm = ref({ userId: 0, title: '', content: '' })
const sending = ref(false)
const openSend = () => {
  sendForm.value = { userId: 0, title: '', content: '' }
  sendVisible.value = true
}
const doSend = async () => {
  if (!sendForm.value.title) { ElMessage.warning('请填写标题'); return }
  sending.value = true
  try {
    await notificationApi.send({ ...sendForm.value })
    ElMessage.success('通知已发送（站内信 + 实时推送）')
    sendVisible.value = false
    await load()
  } finally {
    sending.value = false
  }
}

const markRead = async (row: any) => {
  await notificationApi.markRead(row.id)
  row.isRead = true
  ElMessage.success('已标记已读')
}
const markAll = async () => {
  await notificationApi.markAllRead()
  ElMessage.success('全部已读')
  await load()
}
const removeOne = async (row: any) => {
  await ElMessageBox.confirm('确定删除该消息？', '提示', { type: 'warning' })
  await notificationApi.remove(row.id)
  ElMessage.success('已删除')
  await load()
}
const fmtTime = (t: string) => (t ? new Date(t).toLocaleString() : '—')
</script>

<template>
  <div>
    <div class="page-header mb-3">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">消息中心</h2>
          <div class="text-muted">站内通知：阈值告警、系统消息与管理员发送的通知（实时推送）</div>
        </div>
        <div class="col-auto ms-auto">
          <el-switch v-model="unreadOnly" active-text="只看未读" class="me-3" @change="load" />
          <el-button size="small" @click="markAll">全部已读</el-button>
          <el-button size="small" type="primary" @click="openSend">发送通知</el-button>
        </div>
      </div>
    </div>

    <el-card v-loading="loading">
      <el-table :data="items" stripe>
        <el-table-column label="类型" width="90">
          <template #default="{ row }">
            <el-tag :type="(typeMap[row.type] || typeMap[0]).type" size="small">{{ (typeMap[row.type] || typeMap[0]).label }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="title" label="标题" min-width="200" show-overflow-tooltip />
        <el-table-column prop="content" label="内容" min-width="320" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag v-if="!row.isRead" type="danger" size="small" effect="plain">未读</el-tag>
            <span v-else class="text-muted small">已读</span>
          </template>
        </el-table-column>
        <el-table-column label="时间" width="170">
          <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" fixed="right">
          <template #default="{ row }">
            <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
          <el-button v-if="!row.isRead" link type="primary" size="small" @click="markRead(row)">已读</el-button>
          <el-button link type="danger" size="small" @click="removeOne(row)">删除</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && items.length === 0" description="暂无消息" />
    </el-card>

    <el-dialog align-center v-model="sendVisible" title="发送通知" width="520px">
      <el-form label-width="90px">
        <el-form-item label="接收人">
          <el-radio-group v-model="sendForm.userId">
            <el-radio :value="0">全部用户（广播）</el-radio>
            <el-radio :value="-1">指定用户</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="sendForm.userId === -1" label="用户 ID">
          <el-input-number v-model="sendForm.userId" :min="1" placeholder="用户 ID" />
        </el-form-item>
        <el-form-item label="标题">
          <el-input v-model="sendForm.title" placeholder="通知标题" maxlength="100" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input v-model="sendForm.content" type="textarea" :rows="4" placeholder="通知内容" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="sendVisible = false">取消</el-button>
        <el-button type="primary" :loading="sending" @click="doSend">发送</el-button>
      </template>
    </el-dialog>
  </div>
</template>
