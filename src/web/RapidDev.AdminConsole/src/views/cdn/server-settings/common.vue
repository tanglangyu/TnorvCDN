<script setup lang="ts">
/** 其他设置 —— 启用状态等杂项（与基本信息共用字段，此处为快捷入口） */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const form = ref({ enabled: true, forceHttps: false })
watch(() => props.server, (s) => {
  if (!s) return
  form.value.enabled = s.enabled !== false
  form.value.forceHttps = !!s.forceHttps
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  saving.value = true
  try {
    await cdnApi.put('/admin/servers/' + props.serverId + '/ssl', {
      sslMode: props.server.sslMode || 'auto',
      forceHttps: form.value.forceHttps,
      httpsPorts: props.server.httpsPorts || [443],
    })
    const body: any = { enabled: form.value.enabled }
    await cdnApi.put('/admin/servers/' + props.serverId, { ...props.server, ...body })
    ElMessage.success('已保存并下发到节点')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}

const purge = async () => {
  await ElMessageBox.confirm('清空本站点的全部边缘缓存？', '刷新缓存', { type: 'warning' })
  await cdnApi.post('/admin/purge', { matchType: 'host', host: (props.server.domains || [])[0] || '' })
  ElMessage.success('缓存刷新指令已下发')
}
</script>

<template>
  <div>
    <h5 class="mb-3">其他设置</h5>
    <el-form label-width="160px" style="max-width: 620px">
      <el-form-item label="启用站点"><el-switch v-model="form.enabled" /></el-form-item>
      <el-form-item label="强制 HTTPS"><el-switch v-model="form.forceHttps" /></el-form-item>
    </el-form>
    <div class="d-flex gap-2">
      <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      <el-button @click="purge">刷新本站缓存</el-button>
    </div>
  </div>
</template>
