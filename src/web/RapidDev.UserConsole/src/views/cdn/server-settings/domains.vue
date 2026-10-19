<script setup lang="ts">
/** 域名 —— 站点绑定域名列表（每行一个，支持通配符 *.example.com） */
import { ref, watch } from 'vue'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'save', patch: any): void }>()

const text = ref('')
watch(() => props.server, (s) => {
  if (s) text.value = (s.domains || []).join('\n')
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  const domains = text.value.split('\n').map((x: string) => x.trim().toLowerCase()).filter(Boolean)
  if (!domains.length) return
  saving.value = true
  emit('save', { domains })
  saving.value = false
}
</script>

<template>
  <div>
    <h5 class="mb-3">绑定域名</h5>
    <el-input v-model="text" type="textarea" :rows="8" placeholder="每行一个域名，支持 *.example.com 通配符" style="max-width: 560px" />
    <div class="text-muted small mt-2 mb-3">保存后自动为每个域名创建 CNAME 到集群入口，并尝试自动签发 HTTPS 证书。</div>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
