<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { menuApi } from '@/api/system'
import { TCard, TButton, TModal, TForm, TTag } from '@/components/t'
import type { FormItemSchema } from '@/components/t/types'
import type { SysMenu } from '@/types'

const list = ref<SysMenu[]>([])
const loading = ref(false)
const visible = ref(false)
const isEdit = ref(false)
const model = ref<any>({})
const parentOptions = ref<{ label: string; value: number }[]>([])

const typeOpts = [
  { label: '目录', value: 0 }, { label: '菜单', value: 1 }, { label: '按钮', value: 2 },
]
const statusOpts = [
  { label: '启用', value: 0 }, { label: '停用', value: 1 },
]
const yesNoOpts = [
  { label: '是', value: 1 }, { label: '否', value: 0 },
]

const formSchemas: FormItemSchema[] = [
  { prop: 'parentId', label: '上级菜单', type: 'select', defaultValue: 0, span: 12, options: parentOptions.value },
  { prop: 'menuType', label: '菜单类型', type: 'select', required: true, span: 12, options: typeOpts },
  { prop: 'menuName', label: '菜单名称', required: true, span: 12 },
  { prop: 'icon', label: '图标', span: 6, placeholder: 'home/users/shield/...' },
  { prop: 'orderNum', label: '排序', type: 'number', defaultValue: 0, span: 6 },
  { prop: 'path', label: '路由路径', span: 12, placeholder: '/system/xxx' },
  { prop: 'component', label: '组件路径', span: 12, placeholder: 'system/xxx/index' },
  { prop: 'permission', label: '权限标识', span: 12, placeholder: 'system:xxx:list' },
  { prop: 'query', label: '路由参数', span: 12 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: statusOpts },
  { prop: 'visible', label: '是否显示', type: 'select', defaultValue: 1, span: 6, options: yesNoOpts },
  { prop: 'isCache', label: '是否缓存', type: 'select', defaultValue: 1, span: 6, options: yesNoOpts },
  { prop: 'isFrame', label: '是否外链', type: 'select', defaultValue: 0, span: 6, options: yesNoOpts },
  { prop: 'remark', label: '备注', type: 'textarea', span: 12 },
]

async function load() {
  loading.value = true
  list.value = await menuApi.tree() as any
  loading.value = false
}

function buildParentOpts() {
  const opts: { label: string; value: number }[] = [{ label: '根节点', value: 0 }]
  function walk(menus: SysMenu[], prefix: string) {
    for (const m of menus) {
      if (m.menuType !== 2) {
        opts.push({ label: prefix + m.menuName, value: m.id })
        walk(list.value.filter(x => x.parentId === m.id), prefix + '　')
      }
    }
  }
  walk(list.value.filter(x => x.parentId === 0), '')
  parentOptions.value = opts
}

function openAdd(parentId?: number) {
  isEdit.value = false
  model.value = { parentId: parentId ?? 0, menuType: 1, status: 0, orderNum: 0, visible: 1, isCache: 1, isFrame: 0 }
  buildParentOpts()
  visible.value = true
}
function openEdit(row: SysMenu) {
  isEdit.value = true
  model.value = { ...row }
  buildParentOpts()
  visible.value = true
}
async function save() {
  if (isEdit.value) await menuApi.edit(model.value.id, model.value)
  else await menuApi.add(model.value)
  visible.value = false
  load()
}
async function del(row: SysMenu) {
  if (!confirm(`确定删除菜单 "${row.menuName}" ?`)) return
  await menuApi.remove(row.id)
  load()
}

function typeText(t: number) { return ['目录', '菜单', '按钮'][t] ?? t }
function typeColor(t: number) { return ['bg-primary-lt', 'bg-info-lt', 'bg-secondary-lt'][t] ?? 'bg-secondary-lt' }

// 递归渲染树节点
function childrenOf(parentId: number): SysMenu[] {
  return list.value.filter(m => m.parentId === parentId)
}

