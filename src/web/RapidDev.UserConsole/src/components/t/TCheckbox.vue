<script setup lang="ts">
defineProps<{ modelValue?: any; label?: string; value?: any; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: any): void }>()
</script>
<template>
  <el-checkbox :model-value="!!modelValue" :label="label" :disabled="disabled" @update:model-value="(v: any) => emit('update:modelValue', !!v)" />
</template>
