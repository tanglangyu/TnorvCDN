<script setup lang="ts">
/** 边缘脚本 —— expr-lang 表达式（本站维度编辑，存 Security.EdgeScripts） */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const list = ref<any[]>([])
watch(() => props.server, (s) => {
  if (s) list.value = JSON.parse(JSON.stringify(s.security?.edgeScripts || []))
}, { immediate: true })

const add = () => list.value.push({ name: '', enabled: true, expr: '' })
const rm = (i: number) => list.value.splice(i, 1)

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.edgeScripts = list.value.filter((x) => x.name && x.expr)
  saving.value = true
  try {
    await cdnApi.put('/admin/servers/' + props.serverId + '/security', sec)
    ElMessage.success('边缘脚本已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">边缘脚本</h5>
    <el-alert type="info" :closable="false" class="mb-3" title="expr-lang 表达式，在安全链之前执行；可短路响应" />
    <el-card v-for="(s, i) in list" :key="i" shadow="never" class="mb-2" style="max-width: 860px">
      <div class="d-flex gap-2 mb-2 align-items-center">
        <el-input v-model="s.name" placeholder="脚本名" size="small" style="width: 200px" />
        <el-switch v-model="s.enabled" size="small" />
        <el-button size="small" type="danger" text @click="rm(i)">删除</el-button>
      </div>
      <el-input v-model="s.expr" type="textarea" :rows="3" placeholder='如：Host == "a.com" && Path.HasPrefix("/admin") => respond(403, "denied")' style="max-width: 860px" />
    </el-card>
    <el-button size="small" class="mb-3" @click="add">+ 添加脚本</el-button>
    <div><el-button type="primary" :loading="saving" @click="save">保存</el-button></div>
  </div>
</template>
