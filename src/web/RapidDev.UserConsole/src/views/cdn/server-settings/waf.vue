<script setup lang="ts">
/** WAF —— 通用规则引擎（条件-操作符-动作） */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const rules = ref<any[]>([])
watch(() => props.server, (s) => {
  if (!s) return
  rules.value = JSON.parse(JSON.stringify(s.security?.wafRules || []))
}, { immediate: true })

const operators = ['eq','ne','contains','not-contains','prefix','suffix','regex','in','not-in','gt','lt','gte','lte','exists','not-exists','in-cidr','not-in-cidr','empty','not-empty']
const actions = ['block','allow','redirect','log','captcha']

const addRule = () => rules.value.push({ name: '', enabled: true, action: 'block', actionValue: '', priority: 100, conditions: [{ field: 'uri', operator: 'contains', value: '', negate: false }] })
const addCond = (r: any) => r.conditions.push({ field: 'uri', operator: 'contains', value: '', negate: false })
const rm = (arr: any[], i: number) => arr.splice(i, 1)

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.wafRules = rules.value.filter((r) => r.name)
  saving.value = true
  try {
    await cdnApi.put('/user/servers/' + props.serverId + '/security', sec)
    ElMessage.success('WAF 规则已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">WAF 规则</h5>
    <el-alert type="info" :closable="false" class="mb-3" title="条件全部满足时执行动作；优先级数字越小越先匹配" />
    <el-card v-for="(r, i) in rules" :key="i" shadow="never" class="mb-3" style="max-width: 980px">
      <div class="d-flex gap-2 mb-2 align-items-center">
        <el-input v-model="r.name" placeholder="规则名" style="width: 200px" size="small" />
        <el-select v-model="r.action" size="small" style="width: 110px">
          <el-option v-for="a in actions" :key="a" :value="a" :label="a" />
        </el-select>
        <el-input v-if="r.action === 'redirect'" v-model="r.actionValue" placeholder="跳转 URL" size="small" style="width: 240px" />
        <el-input-number v-model="r.priority" size="small" :min="1" :max="999" controls-position="right" style="width: 110px" />
        <el-switch v-model="r.enabled" size="small" />
        <el-button size="small" type="danger" text @click="rm(rules, i)">删除规则</el-button>
      </div>
      <el-table :data="r.conditions" size="small" stripe>
        <el-table-column label="字段" width="150">
          <template #default="{ row }">
            <el-select v-model="row.field" size="small">
              <el-option v-for="f in ['uri','ip','user_agent','referer','method','host','header_X','query_X','body_length']" :key="f" :value="f" :label="f" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="操作符" width="160">
          <template #default="{ row }">
            <el-select v-model="row.operator" size="small" filterable>
              <el-option v-for="o in operators" :key="o" :value="o" :label="o" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="值" min-width="180"><template #default="{ row }"><el-input v-model="row.value" size="small" /></template></el-table-column>
        <el-table-column label="取反" width="70"><template #default="{ row }"><el-switch v-model="row.negate" size="small" /></template></el-table-column>
        <el-table-column label="操作" width="70"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rm(r.conditions, $index)">删</el-button></template></el-table-column>
      </el-table>
      <el-button size="small" class="mt-2" @click="addCond(r)">+ 添加条件</el-button>
    </el-card>
    <el-button size="small" class="mb-3" @click="addRule">+ 添加规则</el-button>
    <div><el-button type="primary" :loading="saving" @click="save">保存</el-button></div>
  </div>
</template>
