<script setup lang="ts">
import { reactive, watch } from 'vue'
import type { FormItemSchema } from './types'
import TFormItem from './TFormItem.vue'

const props = defineProps<{
  /** 表单数据模型(v-model) */
  modelValue: Record<string, any>
  /** 表单项 schema */
  schemas?: FormItemSchema[]
  /** 标签宽度 */
  labelWidth?: string
  /** 是否禁用整体表单 */
  disabled?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: Record<string, any>): void; (e: 'submit'): void; (e: 'reset'): void }>()

const form = reactive<Record<string, any>>({ ...props.modelValue })
watch(form, (v) => emit('update:modelValue', { ...v }), { deep: true })
watch(() => props.modelValue, (v) => Object.assign(form, v), { deep: true })

function validate(): true | string {
  for (const s of props.schemas || []) {
    if (s.required && (form[s.prop] === undefined || form[s.prop] === '' || form[s.prop] === null)) {
      return `${s.label}不能为空`
    }
    if (s.rules) {
      for (const rule of s.rules) {
        const r = rule(form[s.prop])
        if (r !== true) return r
      }
    }
  }
  return true
}
function submit() {
  const v = validate()
  if (v !== true) return { valid: false, message: v }
  emit('submit')
  return { valid: true }
}
function reset() {
  for (const s of props.schemas || []) form[s.prop] = s.defaultValue ?? ''
  emit('reset')
}
defineExpose({ validate, submit, reset })
</script>

<template>
  <el-form :label-width="labelWidth || '110px'" @submit.prevent="submit">
    <el-row :gutter="16">
      <template v-if="schemas">
        <TFormItem v-for="s in schemas" :key="s.prop" :schema="s" v-model="form[s.prop]" :disabled="disabled">
          <template #default="scope"><slot :name="s.prop" v-bind="scope"></slot></template>
        </TFormItem>
      </template>
      <slot v-else :form="form" :submit="submit" :reset="reset"></slot>
      <el-col :span="24" v-if="$slots.footer">
        <slot name="footer" :submit="submit" :reset="reset"></slot>
      </el-col>
    </el-row>
  </el-form>
</template>
