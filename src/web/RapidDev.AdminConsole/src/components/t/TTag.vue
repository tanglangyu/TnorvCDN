<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ variant?: string; color?: string; size?: 'sm' | 'md' | 'lg'; rounded?: boolean; closable?: boolean }>()
const emit = defineEmits<{ (e: 'close'): void }>()

// variant → el-tag type 映射
const typeMap: Record<string, string> = {
  primary: 'primary', blue: 'primary',
  success: 'success', green: 'success',
  danger: 'danger', red: 'danger',
  warning: 'warning', yellow: 'warning',
  info: 'info', gray: 'info',
  secondary: 'info', light: 'info', dark: 'info',
}
const tagType = computed(() => (typeMap[props.variant || props.color || ''] || 'primary') as any)
</script>
<template>
  <el-tag :type="tagType" :size="size === 'sm' ? 'small' : 'default'" :effect="rounded ? 'plain' : 'light'" :closable="closable" @close="emit('close')">
    <slot></slot>
  </el-tag>
</template>
