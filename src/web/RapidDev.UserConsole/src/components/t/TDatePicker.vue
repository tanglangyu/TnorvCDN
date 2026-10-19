<script setup lang="ts">
defineProps<{ modelValue?: string; placeholder?: string; disabled?: boolean; type?: string }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()
const elType = (t?: string) => (t === 'date' ? 'date' : 'datetime')
</script>
<template>
  <el-date-picker
    :type="(elType(type) as any)"
    :model-value="modelValue"
    :placeholder="placeholder"
    :disabled="disabled"
    value-format="YYYY-MM-DDTHH:mm:ss"
    style="width: 100%"
    @update:model-value="(v: any) => emit('update:modelValue', v || '')"
  />
</template>
