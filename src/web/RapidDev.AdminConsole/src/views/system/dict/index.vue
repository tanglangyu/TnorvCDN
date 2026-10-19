<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { dictApi } from '@/api/system'
import { TCard, TButton, TModal, TForm, TTable, TTag, TInput, TSelect, TSwitch } from '@/components/t'
import type { FormItemSchema, TableColumn } from '@/components/t/types'
import type { SysDictType, SysDictData } from '@/types'

const typeList = ref<SysDictType[]>([])
const typeLoading = ref(false)

// 字典类型
const typeModalVisible = ref(false)
const typeIsEdit = ref(false)
const typeModel = ref<any>({ status: 0 })
const typeFormSchemas: FormItemSchema[] = [
  { prop: 'dictName', label: '字典名称', required: true, span: 12 },
  { prop: 'dictType', label: '字典类型', required: true, span: 12 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }] },
  { prop: 'remark', label: '备注', type: 'textarea', span: 12 },
]

// 数据子项
const selectedType = ref<SysDictType | null>(null)
const dataList = ref<SysDictData[]>([])
const dataLoading = ref(false)
const dataModalVisible = ref(false)
const dataIsEdit = ref(false)
const dataModel = ref<any>({ status: 1, sort: 0, isDefault: false })
const dataFormSchemas: FormItemSchema[] = [
  { prop: 'dictLabel', label: '字典标签', required: true, span: 6 },
  { prop: 'dictValue', label: '字典键值', required: true, span: 6 },
  { prop: 'cssClass', label: '样式', span: 6, placeholder: 'success/danger/warning/primary' },
  { prop: 'sort', label: '排序', type: 'number', defaultValue: 0, span: 6 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 1, span: 6, options: [{ label: '启用', value: 1 }, { label: '停用', value: 0 }] },
  { prop: 'remark', label: '备注', type: 'textarea', span: 12 },
]
const dataColumns: TableColumn[] = [
  { prop: 'dictLabel', label: '字典标签', minWidth: 120 },
  { prop: 'dictValue', label: '字典键值', minWidth: 120 },
  { prop: 'sort', label: '排序', width: 70, align: 'center' },
  { prop: 'status', label: '状态', width: 70, slot: 'status', align: 'center' },
]

async function loadTypes() {
  typeLoading.value = true
  try {
    const r: any = await dictApi.typeList({ pageIndex: 1, pageSize: 100 })
    typeList.value = Array.isArray(r) ? r : (r?.list ?? [])
  } finally { typeLoading.value = false }
}
function selectType(type: SysDictType) {
  if (selectedType.value?.id === type.id) return
  selectedType.value = type
  loadDataList()
}
async function loadDataList() {
  if (!selectedType.value) return
  dataLoading.value = true
  try {
    const r: any = await dictApi.dataList(selectedType.value.dictType)
    dataList.value = Array.isArray(r) ? r : (r?.list ?? r?.data ?? [])
  } finally { dataLoading.value = false }
}

// 类型 CRUD
function openTypeAdd() { typeIsEdit.value = false; typeModel.value = { status: 0 }; typeModalVisible.value = true }
function openTypeEdit(row: any) { typeIsEdit.value = true; typeModel.value = { ...row }; typeModalVisible.value = true }
async function saveType() {
  if (typeIsEdit.value) await dictApi.editType(typeModel.value.id, typeModel.value)
  else await dictApi.addType(typeModel.value)
  typeModalVisible.value = false; loadTypes()
}
async function delType(row: SysDictType) {
  if (!confirm('确定删除字典类型及其数据?')) return
  await dictApi.removeType(row.id); loadTypes()
  if (selectedType.value?.id === row.id) { selectedType.value = null; dataList.value = [] }
}

// 数据 CRUD
function openDataAdd() { dataIsEdit.value = false; dataModel.value = { status: 1, sort: 0 }; dataModalVisible.value = true }
function openDataEdit(row: any) { dataIsEdit.value = true; dataModel.value = { ...row }; dataModalVisible.value = true }
async function saveData() {
  if (dataIsEdit.value) await dictApi.editData(dataModel.value.id, dataModel.value)
  else { dataModel.value.dictType = selectedType.value!.dictType; await dictApi.addData(dataModel.value) }
  dataModalVisible.value = false; loadDataList()
}
async function delData(row: SysDictData) {
  if (!confirm('确定删除?')) return
  await dictApi.removeData(row.id); loadDataList()
}

onMounted(loadTypes)
</script>

