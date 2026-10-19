<script setup lang="ts">
/**
 * 单用户管理页 -- 顶部 Tab 菜单管理用户各类数据。
 * Tab:基本信息 / 登录设备·会话 / 登录日志 / 操作日志(权限点控可见)。
 * 所有列表 Tab 支持高级查询(TSearchForm + TTable),并预置当前用户过滤。
 * 路由:/system/user/:id(隐藏,从用户列表「管理」入口进入)
 */
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { TCard, TButton, TTable, TSearchForm, TForm } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { userApi, logApi, dictApi, idCardApi, configApi } from '@/api/system'
import { authApi } from '@/api/auth'
import { formatTime } from '@/utils/time'
import type { SessionInfo, SysDictData } from '@/types'

const route = useRoute()
const router = useRouter()
const userId = Number(route.params.id)

const tab = ref<'basic' | 'tags' | 'session' | 'login' | 'oper'>('basic')
const loadedTabs = ref<Set<string>>(new Set())

// ===== 基本信息 =====
const basicLoading = ref(false)
const basicInfo = ref<any>({})
const formRef = ref()
const formModel = reactive<Record<string, any>>({})
const saving = ref(false)

const basicSchemas: FormItemSchema[] = [
  { prop: 'nickName', label: '昵称', required: true, span: 6 },
  { prop: 'phone', label: '手机号', type: 'input', span: 6 },
  { prop: 'email', label: '邮箱', type: 'input', span: 6 },
  { prop: 'gender', label: '性别', type: 'select', span: 6, options: [{ label: '未知', value: 0 }, { label: '男', value: 1 }, { label: '女', value: 2 }] },
  { prop: 'userType', label: '用户类型', type: 'select', span: 6, options: [{ label: '客户', value: 1 }, { label: '内部', value: 2 }] },
  { prop: 'remark', label: '备注', type: 'textarea', span: 12 },
]

async function loadBasic() {
  basicLoading.value = true
  try {
    const info: any = await userApi.get(userId)
    basicInfo.value = info
    Object.keys(formModel).forEach((k) => delete formModel[k])
    Object.assign(formModel, info) // 含 userName/deptId/id,保存时原样回传(角色/岗位 null=不改动)
  } finally { basicLoading.value = false }
}

async function saveBasic() {
  const v = formRef.value?.submit()
  if (v && v.valid === false) return
  saving.value = true
  try { await userApi.edit(userId, { ...formModel }); await loadBasic() } finally { saving.value = false }
}

async function toggleStatus() {
  const next = basicInfo.value.status === 0 ? 1 : 0
  if (next === 1 && !confirm('确定禁用该用户?将立即撤销其全部活跃会话(踢下线)。')) return
  try { await userApi.changeStatus(userId, next); await loadBasic() } catch {}
}

// ===== 身份证实名验证 =====
const idCardPageEnabled = ref(true)  // 实名认证页面开关(Auth.IdCardVerifyPageEnabled)
const idCard = reactive({ name: '', idNum: '', verifying: false, result: null as any })
async function verifyIdCard() {
  if (!idCard.name || !idCard.idNum) { idCard.result = { message: '请输入姓名和身份证号' }; return }
  idCard.verifying = true
  try {
    const r: any = await idCardApi.verify(idCard.name, idCard.idNum, userId)
    idCard.result = r
    if (r.success) await loadBasic() // 刷新 IsIdCardVerified
  } catch (e: any) { idCard.result = { message: e?.message || '验证失败' } } finally { idCard.verifying = false }
}
async function resetIdCard() {
  if (!confirm('确定重置该用户的实名状态?(换绑:重置后可重新验证新身份证)')) return
  try { await userApi.resetIdCard(userId); await loadBasic() } catch {}
}

