<script setup lang="ts">
/** CdnGate —— CDN 页面数据加载容器（同源架构，登录即连接主控）。 */
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'

const props = defineProps<{ loadData?: () => Promise<any> }>()
const emit = defineEmits<{ (e: 'data', data: any): void }>()
const loading = ref(false)
const data = ref<any>(null)

async function refresh() {
  if (!props.loadData) return
  loading.value = true
  try {
    data.value = await props.loadData()
    emit('data', data.value)
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.message || '数据加载失败')
  } finally {
    loading.value = false
  }
}

defineExpose({ refresh })
onMounted(refresh)
</script>

<template>
  <div v-loading="loading">
    <slot :data="data" :refresh="refresh" />
  </div>
</template>