onMounted(load)
</script>

<template>
  <TCard title="菜单管理">
    <template #actions>
      <TButton variant="primary" @click="openAdd(0)"><i class="ti ti-plus me-1"></i>新增</TButton>
      <TButton variant="ghost-secondary" @click="load"><i class="ti ti-refresh"></i></TButton>
    </template>

    <div class="table-responsive">
      <table class="table table-vcenter card-table table-hover" v-if="!loading">
        <thead>
          <tr><th style="width:40%">菜单名称</th><th style="width:12%">类型</th><th style="width:12%">排序</th><th style="width:12%">状态</th><th style="width:24%">操作</th></tr>
        </thead>
        <tbody>
          <template v-for="root in childrenOf(0)" :key="root.id">
            <tr>
              <td>
                <span class="fw-medium">
                  <i v-if="root.icon" class="ti me-1" :class="'ti-' + root.icon"></i>
                  {{ root.menuName }}
                </span>
              </td>
              <td><span class="badge" :class="typeColor(root.menuType)">{{ typeText(root.menuType) }}</span></td>
              <td>{{ root.orderNum }}</td>
              <td>
                <span class="badge" :class="root.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ root.status === 0 ? '启用' : '停用' }}</span>
              </td>
              <td>
                <div class="btn-list gap-0">
                  <button class="btn btn-ghost-primary btn-sm" title="新增子级" @click="openAdd(root.id)"><i class="ti ti-plus"></i></button>
                  <button class="btn btn-ghost-primary btn-sm" title="编辑" @click="openEdit(root)"><i class="ti ti-edit"></i></button>
                  <button class="btn btn-ghost-danger btn-sm" title="删除" @click="del(root)"><i class="ti ti-trash"></i></button>
                  <TTag v-if="root.path" variant="info" size="sm" class="ms-1">{{ root.path }}</TTag>
                </div>
              </td>
            </tr>
            <!-- 子级 -->
            <template v-for="child in childrenOf(root.id)" :key="child.id">
              <tr>
                <td class="ps-5">
                  <span :class="{ 'fw-medium': child.menuType !== 2 }">
                    <i v-if="child.icon" class="ti me-1" :class="'ti-' + child.icon"></i>
                    {{ child.menuName }}
                  </span>
                </td>
                <td><span class="badge" :class="typeColor(child.menuType)">{{ typeText(child.menuType) }}</span></td>
                <td>{{ child.orderNum }}</td>
                <td>
                  <span class="badge" :class="child.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ child.status === 0 ? '启用' : '停用' }}</span>
                </td>
                <td>
                  <div class="btn-list gap-0">
                    <button v-if="child.menuType !== 2" class="btn btn-ghost-primary btn-sm" title="新增子级" @click="openAdd(child.id)"><i class="ti ti-plus"></i></button>
                    <button class="btn btn-ghost-primary btn-sm" title="编辑" @click="openEdit(child)"><i class="ti ti-edit"></i></button>
                    <button class="btn btn-ghost-danger btn-sm" title="删除" @click="del(child)"><i class="ti ti-trash"></i></button>
                    <code v-if="child.permission" class="ms-1 text-muted small">{{ child.permission }}</code>
                  </div>
                </td>
              </tr>
            </template>
          </template>
          <tr v-if="childrenOf(0).length === 0">
            <td colspan="5" class="text-center py-4 text-muted">暂无菜单数据，请点击"新增"按钮添加</td>
          </tr>
        </tbody>
      </table>
      <div v-else class="text-center py-4 text-muted">加载中...</div>
    </div>
  </TCard>

  <TModal v-model="visible" :title="isEdit ? '编辑菜单' : '新增菜单'" size="lg">
    <TForm v-model="model" :schemas="formSchemas" />
    <template #footer>
      <TButton variant="ghost-secondary" @click="visible = false">取消</TButton>
      <TButton variant="primary" @click="save">确定</TButton>
    </template>
  </TModal>
</template>
