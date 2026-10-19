<script setup lang="ts" generic="T">
import { computed, ref, watch } from 'vue'
import type { TableColumn, PageQuery } from './types'
import { formatTime } from '@/utils/time'

const props = withDefaults(
  defineProps<{
    /** 列配置 */
    columns: TableColumn[]
    /** 数据(已分页或前端分页) */
    data?: T[]
    /** 加载中 */
    loading?: boolean
    /** 总记录数(服务端分页时传入) */
    total?: number
    /** 当前页(v-model) */
    pageIndex?: number
    /** 每页条数(v-model) */
    pageSize?: number
    /** 是否前端分页(false=服务端分页,需配合 @change) */
    clientPaged?: boolean
    /** 行 key 字段 */
    rowKey?: string
    /** 是否显示选择列 */
    selection?: boolean
    /** 空数据提示 */
    emptyText?: string
    /** 条纹 */
    striped?: boolean
    /** 边框 */
    bordered?: boolean
    /** 紧凑 */
    compact?: boolean
    /** 悬浮 */
    hover?: boolean
  }>(),
  {
    loading: false,
    pageIndex: 1,
    pageSize: 10,
    clientPaged: false,
    rowKey: 'id',
    selection: false,
    emptyText: '暂无数据',
    striped: true,
    bordered: false,
    compact: false,
    hover: true,
  },
)

const emit = defineEmits<{
  (e: 'update:pageIndex', v: number): void
  (e: 'update:pageSize', v: number): void
  (e: 'change', query: PageQuery): void
  (e: 'selection-change', rows: T[]): void
  (e: 'sort', col: TableColumn, dir: 'asc' | 'desc'): void
}>()

const innerPageIndex = ref(props.pageIndex)
const innerPageSize = ref(props.pageSize)
const sortColumn = ref<string>('')
const sortDir = ref<'asc' | 'desc'>('asc')

watch(() => props.pageIndex, (v) => (innerPageIndex.value = v))
watch(() => props.pageSize, (v) => (innerPageSize.value = v))

// 前端分页数据切片
const displayData = computed(() => {
  if (!props.data) return []
  if (!props.clientPaged || props.total !== undefined) return props.data
  const start = (innerPageIndex.value - 1) * innerPageSize.value
  return props.data.slice(start, start + innerPageSize.value)
})

const totalRecords = computed(() => (props.total !== undefined ? props.total : props.data?.length || 0))

function onPageChange(p: number) {
  innerPageIndex.value = p
  emit('update:pageIndex', p)
  emit('change', { pageIndex: p, pageSize: innerPageSize.value })
}

function onSortChange({ prop, order }: { prop?: string; order?: string | null }) {
  if (!order) {
    sortColumn.value = ''
    emit('sort', { prop: '' } as TableColumn, 'asc')
    return
  }
  sortColumn.value = prop || ''
  sortDir.value = order === 'ascending' ? 'asc' : 'desc'
  const col = props.columns.find((c) => c.prop === prop) || ({ prop } as TableColumn)
  emit('sort', col, sortDir.value)
}

function onSelectionChange(rows: any[]) {
  emit('selection-change', rows as T[])
}

function cellValue(row: any, col: TableColumn) {
  const v = row[col.prop]
  if (col.formatter) return col.formatter(row, col, v)
  // 时间列自动格式化:列名以 Time/At/Date 结尾且值为 ISO 字符串 → 按浏览器时区显示
  if (v && /(Time|At|Date)$/.test(col.prop) && typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v)) {
    return formatTime(v)
  }
  return v === null || v === undefined ? '' : String(v)
}
</script>

<template>
  <div class="t-table">
    <el-table
      :data="displayData"
      v-loading="loading"
      :row-key="rowKey"
      :border="bordered"
      :size="compact ? 'small' : 'default'"
      :empty-text="emptyText"
      :stripe="striped"
      :highlight-current-row="hover"
      @selection-change="onSelectionChange"
      @sort-change="onSortChange"
    >
      <el-table-column v-if="selection" type="selection" width="42" />
      <el-table-column
        v-for="col in columns"
        :key="col.prop"
        :prop="col.prop"
        :label="col.label"
        :width="col.width"
        :min-width="col.minWidth"
        :fixed="col.fixed"
        :sortable="col.sortable ? 'custom' : false"
        :align="(col.align as any) || 'left'"
        :show-overflow-tooltip="true"
      >
        <template #default="{ row, $index }">
          <slot v-if="col.slot" :name="`body-${col.slot}`" :row="row" :column="col" :index="$index">{{ cellValue(row, col) }}</slot>
          <template v-else>{{ cellValue(row, col) }}</template>
        </template>
      </el-table-column>
      <el-table-column v-if="$slots.action" label="操作" width="160" fixed="right">
        <template #default="{ row, $index }">
          <slot name="action" :row="row" :index="$index"></slot>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination
      v-if="totalRecords > 0"
      class="mt-3"
      style="justify-content: flex-end"
      :current-page="innerPageIndex"
      :page-size="innerPageSize"
      :total="totalRecords"
      layout="total, prev, pager, next"
      background
      @current-change="onPageChange"
    />
  </div>
</template>
