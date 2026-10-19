<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ total: number; pageIndex: number; pageSize: number; totalText?: boolean }>()
const emit = defineEmits<{ (e: 'update:pageIndex', v: number): void; (e: 'change', v: { pageIndex: number; pageSize: number }): void }>()

const current = computed(() => props.pageIndex)

function change(p: number) {
  if (p === props.pageIndex) return
  emit('update:pageIndex', p)
  emit('change', { pageIndex: p, pageSize: props.pageSize })
}
</script>
<template>
  <div class="d-flex align-items-center justify-content-end flex-wrap gap-2 mt-3">
    <el-pagination
      :current-page="current"
      :page-size="pageSize"
      :total="total"
      layout="total, prev, pager, next"
      background
      @current-change="change"
    />
  </div>
</template>
