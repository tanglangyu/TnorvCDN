<script setup lang="ts">
/** CC 防护 —— 单 IP 频率限制 */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const form = ref<any>({})
watch(() => props.server, (s) => {
  if (!s) return
  const c = JSON.parse(JSON.stringify(s.security?.cc || {}))
  c.excludePaths = c.excludePaths || []
  form.value = c
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.cc = form.value
  saving.value = true
  try {
    await cdnApi.put('/user/servers/' + props.serverId + '/security', sec)
    ElMessage.success('CC 防护配置已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">CC 防护</h5>
    <el-form label-width="180px" style="max-width: 640px">
      <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      <el-form-item label="请求阈值（次/IP）"><el-input-number v-model="form.threshold" :min="1" :max="100000" /></el-form-item>
      <el-form-item label="统计窗口（秒）"><el-input-number v-model="form.windowSeconds" :min="1" :max="600" /></el-form-item>
      <el-form-item label="封禁时长（秒）"><el-input-number v-model="form.banSeconds" :min="1" :max="86400" /></el-form-item>
      <el-form-item label="例外路径">
        <el-select v-model="form.excludePaths" multiple filterable allow-create default-first-option placeholder="如 /api/callback" style="width: 100%">
          <el-option v-for="p in form.excludePaths" :key="p" :value="p" :label="p" />
        </el-select>
      </el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
