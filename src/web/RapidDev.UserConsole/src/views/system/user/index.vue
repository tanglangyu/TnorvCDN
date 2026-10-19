<script setup lang="ts">
/**
 * 用户管理 -- 使用 TCrud 一体化组件。
 * status / userType 均为 int(0=启用 1=停用;1=客户 2=内部)。
 * 「管理」按钮跳转单用户管理页(顶部 Tab 管理各类数据)。
 * 「打标签」弹窗多选 customer_tag 字典项,保存到 sys_user_tag。
 */
import { ref, reactive, onMounted } from 'vue'
import { TCrud, TButton, TModal } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { userApi, dictApi } from '@/api/system'
import { useRouter } from 'vue-router'
import type { SysDictData } from '@/types'

const router = useRouter()

const columns: TableColumn[] = [
  { prop: 'userName', label: '用户名', minWidth: 120, sortable: true },
  { prop: 'nickName', label: '昵称', minWidth: 120 },
  { prop: 'phone', label: '手机号', minWidth: 130 },
  { prop: 'tags', label: '标签', minWidth: 160, slot: 'tags' },
  { prop: 'userType', label: '类型', width: 80, slot: 'userType', align: 'center' },
  { prop: 'isIdCardVerified', label: '实名', width: 70, slot: 'idcard', align: 'center' },
  { prop: 'status', label: '状态', width: 90, slot: 'status', align: 'center' },
  { prop: 'lastLoginAt', label: '最后登录', minWidth: 160 },
  { prop: 'manage', label: '管理', width: 150, slot: 'manage', align: 'center' },
]

const searchSchemas: FormItemSchema[] = [
  { prop: 'userName', label: '用户名', type: 'input', placeholder: '请输入用户名' },
  { prop: 'phone', label: '手机号', type: 'input', placeholder: '请输入手机号' },
]

const formSchemas: FormItemSchema[] = [
  { prop: 'userName', label: '用户名', required: true, span: 6 },
  { prop: 'nickName', label: '昵称', required: true, span: 6 },
  { prop: 'password', label: '密码', type: 'input', span: 6, placeholder: '新增时填写,默认 123456' },
  { prop: 'phone', label: '手机号', type: 'input', span: 6 },
  { prop: 'email', label: '邮箱', type: 'input', span: 6 },
  { prop: 'gender', label: '性别', type: 'select', span: 6, defaultValue: 0, options: [{ label: '未知', value: 0 }, { label: '男', value: 1 }, { label: '女', value: 2 }] },
  { prop: 'userType', label: '用户类型', type: 'select', span: 6, defaultValue: 1, options: [{ label: '客户', value: 1 }, { label: '内部', value: 2 }] },
  { prop: 'status', label: '状态', type: 'select', span: 6, defaultValue: 0, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }] },
  { prop: 'remark', label: '备注', type: 'textarea', span: 12 },
]

function goDetail(row: any) {
  router.push(`/system/user/${row.id}`)
}

// ===== 打标签弹窗(预置字典标签 + 自定义文本标签) =====
const tagModal = reactive({ visible: false, userId: 0, userName: '', saving: false })
const tagOptions = ref<SysDictData[]>([])
const checkedTags = ref<Set<number>>(new Set())      // 预置字典标签选中(TagId)
const customTags = ref<string[]>([])                 // 自定义文本标签
const customInput = ref('')
const crudRef = ref<any>(null)

async function loadTagOptions() {
  if (tagOptions.value.length) return
  tagOptions.value = await dictApi.dataList('customer_tag')
}

async function openTagModal(row: any) {
  await loadTagOptions()
  tagModal.userId = row.id
  tagModal.userName = row.userName
  const list = await userApi.getTags(row.id)
  checkedTags.value = new Set(list.filter(t => t.tagId).map(t => t.tagId!))
  customTags.value = list.filter(t => !t.tagId && t.text).map(t => t.text!)
  customInput.value = ''
  tagModal.visible = true
}

function toggleTag(id: number) {
  if (checkedTags.value.has(id)) checkedTags.value.delete(id)
  else checkedTags.value.add(id)
}

