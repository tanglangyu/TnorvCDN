<script setup lang="ts">
/** 基本信息 —— 站点类型 / 启用状态通用设计 */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void; (e: 'save', patch: any): void }>()

const form = ref({ serverType: 'http', enabled: true })
watch(() => props.server, (s) => {
  if (!s) return
  form.value.serverType = s.serverType || 'http'
  form.value.enabled = s.enabled !== false
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  saving.value = true
  emit('save', { serverType: form.value.serverType, enabled: form.value.enabled })
  saving.value = false
}
</script>

<template>
  <div>
    <h5 class="mb-3">基本信息</h5>
    <el-form label-width="140px" style="max-width: 640px">
      <el-form-item label="站点域名">
        <span>{{ (server.domains || []).join(', ') }}</span>
      </el-form-item>
      <el-form-item label="站点类型">
        <el-select v-model="form.serverType" style="width: 220px" :disabled="(server.l4Ports || []).length === 0">
          <el-option value="http" label="CDN 网站加速" />
          <el-option value="tcp" label="TCP 转发" />
          <el-option value="tls" label="TLS 转发" />
          <el-option value="udp" label="UDP 转发" />
        </el-select>
        <div class="text-muted small w-100">L4 端口为空时仅支持 HTTP 加速类型</div>
      </el-form-item>
      <el-form-item label="启用站点">
        <el-switch v-model="form.enabled" />
      </el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
