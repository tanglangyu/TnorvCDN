<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { roleApi, menuApi } from '@/api/system'
import { TCard, TButton, TModal, TForm, TTag, TSwitch } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import type { SysRole, SysMenu } from '@/types'

const list = ref<SysRole[]>([])
const loading = ref(false)

// 弹窗
const modalVisible = ref(false)
const isEdit = ref(false)
const model = ref<SysRole>({} as SysRole)
const allMenus = ref<SysMenu[]>([])
const checkedMenuIds = ref<Set<number>>(new Set())

// 权限点预览(从勾选的菜单推导)
const permTab = ref<'menu' | 'perms'>('menu')
const grantedPerms = computed(() => {
  const perms = new Set<string>()
  for (const m of allMenus.value) {
    if (checkedMenuIds.value.has(m.id) && m.permission) perms.add(m.permission)
  }
  return Array.from(perms).sort()
})

// 角色权限查看弹窗
const permViewVisible = ref(false)
const viewRole = ref<SysRole | null>(null)
const viewPerms = ref<string[]>([])
const viewMenuIds = ref<number[]>([])

const scopeMap: Record<number, string> = { 1: '全部', 2: '自定义', 3: '本部门', 4: '本部门及以下', 5: '仅本人' }
const formSchemas: FormItemSchema[] = [
  { prop: 'roleName', label: '角色名称', required: true, span: 6 },
  { prop: 'roleKey', label: '角色标识', required: true, span: 6 },
  { prop: 'dataScope', label: '数据范围', type: 'select', required: true, span: 6, options: Object.entries(scopeMap).map(([k, v]) => ({ label: v, value: Number(k) })) },
  { prop: 'sort', label: '排序', type: 'number', defaultValue: 0, span: 6 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }] },
]

async function load() {
  loading.value = true
  try {
    const res = await roleApi.list({ pageIndex: 1, pageSize: 100 }) as any
    list.value = Array.isArray(res) ? res : (res?.list ?? [])
  } finally { loading.value = false }
}

async function openAdd() {
  isEdit.value = false
  model.value = { id: 0, roleName: '', roleKey: '', dataScope: 1, sort: 0, status: 0 } as SysRole
  checkedMenuIds.value = new Set()
  allMenus.value = await menuApi.tree() as any
  expandAll()
  permTab.value = 'menu'
  modalVisible.value = true
}

async function openEdit(row: SysRole) {
  isEdit.value = true
  model.value = { ...row }
  allMenus.value = await menuApi.tree() as any
  const ids = await roleApi.menuIds(row.id) as any
  checkedMenuIds.value = new Set(ids || [])
  expandAll()
  permTab.value = 'menu'
  modalVisible.value = true
}

async function saveRole() {
  const menuIds = Array.from(checkedMenuIds.value)
  if (isEdit.value) await roleApi.edit(model.value.id, model.value, menuIds, [])
  else await roleApi.add(model.value, menuIds, [])
  modalVisible.value = false
  load()
}

async function delRole(row: SysRole) {
  if (!confirm(`确定删除角色 "${row.roleName}"?`)) return
  await roleApi.remove(row.id)
  load()
}

// 查看角色权限(只读)
async function viewPermissions(row: SysRole) {
  viewRole.value = row
  viewPerms.value = []
  viewMenuIds.value = []
  permViewVisible.value = true
  try {
    const r = await roleApi.permissions(row.id) as any
    viewPerms.value = r?.permissions || []
    viewMenuIds.value = r?.menuIds || []
  } catch {}
}

// 菜单树 checkbox
function toggleMenu(id: number) {
  if (checkedMenuIds.value.has(id)) checkedMenuIds.value.delete(id)
  else checkedMenuIds.value.add(id)
  checkedMenuIds.value = new Set(checkedMenuIds.value)
}