function addCustomTag() {
  const v = customInput.value.trim()
  if (!v) return
  if (v.length > 30) { return }
  if (customTags.value.some(t => t.toLowerCase() === v.toLowerCase())) { customInput.value = ''; return }
  customTags.value.push(v)
  customInput.value = ''
}

function removeCustomTag(idx: number) {
  customTags.value.splice(idx, 1)
}

async function saveTags() {
  tagModal.saving = true
  try {
    const tags: { tagId?: number; text?: string }[] = []
    checkedTags.value.forEach(id => tags.push({ tagId: id }))
    customTags.value.forEach(t => tags.push({ text: t }))
    await userApi.updateTags(tagModal.userId, tags)
    tagModal.visible = false
    crudRef.value?.refresh?.()
  } finally { tagModal.saving = false }
}
</script>

<template>
  <TCrud
    ref="crudRef"
    :columns="columns"
    :search-schemas="searchSchemas"
    :form-schemas="formSchemas"
    :fetch="userApi.list"
    :create="userApi.add"
    :detail="userApi.get"
    :update="userApi.edit"
    :remove="userApi.remove"
    add-title="新增用户"
    edit-title="编辑用户"
  >
    <template #body-tags="{ row }">
      <span v-for="t in row.tags" :key="t" class="badge bg-azure-lt me-1">{{ t }}</span>
      <span v-if="!row.tags?.length" class="text-muted">-</span>
    </template>
    <template #body-userType="{ row }">
      <span class="badge" :class="row.userType === 2 ? 'bg-info-lt' : 'bg-azure-lt'">{{ row.userType === 2 ? '内部' : '客户' }}</span>
    </template>
    <template #body-idcard="{ row }">
      <span v-if="row.isIdCardVerified" class="badge bg-success-lt">已实名</span>
      <span v-else class="badge bg-secondary-lt">未实名</span>
    </template>
    <template #body-status="{ row }">
      <span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '启用' : '停用' }}</span>
    </template>
    <template #body-manage="{ row }">
      <TButton variant="ghost-primary" size="sm" @click="goDetail(row)"><i class="ti ti-user-cog"></i>管理</TButton>
      <TButton variant="ghost-secondary" size="sm" @click="openTagModal(row)"><i class="ti ti-tag"></i>标签</TButton>
    </template>
  </TCrud>

  <TModal v-model="tagModal.visible" :title="`打标签 - ${tagModal.userName}`" size="md">
    <div class="mb-3">
      <div class="form-label small text-muted mb-2">预置标签</div>
      <div class="d-flex flex-wrap gap-2">
        <label v-for="t in tagOptions" :key="t.id" class="form-check form-check-inline d-flex align-items-center gap-2">
          <input type="checkbox" class="form-check-input" :checked="checkedTags.has(t.id)" @change="toggleTag(t.id)" />
          <span class="badge" :class="t.listClass || 'bg-azure-lt'">{{ t.dictLabel }}</span>
        </label>
        <div v-if="!tagOptions.length" class="text-muted small">暂无预置标签</div>
      </div>
    </div>
    <div class="mb-2">
      <div class="form-label small text-muted mb-2">自定义标签(回车添加,最长 30 字符)</div>
      <div class="input-group input-group-sm">
        <input class="form-control" v-model="customInput" maxlength="30" placeholder="输入自定义标签文本" @keyup.enter="addCustomTag" />
        <TButton size="sm" variant="outline-secondary" @click="addCustomTag"><i class="ti ti-plus"></i>添加</TButton>
      </div>
      <div class="d-flex flex-wrap gap-1 mt-2">
        <span v-for="(t, idx) in customTags" :key="idx" class="badge bg-warning-lt d-flex align-items-center gap-1">
          {{ t }}
          <a href="#" class="text-muted" @click.prevent="removeCustomTag(idx)"><i class="ti ti-x"></i></a>
        </span>
      </div>
    </div>
    <template #footer>
      <TButton variant="ghost-secondary" @click="tagModal.visible = false">取消</TButton>
      <TButton variant="primary" :loading="tagModal.saving" @click="saveTags">保存</TButton>
    </template>
  </TModal>
</template>
