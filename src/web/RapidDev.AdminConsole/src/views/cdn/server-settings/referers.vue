<script setup lang="ts">
/** Referer 防盗链 —— 白/黑名单 + 空 Referer 策略（节点 pipeline 已实现） */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const form = ref<any>({ enabled: false, allowEmpty: true, whitelist: [], blacklist: [] })
watch(() => props.server, (s) => {
  if (!s) return
  const r = JSON.parse(JSON.stringify(s.security?.referers || {}))
  form.value = { enabled: !!r.enabled, allowEmpty: r.allowEmpty !== false, whitelist: r.whitelist || [], blacklist: r.blacklist || [] }
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.referers = form.value
  saving.value = true
  try {
    await cdnApi.put('/admin/servers/' + props.serverId + '/security', sec)
    ElMessage.success('防盗链配置已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">Referer 防盗链</h5>
    <el-form label-width="180px" style="max-width: 680px">
      <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      <el-form-item label="允许空 Referer">
        <el-switch v-model="form.allowEmpty" />
        <span class="text-muted small ms-2">直接打开/书签访问不放行时关闭</span>
      </el-form-item>
      <el-form-item label="白名单（域名后缀）">
        <el-select v-model="form.whitelist" multiple filterable allow-create default-first-option placeholder="如 example.com（含子域名）" style="width: 100%">
          <el-option v-for="d in form.whitelist" :key="d" :value="d" :label="d" />
        </el-select>
        <div class="text-muted small w-100">为空 = 不启用白名单校验</div>
      </el-form-item>
      <el-form-item label="黑名单（域名后缀）">
        <el-select v-model="form.blacklist" multiple filterable allow-create default-first-option placeholder="如 bad-site.com" style="width: 100%">
          <el-option v-for="d in form.blacklist" :key="d" :value="d" :label="d" />
        </el-select>
        <div class="text-muted small w-100">黑名单优先级高于白名单</div>
      </el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