// 全选/取消全选
function selectAll() {
  if (checkedMenuIds.value.size === allMenus.value.length) {
    checkedMenuIds.value = new Set()
  } else {
    checkedMenuIds.value = new Set(allMenus.value.map(m => m.id))
  }
}
// 展开/折叠目录(只展开/折叠 Directory/Menu,不折叠 Button)
const expandedDirs = ref<Set<number>>(new Set())
function toggleExpand(id: number) {
  if (expandedDirs.value.has(id)) expandedDirs.value.delete(id)
  else expandedDirs.value.add(id)
  expandedDirs.value = new Set(expandedDirs.value)
}
function expandAll() {
  expandedDirs.value = new Set(allMenus.value.filter(m => m.menuType !== 2).map(m => m.id))
}
function collapseAll() {
  expandedDirs.value = new Set()
}
// 勾选父菜单时自动勾选所有子菜单(含按钮权限点)
function toggleWithChildren(menu: SysMenu) {
  const children = childrenOf(menu.id)
  const allChecked = checkedMenuIds.value.has(menu.id)
    && children.every(c => checkedMenuIds.value.has(c.id))
  if (allChecked) {
    // 取消本菜单 + 所有子菜单
    checkedMenuIds.value.delete(menu.id)
    children.forEach(c => checkedMenuIds.value.delete(c.id))
  } else {
    checkedMenuIds.value.add(menu.id)
    children.forEach(c => checkedMenuIds.value.add(c.id))
  }
  checkedMenuIds.value = new Set(checkedMenuIds.value)
}
// 菜单搜索过滤
const menuKeyword = ref('')
const filteredRoots = computed(() => {
  const roots = childrenOf(0)
  if (!menuKeyword.value) return roots
  const kw = menuKeyword.value.toLowerCase()
  // 搜索时:返回匹配的根 + 其下匹配的子项
  return roots.filter(root => {
    const children = childrenOf(root.id)
    const rootMatch = root.menuName.toLowerCase().includes(kw) || (root.permission || '').toLowerCase().includes(kw)
    const childMatch = children.some(c => c.menuName.toLowerCase().includes(kw) || (c.permission || '').toLowerCase().includes(kw))
    return rootMatch || childMatch
  })
})
function filteredChildren(parentId: number): SysMenu[] {
  const all = childrenOf(parentId)
  if (!menuKeyword.value) return all
  const kw = menuKeyword.value.toLowerCase()
  return all.filter(c => c.menuName.toLowerCase().includes(kw) || (c.permission || '').toLowerCase().includes(kw))
}

function childrenOf(parentId: number): SysMenu[] {
  return allMenus.value.filter(m => m.parentId === parentId)
}

function typeText(t: number) { return ['目录', '菜单', '按钮'][t] ?? t }

onMounted(load)
</script>

