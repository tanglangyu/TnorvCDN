<script setup lang="ts">
/** 分组 —— 站点分组标签（用户端特有） */
import { ref, watch } from 'vue'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'save', patch: any): void }>()

const group = ref('')
watch(() => props.server, (s) => { if (s) group.value = '' }, { immediate: true })

const saving = ref(false)
const save = async () => {
  saving.value = true
  emit('save', { __group__: group.value })
  saving.value = false
}
</script>

<template>
  <div>
    <h5 class="mb-3">站点分组</h5>
    <el-form label-width="120px" style="max-width: 520px">
      <el-form-item label="分组标签">
        <el-input v-model="group" placeholder="如 生产环境 / 活动站点（用于列表筛选）" />
      </el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
