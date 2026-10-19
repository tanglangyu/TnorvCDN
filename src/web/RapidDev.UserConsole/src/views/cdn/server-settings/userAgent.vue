<script setup lang="ts">
/** UA 名单 —— 通配符允许/拒绝规则 */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const rules = ref<any[]>([])
watch(() => props.server, (s) => {
  if (s) rules.value = JSON.parse(JSON.stringify(s.security?.uaRules || []))
}, { immediate: true })

const add = () => rules.value.push({ keyword: '', allow: false })
const rm = (i: number) => rules.value.splice(i, 1)

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.uaRules = rules.value.filter((r) => r.keyword)
  saving.value = true
  try {
    await cdnApi.put('/user/servers/' + props.serverId + '/security', sec)
    ElMessage.success('UA 名单已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">UA 名单</h5>
    <el-table :data="rules" size="small" stripe class="mb-2" style="max-width: 680px">
      <el-table-column label="UA 关键词（支持 * 通配）" min-width="240"><template #default="{ row }"><el-input v-model="row.keyword" size="small" placeholder="如 curl* / *python*" /></template></el-table-column>
      <el-table-column label="动作" width="130">
        <template #default="{ row }">
          <el-select v-model="row.allow" size="small"><el-option :value="false" label="拒绝" /><el-option :value="true" label="允许" /></el-select>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="80"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rm($index)">删除</el-button></template></el-table-column>
    </el-table>
    <el-button size="small" class="mb-3" @click="add">+ 添加规则</el-button>
    <div><el-button type="primary" :loading="saving" @click="save">保存</el-button></div>
  </div>
</template>
