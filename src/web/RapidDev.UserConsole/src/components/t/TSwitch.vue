<script setup lang="ts">
defineProps<{ modelValue?: boolean; disabled?: boolean; label?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()
</script>
<template>
  <el-switch :model-value="!!modelValue" :disabled="disabled" :active-text="label" @update:model-value="(v: any) => emit('update:modelValue', !!v)" />
</template>
