<script setup lang="ts">
/** 5秒盾 —— JS 挑战人机验证 */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const form = ref<any>({})
watch(() => props.server, (s) => {
  if (!s) return
  const j = JSON.parse(JSON.stringify(s.security?.jsShield || {}))
  j.excludePaths = j.excludePaths || []
  form.value = j
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.jsShield = form.value
  saving.value = true
  try {
    await cdnApi.put('/admin/servers/' + props.serverId + '/security', sec)
    ElMessage.success('5秒盾配置已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">5秒盾</h5>
    <el-form label-width="160px" style="max-width: 640px">
      <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      <el-form-item label="挑战方式">
        <el-radio-group v-model="form.mode">
          <el-radio value="js">JS 跳转</el-radio>
          <el-radio value="meta">Meta 刷新</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="Cookie 有效期(s)"><el-input-number v-model="form.cookieSeconds" :min="60" :max="86400" /></el-form-item>
      <el-form-item label="失败封禁阈值"><el-input-number v-model="form.maxFails" :min="1" :max="100" /></el-form-item>
      <el-form-item label="封禁时长(s)"><el-input-number v-model="form.banSeconds" :min="10" :max="86400" /></el-form-item>
      <el-form-item label="例外路径">
        <el-select v-model="form.excludePaths" multiple filterable allow-create default-first-option placeholder="输入路径后回车，如 /api/" style="width: 100%">
          <el-option v-for="p in form.excludePaths" :key="p" :value="p" :label="p" />
        </el-select>
      </el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
