<script setup lang="ts">
/** HTTP —— 监听端口配置 */
import { ref, watch } from 'vue'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'save', patch: any): void }>()

const ports = ref('')
watch(() => props.server, (s) => {
  if (s) ports.value = (s.httpPorts || [80]).join(',')
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  const list = ports.value.split(',').map((x: string) => parseInt(x.trim())).filter((n: number) => n > 0 && n < 65536)
  if (!list.length) return
  saving.value = true
  emit('save', { httpPorts: list })
  saving.value = false
}
</script>

<template>
  <div>
    <h5 class="mb-3">HTTP 端口</h5>
    <el-input v-model="ports" placeholder="80,8080" style="max-width: 360px" />
    <div class="text-muted small mt-2 mb-3">多个端口用英文逗号分隔；节点在这些端口上接收 HTTP 请求。</div>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