// ===== 客户标签(预置字典 + 自定义文本) =====
const tagLoading = ref(false)
const tagOptions = ref<SysDictData[]>([])
const checkedTags = ref<Set<number>>(new Set())
const customTags = ref<string[]>([])
const customInput = ref('')
const tagSaving = ref(false)
async function loadTags() {
  tagLoading.value = true
  try {
    tagOptions.value = await dictApi.dataList('customer_tag')
    const list = await userApi.getTags(userId)
    checkedTags.value = new Set(list.filter((t: any) => t.tagId).map((t: any) => t.tagId))
    customTags.value = list.filter((t: any) => !t.tagId && t.text).map((t: any) => t.text)
    customInput.value = ''
  } finally { tagLoading.value = false }
}
function toggleTag(id: number) {
  if (checkedTags.value.has(id)) checkedTags.value.delete(id)
  else checkedTags.value.add(id)
}
function addCustomTag() {
  const v = customInput.value.trim()
  if (!v || v.length > 30) return
  if (customTags.value.some(t => t.toLowerCase() === v.toLowerCase())) { customInput.value = ''; return }
  customTags.value.push(v)
  customInput.value = ''
}
function removeCustomTag(idx: number) { customTags.value.splice(idx, 1) }
async function saveTags() {
  tagSaving.value = true
  try {
    const tags: { tagId?: number; text?: string }[] = []
    checkedTags.value.forEach(id => tags.push({ tagId: id }))
    customTags.value.forEach(t => tags.push({ text: t }))
    await userApi.updateTags(userId, tags); await loadBasic()
  } finally { tagSaving.value = false }
}

// ===== 登录设备·会话 =====
const sessionLoading = ref(false)
const sessions = ref<SessionInfo[]>([])

const sessionColumns: TableColumn[] = [
  { prop: 'device', label: '设备', minWidth: 140 },
  { prop: 'ip', label: 'IP', width: 130 },
  { prop: 'loginAt', label: '登录时间', minWidth: 160 },
  { prop: 'lastActiveAt', label: '最后活跃', minWidth: 160 },
  { prop: 'expireAt', label: '过期时间', minWidth: 160 },
  { prop: 'op', label: '操作', width: 100, slot: 'op', align: 'center' },
]

async function loadSessions() {
  sessionLoading.value = true
  try { sessions.value = (await authApi.session.listByUser(userId)) as SessionInfo[] } finally { sessionLoading.value = false }
}

async function forceRevoke(s: SessionInfo) {
  if (!confirm(`确定强制下线该会话(设备 ${s.device || s.ip || '?'})?`)) return
  try { await authApi.session.forceRevoke(s.sessionId); await loadSessions() } catch {}
}

async function revokeAllSession() {
  if (!confirm('确定强制下线该用户全部设备?')) return
  try { await authApi.session.revokeAllByUser(userId); await loadSessions() } catch {}
}

// ===== 登录日志(预置过滤:UserName eq 当前用户) =====
const loginLoading = ref(false)
const loginData = ref<any[]>([])
const loginTotal = ref(0)
const loginPageIndex = ref(1)
const loginPageSize = ref(10)
const loginFilters = ref<any[]>([])
const loginBase = computed(() => ({ field: 'UserName', op: 'eq', value: basicInfo.value.userName }))

const loginColumns: TableColumn[] = [
  { prop: 'loginIp', label: 'IP', width: 130 },
  { prop: 'loginLocation', label: '登录地点', minWidth: 120 },
  { prop: 'browser', label: '浏览器', width: 120 },
  { prop: 'os', label: '操作系统', width: 120 },
  { prop: 'status', label: '状态', width: 80, slot: 'status', align: 'center' },
  { prop: 'message', label: '消息', minWidth: 150 },
  { prop: 'loginTime', label: '登录时间', minWidth: 160 },
]
const loginSchemas: FormItemSchema[] = [
  { prop: 'loginIp', label: 'IP', type: 'input', placeholder: 'IP(模糊)' },
  { prop: 'message', label: '消息', type: 'input', placeholder: '消息(模糊)' },
]

