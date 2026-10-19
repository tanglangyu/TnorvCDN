<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { deptApi } from '@/api/system'
import { TCard, TButton, TModal, TForm, TTag } from '@/components/t'
import type { FormItemSchema } from '@/components/t/types'
import type { SysDept } from '@/types'

const list = ref<SysDept[]>([])
const loading = ref(false)
const modalVisible = ref(false)
const isEdit = ref(false)
const model = ref<any>({})

const parentOptions = ref<{ label: string; value: number }[]>([{ label: '根部门', value: 0 }])

const formSchemas: FormItemSchema[] = [
  { prop: 'parentId', label: '上级部门', type: 'select', defaultValue: 0, span: 12, options: parentOptions.value },
  { prop: 'deptName', label: '部门名称', required: true, span: 6 },
  { prop: 'sort', label: '排序', type: 'number', defaultValue: 0, span: 6 },
  { prop: 'leader', label: '负责人', span: 6 },
  { prop: 'phone', label: '联系电话', span: 6 },
  { prop: 'email', label: '邮箱', span: 12 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }] },
]

async function load() {
  loading.value = true
  list.value = await deptApi.list() as any
  loading.value = false
}

function buildParentOpts() {
  const opts: { label: string; value: number }[] = [{ label: '根部门', value: 0 }]
  function walk(depts: SysDept[], prefix: string) {
    for (const d of depts) {
      opts.push({ label: prefix + d.deptName, value: d.id })
      walk(list.value.filter(x => x.parentId === d.id), prefix + '　')
    }
  }
  walk(list.value.filter(x => x.parentId === 0), '')
  parentOptions.value = opts
}

function openAdd(parentId?: number) {
  isEdit.value = false
  model.value = { parentId: parentId ?? 0, status: 0, sort: 0 }
  buildParentOpts()
  modalVisible.value = true
}
function openEdit(row: SysDept) {
  isEdit.value = true
  model.value = { ...row }
  buildParentOpts()
  modalVisible.value = true
}
async function save() {
  if (isEdit.value) await deptApi.edit(model.value.id, model.value)
  else await deptApi.add(model.value)
  modalVisible.value = false
  load()
}
async function del(row: SysDept) {
  if (!confirm(`确定删除部门 "${row.deptName}"?`)) return
  await deptApi.remove(row.id)
  load()
}

function childrenOf(parentId: number): SysDept[] {
  return list.value.filter(d => d.parentId === parentId)
}

onMounted(load)
</script>

<template>
  <div class="row g-3">
    <div class="col-lg-8">
      <TCard title="部门树">
        <template #actions>
          <TButton variant="primary" size="sm" @click="openAdd(0)"><i class="ti ti-plus me-1"></i>新增</TButton>
          <TButton variant="ghost-secondary" size="sm" @click="load"><i class="ti ti-refresh"></i></TButton>
        </template>
        <div class="table-responsive">
          <table class="table table-vcenter card-table table-hover table-sm" v-if="!loading">
            <thead><tr><th style="width:35%">部门名称</th><th style="width:15%">负责人</th><th style="width:15%">联系电话</th><th style="width:12%">排序</th><th style="width:12%">状态</th><th style="width:11%">操作</th></tr></thead>
            <tbody>
              <template v-for="root in childrenOf(0)" :key="root.id">
                <tr>
                  <td><span class="fw-medium"><i class="ti ti-building me-1"></i>{{ root.deptName }}</span></td>
                  <td>{{ root.leader || '-' }}</td>
                  <td>{{ root.phone || '-' }}</td>
                  <td class="text-center">{{ root.sort }}</td>
                  <td><span class="badge" :class="root.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ root.status === 0 ? '启用' : '停用' }}</span></td>
                  <td>
                    <button class="btn btn-ghost-primary btn-sm" title="新增子部门" @click="openAdd(root.id)"><i class="ti ti-plus"></i></button>
                    <button class="btn btn-ghost-primary btn-sm" title="编辑" @click="openEdit(root)"><i class="ti ti-edit"></i></button>
                    <button class="btn btn-ghost-danger btn-sm" title="删除" @click="del(root)"><i class="ti ti-trash"></i></button>
                  </td>
                </tr>
                <template v-for="child in childrenOf(root.id)" :key="child.id">
                  <tr>
                    <td class="ps-5">{{ child.deptName }}</td>
                    <td>{{ child.leader || '-' }}</td>
                    <td>{{ child.phone || '-' }}</td>
                    <td class="text-center">{{ child.sort }}</td>
                    <td><span class="badge" :class="child.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ child.status === 0 ? '启用' : '停用' }}</span></td>
                    <td>
                      <button class="btn btn-ghost-primary btn-sm" title="编辑" @click="openEdit(child)"><i class="ti ti-edit"></i></button>
                      <button class="btn btn-ghost-danger btn-sm" title="删除" @click="del(child)"><i class="ti ti-trash"></i></button>
                    </td>
                  </tr>
                </template>
              </template>
              <tr v-if="childrenOf(0).length === 0">
                <td colspan="6" class="text-center py-4 text-muted">暂无部门数据</td>
              </tr>
            </tbody>
          </table>
          <div v-else class="text-center py-4 text-muted">加载中...</div>
        </div>
      </TCard>
    </div>
    <div class="col-lg-4">
      <TCard title="说明">
        <ul class="text-muted small">
          <li>部门以树形结构组织</li>
          <li>点击"<i class="ti ti-plus"></i>"可为当前部门添加子级</li>
          <li>部门不可删除时说明存在子部门或用户</li>
        </ul>
      </TCard>
    </div>
  </div>

  <TModal v-model="modalVisible" :title="isEdit ? '编辑部门' : '新增部门'">
    <TForm v-model="model" :schemas="formSchemas" />
    <template #footer>
      <TButton variant="ghost-secondary" @click="modalVisible = false">取消</TButton>
      <TButton variant="primary" @click="save">确定</TButton>
    </template>
  </TModal>
</template>
