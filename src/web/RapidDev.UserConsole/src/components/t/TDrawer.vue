<script setup lang="ts">
const props = withDefaults(defineProps<{ modelValue?: boolean; title?: string; width?: string; placement?: 'left' | 'right' | 'bottom' }>(), { width: '480px', placement: 'right' })
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'close'): void }>()
function close() { emit('update:modelValue', false); emit('close') }
</script>
<template>
  <el-drawer
    :model-value="modelValue"
    :title="title"
    :size="width"
    :direction="placement === 'left' ? 'ltr' : placement === 'bottom' ? 'btt' : 'rtl'"
    @update:model-value="(v: boolean) => (v ? undefined : close())"
  >
    <slot></slot>
    <template #footer v-if="$slots.footer"><slot name="footer"></slot></template>
  </el-drawer>
</template>
