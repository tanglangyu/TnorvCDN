<script setup lang="ts">
const props = withDefaults(defineProps<{ modelValue?: boolean; title?: string; size?: 'sm' | 'md' | 'lg' | 'xl'; closable?: boolean }>(), { size: 'md', closable: true })
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void; (e: 'close'): void }>()
const width = { sm: '420px', md: '600px', lg: '900px', xl: '1200px' }[props.size]
function close() {
  if (!props.closable) return
  emit('update:modelValue', false)
  emit('close')
}
</script>
<template>
  <el-dialog
    :model-value="modelValue"
    :title="title"
    :width="width"
    :show-close="closable"
    :close-on-click-modal="closable"
    @update:model-value="(v: boolean) => (v ? undefined : close())"
    align-center
    destroy-on-close
  >
    <template #header v-if="$slots.title">
      <span>{{ title }}<slot name="title"></slot></span>
    </template>
    <slot></slot>
    <template #footer v-if="$slots.footer"><slot name="footer"></slot></template>
  </el-dialog>
</template>
