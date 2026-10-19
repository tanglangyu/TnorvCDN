<script setup lang="ts">
import { useTheme } from '@/composables/useTheme'
const { mode, toggle } = useTheme()
</script>
<template>
  <el-button text circle @click="toggle()" :title="mode === 'light' ? '切换到暗色模式' : '切换到亮色模式'">
    <i class="ti" :class="mode === 'light' ? 'ti-moon' : 'ti-sun'"></i>
  </el-button>
</template>
