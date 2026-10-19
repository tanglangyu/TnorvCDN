<script setup lang="ts">
const props = defineProps<{ modelValue?: any; options?: { label: string; value: any }[]; disabled?: boolean; inline?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: any): void }>()
</script>
<template>
  <el-radio-group :model-value="modelValue" :disabled="disabled" @update:model-value="(v: any) => emit('update:modelValue', v)">
    <el-radio v-for="opt in options" :key="opt.value" :value="opt.value" :class="inline ? 'mr-3' : ''">{{ opt.label }}</el-radio>
  </el-radio-group>
</template>
