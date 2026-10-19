<script setup lang="ts">
import { reactive, ref } from 'vue'
import type { FormItemSchema, PageQuery } from './types'
import TInput from './TInput.vue'
import TSelect from './TSelect.vue'
import TDatePicker from './TDatePicker.vue'
import TButton from './TButton.vue'

/**
 * 增强搜索表单。
 * - 每个字段可声明 op(默认 like);高级模式下用户可改操作符。
 * - 支持 between(范围,双值)、in/notin(多选/逗号分隔)、null/notnull 等全部操作符。
 * - 高级模式下每个条件前可选"且/或"连接符(首条无),实现 AND/OR 组合。
 * - schema.field 可指定后端 PascalCase 字段名,不填用 prop。
 */
const props = defineProps<{
  schemas: FormItemSchema[]
  /** 高级模式:显示操作符 + 连接符 */
  advanced?: boolean
}>()
const emit = defineEmits<{
  (e: 'search', query: PageQuery): void
  (e: 'reset'): void
}>()

const query = reactive<Record<string, any>>({})
/** 每个字段的操作符(高级模式下可改) */
const opMap = reactive<Record<string, string>>({})
/** 每个字段与上一条的连接符 and/or(首条忽略) */
const logicArr = ref<string[]>([])

props.schemas.forEach((s) => {
  query[s.prop] = s.defaultValue ?? ''
  // select 默认 eq(精确),其余默认 like
  opMap[s.prop] = s.props?.op || (s.type === 'select' ? 'eq' : 'like')
})
logicArr.value = props.schemas.map(() => 'and')

/** 是否展开高级筛选 */
const showAdvanced = ref(false)

const opOptions = [
  { label: '包含', value: 'like' },
  { label: '等于', value: 'eq' },
  { label: '不等于', value: 'neq' },
  { label: '开头是', value: 'll' },
  { label: '结尾是', value: 'rl' },
  { label: '大于', value: 'gt' },
  { label: '大于等于', value: 'gte' },
  { label: '小于', value: 'lt' },
  { label: '小于等于', value: 'lte' },
  { label: '范围', value: 'between' },
  { label: '多选', value: 'in' },
  { label: '排除多选', value: 'notin' },
  { label: '为空', value: 'null' },
  { label: '不为空', value: 'notnull' },
]

const logicOptions = [
  { label: '且', value: 'and' },
  { label: '或', value: 'or' },
]

/** 当前 op 是否需要双值输入(between) */
function isBetween(s: FormItemSchema) { return opMap[s.prop] === 'between' }
/** 当前 op 是否多值(in/notin) */
function isMulti(s: FormItemSchema) { return opMap[s.prop] === 'in' || opMap[s.prop] === 'notin' }
/** in/notin 多选切换:保证 query[prop] 为数组,勾选 push/取消 splice */
function toggleMulti(prop: string, val: any) {
  if (!Array.isArray(query[prop])) query[prop] = []
  const i = query[prop].indexOf(val)
  if (i >= 0) query[prop].splice(i, 1)
  else query[prop].push(val)
}
/** 当前 op 是否无值(null/notnull) */
function isNoValue(s: FormItemSchema) { const op = opMap[s.prop]; return op === 'null' || op === 'notnull' }

function search() {
  const result: PageQuery = { pageIndex: 1, pageSize: 10 }
  const filters: any[] = []
  props.schemas.forEach((s, idx) => {
    const field = s.field || s.prop
    const op = opMap[s.prop] || 'like'
    const logic = logicArr.value[idx] || 'and'
    if (op === 'null' || op === 'notnull') {
      filters.push({ field, op, logic })
      return
    }
    if (op === 'between') {
      const v1 = query[s.prop]; const v2 = query[s.prop + '__2']
      if (v1 !== '' && v1 != null && v2 !== '' && v2 != null)
        filters.push({ field, op, value: v1, value2: v2, logic })
      return
    }
    if (op === 'in' || op === 'notin') {
      // checkbox 组返回数组,input 兜底逗号字符串。统一转逗号字符串提交(后端 ToList 按逗号 split)
      let v = query[s.prop]
      if (Array.isArray(v)) v = v.join(',')
      if (typeof v === 'string' && v.trim()) filters.push({ field, op, value: v, logic })
      return
    }
    const val = query[s.prop]
    if (val !== '' && val !== null && val !== undefined) filters.push({ field, op, value: val, logic })
  })
  result.filters = filters
  emit('search', result)
}

