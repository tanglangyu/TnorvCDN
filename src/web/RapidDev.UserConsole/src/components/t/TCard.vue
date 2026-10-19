<script setup lang="ts">
import { computed } from 'vue'
const props = withDefaults(defineProps<{ title?: string; bordered?: boolean; noPadding?: boolean; collapsible?: boolean }>(), { bordered: true, noPadding: false })
const shadow = computed(() => (props.bordered ? 'never' : 'hover') as any)
const bodyStyle = computed(() => (props.noPadding ? { padding: 0 } : undefined))
</script>
<template>
  <el-card :shadow="shadow" :body-style="bodyStyle">
    <template #header v-if="title || $slots.title || $slots.actions">
      <div class="d-flex align-items-center justify-content-between">
        <span class="fw-bold">{{ title }}<slot name="title"></slot></span>
        <div><slot name="actions"></slot></div>
      </div>
    </template>
    <slot></slot>
    <template #footer v-if="$slots.footer"><slot name="footer"></slot></template>
  </el-card>
</template>
