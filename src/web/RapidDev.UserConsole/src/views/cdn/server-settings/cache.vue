<script setup lang="ts">
/** 缓存 —— 缓存开关 + 规则列表（扩展名/路径/首页/全部） */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const form = ref<any>({ enabled: false, rules: [] })
watch(() => props.server, (s) => {
  if (!s) return
  const c = JSON.parse(JSON.stringify(s.cache || {}))
  c.rules = c.rules || []
  form.value = c
}, { immediate: true })

const addRule = () => form.value.rules.push({ matchType: 'extension', value: 'jpg,png,css,js', ttlSeconds: 3600, clientTtlSeconds: 0, ignoreQueryString: false })
const rm = (i: number) => form.value.rules.splice(i, 1)

const saving = ref(false)
const save = async () => {
  saving.value = true
  try {
    await cdnApi.put('/user/servers/' + props.serverId + '/cache', form.value)
    ElMessage.success('缓存配置已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">缓存配置</h5>
    <el-form label-width="120px" style="max-width: 640px">
      <el-form-item label="启用缓存"><el-switch v-model="form.enabled" /></el-form-item>
    </el-form>
    <el-table :data="form.rules" size="small" stripe class="mb-2" style="max-width: 980px">
      <el-table-column label="匹配类型" width="140">
        <template #default="{ row }">
          <el-select v-model="row.matchType" size="small">
            <el-option value="extension" label="文件扩展名" />
            <el-option value="path-prefix" label="路径前缀" />
            <el-option value="path-exact" label="精确路径" />
            <el-option value="homepage" label="首页" />
            <el-option value="all" label="全部" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column label="匹配值" min-width="160"><template #default="{ row }"><el-input v-model="row.value" size="small" :disabled="row.matchType === 'homepage' || row.matchType === 'all'" placeholder="jpg,png 或 /static/" /></template></el-table-column>
      <el-table-column label="缓存TTL(s)" width="130"><template #default="{ row }"><el-input-number v-model="row.ttlSeconds" size="small" :min="1" controls-position="right" /></template></el-table-column>
      <el-table-column label="浏览器TTL(s)" width="140"><template #default="{ row }"><el-input-number v-model="row.clientTtlSeconds" size="small" :min="0" controls-position="right" /></template></el-table-column>
      <el-table-column label="忽略参数" width="90"><template #default="{ row }"><el-switch v-model="row.ignoreQueryString" size="small" /></template></el-table-column>
      <el-table-column label="操作" width="80"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rm($index)">删除</el-button></template></el-table-column>
    </el-table>
    <el-button size="small" class="mb-3" @click="addRule">+ 添加缓存规则</el-button>
    <div><el-button type="primary" :loading="saving" @click="save">保存</el-button></div>
  </div>
</template>