async function loadLogin() {
  loginLoading.value = true
  try {
    const res: any = await logApi.loginList({ pageIndex: loginPageIndex.value, pageSize: loginPageSize.value, filters: [loginBase.value, ...loginFilters.value] })
    loginData.value = res.list || []
    loginTotal.value = res.total || 0
  } finally { loginLoading.value = false }
}
function onLoginSearch(q: Record<string, any>) {
  loginFilters.value = q.filters && Array.isArray(q.filters) ? q.filters : []
  loginPageIndex.value = 1
  loadLogin()
}
function onLoginReset() { loginFilters.value = []; loginPageIndex.value = 1; loadLogin() }

// ===== 操作日志(预置过滤:OperUserId eq 当前用户;权限点 system:operlog:list 控可见) =====
const operLoading = ref(false)
const operData = ref<any[]>([])
const operTotal = ref(0)
const operPageIndex = ref(1)
const operPageSize = ref(10)
const operFilters = ref<any[]>([])
const operBase = computed(() => ({ field: 'OperUserId', op: 'eq', value: userId }))

const operColumns: TableColumn[] = [
  { prop: 'module', label: '模块', width: 120 },
  { prop: 'title', label: '操作描述', minWidth: 150 },
  { prop: 'requestMethod', label: '方法', width: 80 },
  { prop: 'operIp', label: 'IP', width: 130 },
  { prop: 'status', label: '状态', width: 80, slot: 'operStatus', align: 'center' },
  { prop: 'costTime', label: '耗时(ms)', width: 100, align: 'right' },
  { prop: 'operTime', label: '操作时间', minWidth: 160 },
]
const operSchemas: FormItemSchema[] = [
  { prop: 'module', label: '模块', type: 'input', placeholder: '模块名(模糊)' },
  { prop: 'title', label: '描述', type: 'input', placeholder: '操作描述(模糊)' },
]

async function loadOper() {
  operLoading.value = true
  try {
    const res: any = await logApi.operList({ pageIndex: operPageIndex.value, pageSize: operPageSize.value, filters: [operBase.value, ...operFilters.value] })
    operData.value = res.list || []
    operTotal.value = res.total || 0
  } finally { operLoading.value = false }
}
function onOperSearch(q: Record<string, any>) {
  operFilters.value = q.filters && Array.isArray(q.filters) ? q.filters : []
  operPageIndex.value = 1
  loadOper()
}
function onOperReset() { operFilters.value = []; operPageIndex.value = 1; loadOper() }

// ===== Tab 切换:懒加载 =====
function switchTab(t: typeof tab.value) {
  tab.value = t
  if (loadedTabs.value.has(t)) return
  loadedTabs.value.add(t)
  if (t === 'tags') loadTags()
  else if (t === 'session') loadSessions()
  else if (t === 'login') loadLogin()
  else if (t === 'oper') loadOper()
}

onMounted(async () => {
  loadBasic()
  try {
    const v: any = await configApi.getByKey('Auth.IdCardVerifyPageEnabled')
    idCardPageEnabled.value = v !== 'false' && v !== false
  } catch { /* 默认 true */ }
})
</script>

