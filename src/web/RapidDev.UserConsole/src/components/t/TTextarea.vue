<script setup lang="ts">
const props = defineProps<{ modelValue?: string; placeholder?: string; rows?: number; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()
</script>
<template>
  <el-input
    type="textarea"
    :rows="rows || 3"
    :model-value="modelValue"
    :placeholder="placeholder"
    :disabled="disabled"
    @update:model-value="(v: string) => emit('update:modelValue', v)"
  />
</template>
