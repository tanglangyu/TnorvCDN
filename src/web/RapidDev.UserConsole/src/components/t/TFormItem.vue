<script setup lang="ts">
import { computed } from 'vue'
import type { FormItemSchema } from './types'
import TInput from './TInput.vue'
import TSelect from './TSelect.vue'
import TTextarea from './TTextarea.vue'
import TSwitch from './TSwitch.vue'
import TRadio from './TRadio.vue'
import TDatePicker from './TDatePicker.vue'

const props = defineProps<{ schema: FormItemSchema; modelValue: any; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: any): void }>()

const span = computed(() => props.schema.span ? props.schema.span * 2 : 12)
const value = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v),
})
</script>

<template>
  <el-col :span="span" :xs="24" :md="12">
    <el-form-item :label="schema.label" :required="schema.required">
      <!-- 自定义渲染 -->
      <slot name="default" :model="modelValue"></slot>
      <!-- 内置控件 -->
      <TInput v-if="!schema.type || schema.type === 'input'" v-model="value" :placeholder="schema.placeholder || `请输入${schema.label}`" :disabled="disabled || schema.disabled" v-bind="schema.props" />
      <TInput v-else-if="schema.type === 'number'" type="number" v-model="value" :placeholder="schema.placeholder || `请输入${schema.label}`" :disabled="disabled || schema.disabled" />
      <TTextarea v-else-if="schema.type === 'textarea'" v-model="value" :placeholder="schema.placeholder" :disabled="disabled" />
      <TSelect v-else-if="schema.type === 'select'" v-model="value" :options="schema.options" :placeholder="schema.placeholder || `请选择${schema.label}`" :disabled="disabled" />
      <TSwitch v-else-if="schema.type === 'switch'" v-model="value" :disabled="disabled" />
      <TRadio v-else-if="schema.type === 'radio'" v-model="value" :options="schema.options" :disabled="disabled" inline />
      <TDatePicker v-else-if="schema.type === 'date'" type="date" v-model="value" :placeholder="schema.placeholder" :disabled="disabled" />
      <TDatePicker v-else-if="schema.type === 'datetime'" type="datetime-local" v-model="value" :placeholder="schema.placeholder" :disabled="disabled" />
      <div v-if="schema.props?.hint" class="el-form-item-msg">{{ schema.props.hint }}</div>
    </el-form-item>
  </el-col>
</template>