<template>
  <TCard>
    <template #title>
      <TButton variant="ghost-secondary" size="sm" class="me-2" @click="router.back()"><i class="ti ti-arrow-left"></i>返回</TButton>
      <span>用户详情</span>
      <span v-if="basicInfo.userName" class="ms-2 text-muted">- {{ basicInfo.userName }} ({{ basicInfo.nickName }})</span>
    </template>
    <template #actions>
      <span v-if="basicInfo.userType" class="badge" :class="basicInfo.userType === 2 ? 'bg-info-lt' : 'bg-azure-lt'">{{ basicInfo.userType === 2 ? '内部' : '客户' }}</span>
      <span v-if="basicInfo.status !== undefined" class="badge" :class="basicInfo.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ basicInfo.status === 0 ? '启用' : '停用' }}</span>
    </template>

    <ul class="nav nav-tabs mb-3">
      <li class="nav-item"><button class="nav-link" :class="{ active: tab === 'basic' }" @click="switchTab('basic')">基本信息</button></li>
      <li class="nav-item" v-if="basicInfo.userType === 1"><button class="nav-link" :class="{ active: tab === 'tags' }" @click="switchTab('tags')">客户标签</button></li>
      <li class="nav-item"><button class="nav-link" :class="{ active: tab === 'session' }" @click="switchTab('session')">登录设备·会话</button></li>
      <li class="nav-item"><button class="nav-link" :class="{ active: tab === 'login' }" @click="switchTab('login')">登录日志</button></li>
      <li class="nav-item" v-perm="'system:operlog:list'"><button class="nav-link" :class="{ active: tab === 'oper' }" @click="switchTab('oper')">操作日志</button></li>
    </ul>

    <!-- 基本信息 -->
    <div v-show="tab === 'basic'">
      <div v-if="basicLoading" class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div>加载中...</div>
      <div class="datagrid mb-3">
        <div class="datagrid-item"><div class="datagrid-title">用户ID</div><div class="datagrid-content">{{ basicInfo.id }}</div></div>
        <div class="datagrid-item"><div class="datagrid-title">用户名</div><div class="datagrid-content">{{ basicInfo.userName }}</div></div>
        <div class="datagrid-item"><div class="datagrid-title">部门ID</div><div class="datagrid-content">{{ basicInfo.deptId ?? '-' }}</div></div>
        <div class="datagrid-item"><div class="datagrid-title">最后登录</div><div class="datagrid-content">{{ basicInfo.lastLoginAt ? formatTime(basicInfo.lastLoginAt) : '-' }}</div></div>
      </div>
      <TForm ref="formRef" v-model="formModel" :schemas="basicSchemas" />
      <div class="d-flex gap-2 mt-3">
        <TButton variant="primary" :loading="saving" @click="saveBasic"><i class="ti ti-device-floppy me-1"></i>保存基本信息</TButton>
        <TButton :variant="basicInfo.status === 0 ? 'warning' : 'success'" @click="toggleStatus">
          <i class="ti me-1" :class="basicInfo.status === 0 ? 'ti-ban' : 'ti-circle-check'"></i>{{ basicInfo.status === 0 ? '禁用用户(踢下线)' : '启用用户' }}
        </TButton>
      </div>

      <!-- 身份证实名验证(创蓝二要素) -->
      <div v-if="idCardPageEnabled" class="mt-4 pt-3 border-top">
        <h3 class="card-title mb-3"><i class="ti ti-id-badge me-1"></i>身份证实名验证</h3>
        <div class="alert alert-success" v-if="basicInfo.isIdCardVerified">
          <i class="ti ti-circle-check me-1"></i>该用户已实名认证
          <TButton variant="ghost-warning" size="sm" class="ms-3" @click="resetIdCard"><i class="ti ti-refresh me-1"></i>重置实名(换绑)</TButton>
        </div>
        <div v-else>
          <div class="row g-2 align-items-end">
            <div class="col-3"><label class="form-label">姓名</label><input class="form-control" v-model="idCard.name" placeholder="请输入姓名" /></div>
            <div class="col-4"><label class="form-label">身份证号</label><input class="form-control" v-model="idCard.idNum" placeholder="请输入身份证号" /></div>
            <div class="col-auto"><TButton variant="primary" :loading="idCard.verifying" @click="verifyIdCard"><i class="ti ti-shield-check me-1"></i>验证</TButton></div>
          </div>
          <div v-if="idCard.result" class="mt-2">
            <div v-if="idCard.result.success" class="alert alert-success mb-0">
              <strong>验证一致</strong>
              <span v-if="idCard.result.province" class="ms-3">{{ idCard.result.province }} {{ idCard.result.city }} {{ idCard.result.country }}</span>
              <span v-if="idCard.result.gender" class="ms-3">性别: {{ idCard.result.gender === '1' ? '男' : '女' }}</span>
              <span v-if="idCard.result.age" class="ms-3">年龄: {{ idCard.result.age }}</span>
            </div>
            <div v-else class="alert alert-warning mb-0">{{ idCard.result.remark || idCard.result.message || '验证未通过' }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- 客户标签(仅客户类型) -->
    <div v-show="tab === 'tags'">
      <div v-if="tagLoading" class="text-center text-muted py-4">加载中...</div>
      <div v-else>
        <div class="form-label small text-muted mb-2">预置标签</div>
        <div class="d-flex flex-wrap gap-3 mb-3">
          <label v-for="t in tagOptions" :key="t.id" class="form-check form-check-inline d-flex align-items-center gap-2">
            <input type="checkbox" class="form-check-input" :checked="checkedTags.has(t.id)" @change="toggleTag(t.id)" />
            <span class="badge" :class="t.listClass || 'bg-azure-lt'">{{ t.dictLabel }}</span>
          </label>
          <div v-if="!tagOptions.length" class="text-muted small">暂无预置标签</div>
        </div>
        <div class="form-label small text-muted mb-2">自定义标签(回车添加,最长 30 字符)</div>
        <div class="input-group input-group-sm" style="max-width: 360px">
          <input class="form-control" v-model="customInput" maxlength="30" placeholder="输入自定义标签文本" @keyup.enter="addCustomTag" />
          <TButton size="sm" variant="outline-secondary" @click="addCustomTag"><i class="ti ti-plus"></i>添加</TButton>
        </div>
        <div class="d-flex flex-wrap gap-1 mt-2">
          <span v-for="(t, idx) in customTags" :key="idx" class="badge bg-warning-lt d-flex align-items-center gap-1">
            {{ t }}
            <a href="#" class="text-muted" @click.prevent="removeCustomTag(idx)"><i class="ti ti-x"></i></a>
          </span>
        </div>
        <TButton variant="primary" :loading="tagSaving" class="mt-3" @click="saveTags"><i class="ti ti-device-floppy me-1"></i>保存标签</TButton>
      </div>
    </div>

    <!-- 登录设备·会话 -->
    <div v-show="tab === 'session'">
      <div class="d-flex justify-content-end mb-2">
        <TButton size="sm" variant="ghost-secondary" :loading="sessionLoading" @click="loadSessions"><i class="ti ti-refresh"></i></TButton>
        <TButton size="sm" variant="ghost-danger" @click="revokeAllSession"><i class="ti ti-logout me-1"></i>强制下线全部</TButton>
      </div>
      <div class="table-responsive">
        <TTable :columns="sessionColumns" :data="sessions" :loading="sessionLoading" empty-text="暂无活跃会话">
          <template #body-op="{ row }">
            <TButton variant="ghost-danger" size="sm" @click="forceRevoke(row)"><i class="ti ti-logout"></i>下线</TButton>
          </template>
        </TTable>
      </div>
    </div>

    <!-- 登录日志 -->
    <div v-show="tab === 'login'">
      <TSearchForm :schemas="loginSchemas" :advanced="true" @search="onLoginSearch" @reset="onLoginReset" />
      <div class="table-responsive">
        <TTable :columns="loginColumns" :data="loginData" :loading="loginLoading" :total="loginTotal" v-model:pageIndex="loginPageIndex" v-model:pageSize="loginPageSize" @change="loadLogin">
          <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '成功' : '失败' }}</span></template>
        </TTable>
      </div>
    </div>

    <!-- 操作日志(权限点控可见) -->
    <div v-show="tab === 'oper'" v-perm="'system:operlog:list'">
      <TSearchForm :schemas="operSchemas" :advanced="true" @search="onOperSearch" @reset="onOperReset" />
      <div class="table-responsive">
        <TTable :columns="operColumns" :data="operData" :loading="operLoading" :total="operTotal" v-model:pageIndex="operPageIndex" v-model:pageSize="operPageSize" @change="loadOper">
          <template #body-operStatus="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '成功' : '失败' }}</span></template>
        </TTable>
      </div>
    </div>
  </TCard>
</template>