<template>
  <div class="row g-3">
    <!-- 左侧:字典类型 -->
    <div class="col-lg-5">
      <TCard title="字典类型">
        <template #actions>
          <TButton variant="primary" size="sm" @click="openTypeAdd"><i class="ti ti-plus me-1"></i>新增</TButton>
          <TButton variant="ghost-secondary" size="sm" @click="loadTypes"><i class="ti ti-refresh"></i></TButton>
        </template>
        <div class="table-responsive">
          <table class="table table-vcenter card-table table-hover table-sm">
            <thead><tr><th>名称</th><th>类型</th><th>状态</th><th>操作</th></tr></thead>
            <tbody>
              <tr v-if="typeLoading"><td colspan="4" class="text-center py-3 text-muted">加载中...</td></tr>
              <tr v-else-if="!typeList.length"><td colspan="4" class="text-center py-3 text-muted">暂无数据</td></tr>
              <tr
                v-for="t in typeList" :key="t.id"
                @click="selectType(t)"
                class="cursor-pointer"
                :class="{ 'table-active bg-primary-lt': selectedType?.id === t.id }"
                style="cursor: pointer"
              >
                <td class="fw-medium">{{ t.dictName }}</td>
                <td><TTag variant="primary">{{ t.dictType }}</TTag></td>
                <td>
                  <span class="badge" :class="t.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ t.status === 0 ? '启用' : '停用' }}</span>
                </td>
                <td>
                  <button class="btn btn-ghost-primary btn-sm" @click.stop="openTypeEdit(t)"><i class="ti ti-edit"></i></button>
                  <button class="btn btn-ghost-danger btn-sm" @click.stop="delType(t)"><i class="ti ti-trash"></i></button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </TCard>
    </div>

    <!-- 右侧:字典数据 -->
    <div class="col-lg-7">
      <TCard>
        <template #title>
          <span v-if="selectedType">字典数据 — {{ selectedType.dictType }}</span>
          <span v-else class="text-muted">选择左侧字典类型查看数据</span>
        </template>
        <template #actions v-if="selectedType">
          <TButton variant="primary" size="sm" @click="openDataAdd"><i class="ti ti-plus me-1"></i>新增数据</TButton>
          <TButton variant="ghost-secondary" size="sm" @click="loadDataList"><i class="ti ti-refresh"></i></TButton>
        </template>
        <div class="table-responsive">
          <table class="table table-vcenter card-table table-hover table-sm" v-if="selectedType">
            <thead><tr><th>标签</th><th>键值</th><th>排序</th><th>状态</th><th>操作</th></tr></thead>
            <tbody>
              <tr v-if="dataLoading"><td colspan="5" class="text-center py-3 text-muted">加载中...</td></tr>
              <tr v-else-if="!dataList.length"><td colspan="5" class="text-center py-3 text-muted">暂无数据</td></tr>
              <tr v-for="d in dataList" :key="d.id">
                <td>{{ d.dictLabel }}</td>
                <td><code>{{ d.dictValue }}</code></td>
                <td class="text-center">{{ d.sort }}</td>
                <td>
                  <span class="badge" :class="d.status === 1 ? 'bg-success-lt' : 'bg-danger-lt'">{{ d.status === 1 ? '启用' : '停用' }}</span>
                </td>
                <td>
                  <button class="btn btn-ghost-primary btn-sm" @click="openDataEdit(d)"><i class="ti ti-edit"></i></button>
                  <button class="btn btn-ghost-danger btn-sm" @click="delData(d)"><i class="ti ti-trash"></i></button>
                </td>
              </tr>
            </tbody>
          </table>
          <div v-else class="text-center py-5 text-muted"><i class="ti ti-arrow-left me-2"></i>点击左侧字典类型，查看数据</div>
        </div>
      </TCard>
    </div>
  </div>

  <!-- 字典类型弹窗 -->
  <TModal v-model="typeModalVisible" :title="typeIsEdit ? '编辑字典类型' : '新增字典类型'">
    <TForm v-model="typeModel" :schemas="typeFormSchemas" />
    <template #footer>
      <TButton variant="ghost-secondary" @click="typeModalVisible = false">取消</TButton>
      <TButton variant="primary" @click="saveType">确定</TButton>
    </template>
  </TModal>

  <!-- 字典数据弹窗 -->
  <TModal v-model="dataModalVisible" :title="dataIsEdit ? '编辑字典数据' : '新增字典数据'">
    <TForm v-model="dataModel" :schemas="dataFormSchemas" />
    <template #footer>
      <TButton variant="ghost-secondary" @click="dataModalVisible = false">取消</TButton>
      <TButton variant="primary" @click="saveData">确定</TButton>
    </template>
  </TModal>
</template>
