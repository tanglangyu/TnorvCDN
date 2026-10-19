<script setup lang="ts">
import { computed } from 'vue'
import type { ButtonVariant, ButtonSize } from './types'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: ButtonSize
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    loading?: boolean
    block?: boolean
    icon?: string
    to?: string
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    disabled: false,
    loading: false,
    block: false,
  },
)

// Tabler variant → Element Plus type 映射
const typeMap: Record<string, string> = {
  primary: 'primary',
  secondary: 'default',
  success: 'success',
  danger: 'danger',
  warning: 'warning',
  info: 'info',
  light: 'default',
  dark: 'default',
  link: 'primary',
}
const typeValue = computed(() => {
  const v = props.variant || 'primary'
  if (v.startsWith('outline-')) return typeMap[v.slice(8)] || 'primary'
  if (v.startsWith('ghost-')) return typeMap[v.slice(6)] || 'primary'
  return typeMap[v] || 'primary'
})
const plain = computed(() => props.variant?.startsWith('outline-'))
const text = computed(() => props.variant?.startsWith('ghost-') || props.variant === 'link')
const elSize = computed(() => (props.size === 'sm' ? 'small' : props.size === 'lg' ? 'large' : 'default'))
</script>

<template>
  <el-button
    :type="(typeValue as any)"
    :plain="plain"
    :text="text"
    :size="elSize"
    :native-type="type"
    :disabled="disabled"
    :loading="loading"
    :style="block ? { width: '100%' } : undefined"
  >
    <slot name="icon" v-if="icon && !loading"></slot>
    <slot></slot>
  </el-button>
</template>