<template>
  <div class="row g-3">
    <div class="col-lg-6">
      <TCard title="角色列表">
        <template #actions>
          <TButton variant="primary" size="sm" @click="openAdd"><i class="ti ti-plus me-1"></i>新增</TButton>
        </template>
        <div class="table-responsive">
          <table class="table table-vcenter card-table table-hover table-sm">
            <thead><tr><th>名称</th><th>标识</th><th>数据范围</th><th>排序</th><th>状态</th><th>操作</th></tr></thead>
            <tbody>
              <tr v-if="loading"><td colspan="6" class="text-center py-3 text-muted">加载中...</td></tr>
              <tr v-else-if="!list.length"><td colspan="6" class="text-center py-3 text-muted">暂无数据</td></tr>
              <tr v-for="r in list" :key="r.id">
                <td class="fw-medium">{{ r.roleName }}</td>
                <td><TTag variant="primary" size="sm">{{ r.roleKey }}</TTag></td>
                <td><span class="badge bg-info-lt">{{ scopeMap[r.dataScope] }}</span></td>
                <td class="text-center">{{ r.sort }}</td>
                <td><span class="badge" :class="r.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ r.status === 0 ? '启用' : '停用' }}</span></td>
                <td>
                  <button class="btn btn-ghost-info btn-sm" @click="viewPermissions(r)" title="查看权限"><i class="ti ti-eye"></i></button>
                  <button class="btn btn-ghost-primary btn-sm" @click="openEdit(r)"><i class="ti ti-edit"></i></button>
                  <button class="btn btn-ghost-danger btn-sm" @click="delRole(r)"><i class="ti ti-trash"></i></button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </TCard>
    </div>
    <div class="col-lg-6">
      <TCard title="使用说明">
        <p class="text-muted">点击"编辑"为角色分配菜单权限(菜单含按钮权限点);"查看权限"看该角色拥有的权限点。</p>
        <ul class="text-muted small">
          <li><strong>数据范围</strong>: 全部=可看所有数据, 本部门=仅本部门, 仅本人=仅自己</li>
          <li><strong>角色标识</strong>: admin 为超级管理员, 自动拥有全部权限</li>
          <li><strong>权限点</strong>: 通过勾选菜单(含按钮)分配,见 <RouterLink to="/system/permission">权限点管理</RouterLink></li>
        </ul>
      </TCard>
    </div>
  </div>

  <!-- 编辑弹窗(菜单树 + 权限点预览) -->
  <TModal v-model="modalVisible" :title="isEdit ? '编辑角色' : '新增角色'" size="lg">
    <TForm v-model="model" :schemas="formSchemas" />

    <div class="mt-3">
      <ul class="nav nav-tabs mb-2">
        <li class="nav-item"><a class="nav-link" :class="{active: permTab==='menu'}" href="javascript:;" @click="permTab='menu'">菜单权限</a></li>
        <li class="nav-item"><a class="nav-link" :class="{active: permTab==='perms'}" href="javascript:;" @click="permTab='perms'">
          权限点预览 <span class="badge bg-primary-lt ms-1">{{ grantedPerms.length }}</span>
        </a></li>
      </ul>

      <div v-show="permTab==='menu'">
        <div class="d-flex align-items-center gap-2 mb-2 flex-wrap">
          <TInput v-model="menuKeyword" placeholder="搜索菜单/权限点" size="sm" style="width: 200px">
            <template #prefix><i class="ti ti-search"></i></template>
          </TInput>
          <TButton size="sm" variant="ghost-secondary" @click="expandAll"><i class="ti ti-arrows-vertical"></i> 全展开</TButton>
          <TButton size="sm" variant="ghost-secondary" @click="collapseAll"><i class="ti ti-arrows-vertical"></i> 全折叠</TButton>
          <TButton size="sm" variant="ghost-primary" @click="selectAll">
            <i class="ti" :class="checkedMenuIds.size === allMenus.length ? 'ti-square-x' : 'ti-square-check'"></i>
            {{ checkedMenuIds.size === allMenus.length ? '取消全选' : '全选' }}
          </TButton>
          <span class="text-muted small ms-auto">已选 {{ checkedMenuIds.size }} / {{ allMenus.length }}</span>
        </div>
        <div class="border rounded p-2" style="max-height: 320px; overflow-y: auto">
          <template v-for="root in filteredRoots" :key="root.id">
            <div class="mb-1">
              <div class="d-flex align-items-center">
                <button v-if="childrenOf(root.id).length" class="btn btn-ghost-secondary btn-sm p-0 me-1" @click="toggleExpand(root.id)" style="width:24px">
                  <i class="ti" :class="expandedDirs.has(root.id) ? 'ti-chevron-down' : 'ti-chevron-right'"></i>
                </button>
                <label class="form-check fw-medium flex-grow-1" style="cursor: pointer">
                  <input type="checkbox" class="form-check-input" :checked="checkedMenuIds.has(root.id)" @change="toggleWithChildren(root)" />
                  <i class="ti me-1" :class="root.menuType === 0 ? 'ti-folder' : 'ti-menu-2'"></i>
                  {{ root.menuName }}
                  <small class="text-muted ms-1" v-if="root.permission">({{ root.permission }})</small>
                </label>
              </div>
              <div class="ps-4" v-show="expandedDirs.has(root.id) || menuKeyword">
                <div v-for="child in filteredChildren(root.id)" :key="child.id" class="d-flex align-items-center">
                  <button v-if="childrenOf(child.id).length" class="btn btn-ghost-secondary btn-sm p-0 me-1" @click="toggleExpand(child.id)" style="width:24px">
                    <i class="ti" :class="expandedDirs.has(child.id) ? 'ti-chevron-down' : 'ti-chevron-right'"></i>
                  </button>
                  <span v-else style="width:24px"></span>
                  <label class="form-check small flex-grow-1" style="cursor: pointer">
                    <input type="checkbox" class="form-check-input" :checked="checkedMenuIds.has(child.id)" @change="toggleWithChildren(child)" />
                    <i class="ti me-1" :class="child.menuType === 2 ? 'ti-point' : 'ti-menu-2'"></i>
                    {{ child.menuName }}
                    <small class="text-muted" v-if="child.permission">— {{ child.permission }}</small>
                  </label>
                </div>
              </div>
            </div>
          </template>
          <div v-if="filteredRoots.length === 0" class="text-muted text-center py-3 small">无匹配菜单</div>
        </div>
      </div>

      <div v-show="permTab==='perms'" class="border rounded p-2" style="max-height: 350px; overflow-y: auto">
        <div v-if="grantedPerms.length === 0" class="text-muted text-center py-3 small">未勾选任何含权限点的菜单</div>
        <div v-else>
          <span v-for="p in grantedPerms" :key="p" class="badge bg-azure-lt me-1 mb-1"><code>{{ p }}</code></span>
        </div>
      </div>
    </div>

    <template #footer>
      <TButton variant="ghost-secondary" @click="modalVisible = false">取消</TButton>
      <TButton variant="primary" @click="saveRole">保存</TButton>
    </template>
  </TModal>

  <!-- 权限查看弹窗 -->
  <TModal v-model="permViewVisible" :title="`角色权限 - ${viewRole?.roleName || ''}`" size="lg">
    <div class="mb-3">
      <h6>已分配菜单 ({{ viewMenuIds.length }})</h6>
      <span class="text-muted small">菜单 ID: {{ viewMenuIds.join(', ') || '无' }}</span>
    </div>
    <div>
      <h6>拥有的权限点 ({{ viewPerms.length }})</h6>
      <div class="border rounded p-2" style="max-height: 400px; overflow-y: auto">
        <div v-if="viewPerms.length === 0" class="text-muted text-center py-3 small">
          <template v-if="viewRole?.roleKey === 'admin'">超级管理员拥有全部权限(*:*:*)</template>
          <template v-else>未分配任何权限点</template>
        </div>
        <div v-else>
          <span v-for="p in viewPerms" :key="p" class="badge bg-azure-lt me-1 mb-1"><code>{{ p }}</code></span>
        </div>
      </div>
    </div>
    <template #footer>
      <TButton variant="primary" @click="permViewVisible = false">关闭</TButton>
    </template>
  </TModal>
</template>
