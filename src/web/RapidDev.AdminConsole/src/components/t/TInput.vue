<script setup lang="ts">
import { computed } from 'vue'
const props = withDefaults(
  defineProps<{
    modelValue?: string | number
    type?: string
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    size?: 'sm' | 'md' | 'lg'
    icon?: string
    suffix?: string
    clearable?: boolean
  }>(),
  { type: 'text', disabled: false, readonly: false, size: 'md', clearable: false },
)
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void; (e: 'clear'): void }>()

const elSize = computed(() => (props.size === 'sm' ? 'small' : props.size === 'lg' ? 'large' : 'default'))
function onInput(v: string) {
  emit('update:modelValue', v)
}
function clear() {
  emit('update:modelValue', '')
  emit('clear')
}
</script>

<template>
  <el-input
    :type="type === 'number' ? 'number' : type"
    :model-value="modelValue as any"
    :placeholder="placeholder"
    :disabled="disabled"
    :readonly="readonly"
    :size="elSize"
    :clearable="clearable"
    @update:model-value="onInput"
    @clear="clear"
  >
    <template #prefix v-if="icon">
      <i class="ti" :class="`ti-${icon}`"></i>
    </template>
    <template #append v-if="suffix">{{ suffix }}</template>
  </el-input>
</template>
