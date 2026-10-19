<script setup lang="ts">
/** 日志/统计等跳转子页 */
import { useRouter } from 'vue-router'
const props = defineProps<{ serverId: number; server: any; to: string; title: string }>()
const router = useRouter()
const go = () => router.push({ path: props.to, query: { serverId: String(props.serverId) } })
</script>

<template>
  <div class="text-center py-5">
    <p class="text-muted">查看本站点的{{ title }}（已带站点过滤）</p>
    <el-button type="primary" @click="go">前往{{ title }} →</el-button>
  </div>
</template>
