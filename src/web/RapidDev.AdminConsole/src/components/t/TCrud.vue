<script setup lang="ts" generic="T">
/**
 * TCrud —— 增删改查页一体化组件。
 * 一行声明式配置即可渲染:搜索表单 + 表格 + 新增/编辑弹窗 + 删除确认。
 *
 * 用法:
 * <TCrud
 *   :columns="columns"
 *   :search-schemas="searchSchemas"
 *   :form-schemas="formSchemas"
 *   :fetch="api.list"          // (query) => PagedResult<T>
 *   :create="api.add"          // (dto) => id
 *   :update="api.edit"         // (id, dto) => void
 *   :remove="api.del"          // (id) => void
 * />
 */
import { ref, reactive, onMounted } from 'vue'
import { ElMessageBox } from 'element-plus'
import type { TableColumn, FormItemSchema, PageQuery } from './types'
import TTable from './TTable.vue'
import TSearchForm from './TSearchForm.vue'
import TForm from './TForm.vue'
import TButton from './TButton.vue'
import TModal from './TModal.vue'
import { http } from '@/utils/request'

const props = withDefaults(
  defineProps<{
    /** 表格列 */
    columns: TableColumn[]
    /** 搜索项 */
    searchSchemas?: FormItemSchema[]
    /** 表单项 */
    formSchemas: FormItemSchema[]
    /** 列表接口 (query) => PagedResult */
    fetch: (query: PageQuery) => Promise<any>
    /** 新增接口 */
    create?: (dto: any) => Promise<any>
    /** 详情接口(编辑回填,可选) */
    detail?: (id: number) => Promise<any>
    /** 编辑接口 */
    update?: (id: number, dto: any) => Promise<any>
    /** 删除接口 */
    remove?: (id: number) => Promise<any>
    /** 权限前缀,如 system:user。自动拼成 list/add/edit/delete 按钮 */
    permissionPrefix?: string
    /** 行主键字段 */
    rowKey?: string
    /** 是否显示操作列 */
    showAction?: boolean
    /** 自定义标题:新增/编辑 */
    addTitle?: string
    editTitle?: string
  }>(),
  { rowKey: 'id', showAction: true, addTitle: '新增', editTitle: '编辑' },
)

const emit = defineEmits<{
  (e: 'add'): void
  (e: 'edit', row: T): void
  (e: 'after-submit'): void
}>()

