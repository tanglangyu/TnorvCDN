<script setup lang="ts">
/**
 * CdnGate —— CDN 页面数据加载容器。
 * 同源架构：登录即连接主控，无需二次配置。负责数据加载/刷新/错误提示。
 */
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'

const props = defineProps<{ loadData?: () => Promise<any> }>()
const loading = ref(false)
const failed = ref(false)
const data = ref<any>(null)

async function refresh() {
  if (!props.loadData) return
  loading.value = true
  failed.value = false
  try {
    data.value = await props.loadData()
  } catch (e: any) {
    failed.value = true
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
