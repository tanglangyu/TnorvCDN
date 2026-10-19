<script setup lang="ts">
const props = defineProps<{
  modelValue?: any
  options?: { label: string; value: any }[]
  placeholder?: string
  disabled?: boolean
  size?: 'sm' | 'md' | 'lg'
  multiple?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: any): void }>()
</script>

<template>
  <el-select
    :model-value="modelValue"
    :placeholder="placeholder"
    :disabled="disabled"
    :size="size === 'sm' ? 'small' : size === 'lg' ? 'large' : 'default'"
    :multiple="multiple"
    clearable
    style="width: 100%"
    @update:model-value="(v: any) => emit('update:modelValue', v)"
  >
    <el-option v-for="opt in options" :key="opt.value" :label="opt.label" :value="opt.value" />
    <slot></slot>
  </el-select>
</template>