const loading = ref(false)
const tableData = ref<T[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(10)
const searchQuery = reactive<Record<string, any>>({})
/** 当前过滤条件(AdvancedQuery.filters) */
const currentFilters = ref<any[]>([])
/** 当前排序(AdvancedQuery.sorts) */
const currentSorts = ref<any[]>([])

const modalVisible = ref(false)
const modalTitle = ref(props.addTitle)
const isEdit = ref(false)
const formModel = reactive<Record<string, any>>({})
const submitting = ref(false)
const formRef = ref()

async function loadData() {
  loading.value = true
  try {
    const query: PageQuery = {
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      filters: currentFilters.value,
      sorts: currentSorts.value,
      ...searchQuery,
    }
    const res = await props.fetch(query)
    tableData.value = res.list || res.data || []
    total.value = res.total ?? 0
  } catch (e) {
    console.error(e)
  } finally {
    loading.value = false
  }
}

function onSearch(q: Record<string, any>) {
  // 支持两种格式:旧式扁平字段 和 新式 filters 数组
  if (q.filters && Array.isArray(q.filters)) {
    currentFilters.value = q.filters
    // 同时把扁平字段存入 searchQuery(兼容后端 [FromQuery] 绑定)
    const flat: Record<string, any> = { ...q }
    delete flat.filters
    delete flat.sorts
    Object.keys(searchQuery).forEach((k) => delete searchQuery[k])
    Object.assign(searchQuery, flat)
  } else {
    Object.keys(searchQuery).forEach((k) => delete searchQuery[k])
    Object.assign(searchQuery, q)
  }
  pageIndex.value = 1
  loadData()
}
function onReset() {
  Object.keys(searchQuery).forEach((k) => (searchQuery[k] = undefined))
  currentFilters.value = []
  pageIndex.value = 1
}
function onPageChange(p: PageQuery) {
  pageIndex.value = p.pageIndex
  pageSize.value = p.pageSize
  loadData()
}
/** TTable 排序变化 */
function onSortChange(col: any, dir: 'asc' | 'desc') {
  if (dir && col.prop) {
    currentSorts.value = [{ field: col.prop, direction: dir }]
  } else {
    currentSorts.value = []
  }
  loadData()
}

function openAdd() {
  emit('add')
  isEdit.value = false
  modalTitle.value = props.addTitle
  Object.keys(formModel).forEach((k) => delete formModel[k])
  props.formSchemas.forEach((s) => (formModel[s.prop] = s.defaultValue ?? ''))
  modalVisible.value = true
}

async function openEdit(row: any) {
  emit('edit', row)
  isEdit.value = true
  modalTitle.value = props.editTitle
  let data = row
  if (props.detail) {
    try { data = await props.detail(row[props.rowKey]) } catch {}
  }
  Object.keys(formModel).forEach((k) => delete formModel[k])
  Object.assign(formModel, data)
  modalVisible.value = true
}

async function submit() {
  const valid = formRef.value?.submit()
  if (valid && valid.valid === false) return
  submitting.value = true
  try {
    if (isEdit.value && props.update) {
      await props.update(formModel[props.rowKey], { ...formModel })
    } else if (props.create) {
      await props.create({ ...formModel })
    }
    modalVisible.value = false
    emit('after-submit')
    loadData()
  } catch (e) {
    console.error(e)
  } finally {
    submitting.value = false
  }
}

async function handleDelete(row: any) {
  if (!props.remove) return
  try {
    await ElMessageBox.confirm('确定删除该记录?', '提示', { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
  } catch {
    return
  }
  try {
    await props.remove(row[props.rowKey])
    loadData()
  } catch (e) { console.error(e) }
}

onMounted(loadData)
defineExpose({ refresh: loadData })
</script>

<template>
  <div class="t-crud">
    <TSearchForm v-if="searchSchemas?.length" :schemas="searchSchemas" :advanced="true" @search="onSearch" @reset="onReset" />

    <el-card shadow="never" :body-style="{ padding: 0 }">
      <template #header>
        <div class="d-flex align-items-center justify-content-between">
          <span class="fw-bold"><slot name="title">数据列表</slot></span>
          <div class="d-flex gap-2 align-items-center">
            <TButton v-if="create" variant="primary" @click="openAdd"><i class="ti ti-plus me-1"></i>{{ addTitle }}</TButton>
            <slot name="toolbar"></slot>
          </div>
        </div>
      </template>
      <TTable :columns="columns" :data="tableData" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" :row-key="rowKey" @change="onPageChange" @sort="onSortChange">
        <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps" /></template>
        <template #action="{ row }" v-if="showAction && (update || remove)">
          <TButton v-if="update" variant="ghost-primary" size="sm" @click="openEdit(row)"><i class="ti ti-edit"></i>编辑</TButton>
          <TButton v-if="remove" variant="ghost-danger" size="sm" @click="handleDelete(row)"><i class="ti ti-trash"></i>删除</TButton>
        </template>
      </TTable>
    </el-card>

    <TModal v-model="modalVisible" :title="modalTitle" size="lg">
      <TForm ref="formRef" v-model="formModel" :schemas="formSchemas" />
      <template #footer>
        <TButton variant="ghost-secondary" @click="modalVisible = false">取消</TButton>
        <TButton variant="primary" :loading="submitting" @click="submit">确定</TButton>
      </template>
    </TModal>
  </div>
</template>
