<script setup lang="ts">
defineProps<{ title?: string; description?: string; showBack?: boolean }>()
const emit = defineEmits<{ (e: 'back'): void }>()
</script>
<template>
  <div class="d-flex align-items-center justify-content-between mb-3">
    <div>
      <div class="text-muted" style="font-size: 12px" v-if="$slots.pretitle"><slot name="pretitle"></slot></div>
      <div class="d-flex align-items-center gap-2">
        <el-button v-if="showBack" text circle @click="emit('back')"><i class="ti ti-arrow-left"></i></el-button>
        <h2 class="m-0">{{ title }}<slot name="title"></slot></h2>
      </div>
      <div class="text-muted mt-1" style="font-size: 13px" v-if="description || $slots.description">{{ description }}<slot name="description"></slot></div>
    </div>
    <div><slot name="actions"></slot></div>
  </div>
</template>