function reset() {
  props.schemas.forEach((s) => {
    query[s.prop] = s.defaultValue ?? ''
    query[s.prop + '__2'] = ''
    opMap[s.prop] = s.props?.op || (s.type === 'select' ? 'eq' : 'like')
  })
  logicArr.value = props.schemas.map(() => 'and')
  emit('reset')
  emit('search', { pageIndex: 1, pageSize: 10, filters: [] })
}
</script>

<template>
  <el-card class="mb-3" shadow="never">
    <el-row :gutter="16" align="bottom">
      <el-col
        v-for="(s, idx) in schemas"
        :key="s.prop"
        :xs="24"
        :md="12"
        :lg="advanced && showAdvanced ? (isBetween(s) ? 24 : 12) : 8"
      >
        <div class="form-label mb-1">{{ s.label }}</div>
        <div class="d-flex gap-1 align-items-center" style="min-width: 0">
          <!-- 连接符(高级模式,首条无) -->
          <TSelect
            v-if="advanced && showAdvanced && idx > 0"
            v-model="logicArr[idx]"
            :options="logicOptions"
            style="width: 80px; flex-shrink: 0"
          />
          <!-- 操作符(高级模式) -->
          <TSelect
            v-if="advanced && showAdvanced"
            v-model="opMap[s.prop]"
            :options="opOptions"
            style="width: 150px; flex-shrink: 0"
          />
          <!-- 控件区:min-width:0 防 flex 子项溢出重叠 -->
          <span v-if="isNoValue(s)" class="text-muted" style="font-size: 13px">无需输入值</span>
          <!-- between:双输入/双日期 -->
          <template v-else-if="isBetween(s)">
            <template v-if="s.type === 'date' || s.type === 'datetime'">
              <TDatePicker :type="s.type === 'datetime' ? 'datetime-local' : 'date'" v-model="query[s.prop]" :placeholder="'开始'" style="flex: 1; min-width: 0" />
              <span class="text-muted">~</span>
              <TDatePicker :type="s.type === 'datetime' ? 'datetime-local' : 'date'" v-model="query[s.prop + '__2']" :placeholder="'结束'" style="flex: 1; min-width: 0" />
            </template>
            <template v-else>
              <TInput v-model="query[s.prop]" :placeholder="'下限'" style="flex: 1; min-width: 0" />
              <span class="text-muted">~</span>
              <TInput v-model="query[s.prop + '__2']" :placeholder="'上限'" style="flex: 1; min-width: 0" />
            </template>
          </template>
          <!-- in/notin:select 渲染为水平 checkbox 组;input 用逗号分隔 -->
          <template v-else-if="isMulti(s)">
            <el-checkbox-group
              v-if="s.type === 'select'"
              :model-value="Array.isArray(query[s.prop]) ? query[s.prop] : []"
              @update:model-value="(v: any) => (query[s.prop] = v)"
              style="flex: 1; min-width: 0"
            >
              <el-checkbox v-for="opt in s.options" :key="opt.value" :value="opt.value">{{ opt.label }}</el-checkbox>
            </el-checkbox-group>
            <TInput v-else v-model="query[s.prop]" :placeholder="'多值,逗号分隔'" style="flex: 1; min-width: 0" />
          </template>
          <!-- 普通单值 -->
          <template v-else>
            <TInput v-if="!s.type || s.type === 'input'" v-model="query[s.prop]" :placeholder="s.placeholder" style="flex: 1; min-width: 0" />
            <TSelect
              v-else-if="s.type === 'select'"
              v-model="query[s.prop]"
              :options="s.options"
              :placeholder="s.placeholder"
              style="flex: 1; min-width: 0"
            />
            <TDatePicker
              v-else-if="s.type === 'date' || s.type === 'datetime'"
              :type="s.type === 'datetime' ? 'datetime-local' : 'date'"
              v-model="query[s.prop]"
              :placeholder="s.placeholder"
              style="flex: 1; min-width: 0"
            />
          </template>
        </div>
      </el-col>
      <el-col :xs="24" :md="12" :lg="6" class="mt-2 mt-md-0">
        <div class="d-flex gap-2">
          <TButton variant="primary" @click="search"><i class="ti ti-search me-1"></i>查询</TButton>
          <TButton variant="ghost-secondary" @click="reset"><i class="ti ti-refresh me-1"></i>重置</TButton>
          <TButton v-if="advanced" variant="ghost-secondary" size="sm" @click="showAdvanced = !showAdvanced">
            <i class="ti" :class="showAdvanced ? 'ti-chevron-up' : 'ti-chevron-down'"></i>
            {{ showAdvanced ? '收起高级' : '高级筛选' }}
          </TButton>
        </div>
      </el-col>
    </el-row>
  </el-card>
</template>
