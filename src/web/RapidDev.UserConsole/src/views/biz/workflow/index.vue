<script setup lang="ts">
import { ElMessage } from 'element-plus'
/**
 * 工作流管理 —— 轻量顺序审批引擎。
 * 三大区块:我的待办(待我审批) / 我发起的(审批进度) / 流程定义(设计+部署+发起)。
 * 支持同意/拒绝/转办/撤回,实例详情含审批历史时间线。
 */
import { ref, computed, onMounted } from 'vue'
import { workflowApi } from '@/api/features'
import { roleApi } from '@/api/system'
import { TCard, TButton, TInput, TSelect, TModal } from '@/components/t'
import type { SysRole } from '@/types'

interface FlowNode { id: string; name: string; assigneeRoleKey: string }

const loading = ref(false)
const defs = ref<any[]>([])
const total = ref(0)
const myTasks = ref<any[]>([])
const myInstances = ref<any[]>([])

// 顶部统计
const stats = computed(() => ({
  todo: myTasks.value.length,
  running: myInstances.value.filter(i => i.status === 0).length,
  done: myInstances.value.filter(i => i.status === 1).length,
  rejected: myInstances.value.filter(i => i.status === 2).length,
}))

// 新建/编辑共用节点编辑器状态
const showDef = ref(false)
const editingId = ref(0)
const defForm = ref({ code: '', name: '', description: '' })
const nodes = ref<FlowNode[]>([])
let nodeSeq = 0

const showStart = ref(false)
const startForm = ref({ definitionId: 0, definitionName: '', businessKey: '', title: '', summary: '' })

// 审批弹窗(同意/拒绝/转办共用)
const showApprove = ref(false)
const approveForm = ref({ taskId: 0, action: 1 as 1 | 2, comment: '', title: '' })
// 转办
const showDelegate = ref(false)
const delegateForm = ref({ taskId: 0, toRoleKey: '', comment: '' })

// 实例详情抽屉
const showDetail = ref(false)
const detail = ref<{ instance: any; tasks: any[] } | null>(null)
const detailLoading = ref(false)

const roleOptions = ref<{ label: string; value: string }[]>([])

async function load() {
  loading.value = true
  try {
    // 并行拉取三个列表,避免串行延迟
    const [defRes, tasks, insts] = await Promise.all([
      workflowApi.definitions({ pageIndex: 1, pageSize: 50 }),
      workflowApi.myTasks(),
      workflowApi.myInstances(),
    ])
    defs.value = defRes.list || []; total.value = defRes.total
    myTasks.value = tasks || []
    myInstances.value = insts || []
  } finally { loading.value = false }
}

async function loadRoles() {
  try {
    const roles: SysRole[] = await roleApi.all()
    roleOptions.value = (roles || []).map(r => ({ label: r.roleName, value: r.roleKey }))
  } catch { roleOptions.value = [] }
}

// ---- 节点编辑器 ----
function addNode() {
  nodeSeq++
  nodes.value.push({ id: `node_${nodeSeq}`, name: `审批人${nodes.value.length + 1}`, assigneeRoleKey: '' })
}
function removeNode(i: number) { nodes.value.splice(i, 1) }
function moveNode(i: number, dir: -1 | 1) {
  const j = i + dir
  if (j < 0 || j >= nodes.value.length) return
  const arr = nodes.value
  const tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp
}
function buildFlowJson(): string {
  return JSON.stringify({
    nodes: nodes.value.map(n => ({ id: n.id, name: n.name, type: 'approve', assigneeRoleKey: n.assigneeRoleKey })),
    edges: [],
  })
}
function parseFlowJson(json: string | null | undefined) {
  nodes.value = []; nodeSeq = 0
  if (!json) return
  try {
    const o = JSON.parse(json)
    const arr: any[] = o?.nodes || []
    arr.forEach((n) => {
      nodeSeq++
      nodes.value.push({
        id: n.id || `node_${nodeSeq}`,
        name: n.name || '审批',
        assigneeRoleKey: n.assigneeRoleKey || n.assignee || '',
      })
    })
  } catch {}
}

// ---- 新建/编辑 ----
function openCreate() {
  editingId.value = 0
  defForm.value = { code: '', name: '', description: '' }
  nodes.value = []; nodeSeq = 0; addNode()
  showDef.value = true
}
function openEdit(d: any) {
  editingId.value = d.id
  defForm.value = { code: d.code || '', name: d.name || '', description: d.description || '' }
  parseFlowJson(d.flowJson)
  if (nodes.value.length === 0) addNode()
  showDef.value = true
}
async function saveDef() {
  if (!defForm.value.code.trim()) { ElMessage.error('请填写流程编码'); return }
  if (!defForm.value.name.trim()) { ElMessage.error('请填写流程名称'); return }
  if (nodes.value.length === 0) { ElMessage.error('请至少添加一个审批节点'); return }
  if (nodes.value.some(n => !n.name.trim())) { ElMessage.error('每个审批节点都需要名称'); return }
  const flowJson = buildFlowJson()
  try {
    if (editingId.value === 0) {
      await workflowApi.createDef({
        code: defForm.value.code.trim(), name: defForm.value.name.trim(),
        description: defForm.value.description, flowJson, status: 0,
      })
    } else {
      await workflowApi.updateDef(editingId.value, {
        code: defForm.value.code, name: defForm.value.name,
        description: defForm.value.description, flowJson,
      })
    }
    showDef.value = false
    await load()
  } catch (e: any) { ElMessage.error('保存失败: ' + (e?.message || '')) }
}
async function deploy(id: number) {
  try { await workflowApi.deploy(id); await load() }
  catch (e: any) { ElMessage.error('部署失败: ' + (e?.message || '')) }
}
async function removeDef(id: number) {
  if (!confirm('确认删除该流程定义?')) return
  try { await workflowApi.deleteDef(id); await load() }
  catch (e: any) { ElMessage.error('删除失败: ' + (e?.message || '')) }
}

// ---- 发起 ----
function openStart(d: any) {
  startForm.value = { definitionId: d.id, definitionName: d.name, businessKey: '', title: '', summary: '' }
  showStart.value = true
}
async function start() {
  if (!startForm.value.title.trim()) { ElMessage.error('请填写标题'); return }
  const bizKey = startForm.value.businessKey.trim() || `wf:${Date.now()}`
  try {
    await workflowApi.start({
      definitionId: startForm.value.definitionId,
      businessKey: bizKey,
      title: startForm.value.title.trim(),
      summary: startForm.value.summary,
    })
    showStart.value = false
    await load() // 重新拉取,确保"我发起的"立即出现
  } catch (e: any) {
    // 不再吞错:发起失败必须提示(之前空 catch 导致用户以为成功,操作旧任务报"不存在")
    ElMessage.error('发起失败: ' + (e?.message || '请检查流程是否已部署'))
  }
}

// ---- 审批(同意/拒绝) ----
function openApprove(task: any, action: 1 | 2) {
  approveForm.value = { taskId: task.task.id, action, comment: '', title: task.title }
  showApprove.value = true
}
async function confirmApprove() {
  if (approveForm.value.action === 2 && !approveForm.value.comment.trim()) {
    ElMessage.error('拒绝时审批意见必填'); return
  }
  try {
    await workflowApi.approve(approveForm.value.taskId, {
      action: approveForm.value.action,
      comment: approveForm.value.comment || '',
    })
    showApprove.value = false
    await load()
  } catch (e: any) { ElMessage.error('审批失败: ' + (e?.message || '')) }
}

// ---- 转办 ----
function openDelegate(task: any) {
  delegateForm.value = { taskId: task.task.id, toRoleKey: '', comment: '' }
  showDelegate.value = true
}
async function confirmDelegate() {
  if (!delegateForm.value.toRoleKey) { ElMessage.error('请选择转办角色'); return }
  try {
    await workflowApi.delegate(delegateForm.value.taskId, {
      toRoleKey: delegateForm.value.toRoleKey,
      comment: delegateForm.value.comment || '',
    })
    showDelegate.value = false
    await load()
  } catch (e: any) { ElMessage.error('转办失败: ' + (e?.message || '')) }
}

// ---- 撤回 ----
async function recall(instanceId: number, title: string) {
  if (!confirm(`确认撤回「${title}」?撤回后流程终止,无法恢复。`)) return
  try { await workflowApi.recall(instanceId); await load() }
  catch (e: any) { ElMessage.error('撤回失败: ' + (e?.message || '')) }
}

// ---- 实例详情 ----
async function openDetail(instanceId: number) {
  showDetail.value = true
  detail.value = null
  detailLoading.value = true
  try {
    const res: any = await workflowApi.instance(instanceId)
    detail.value = { instance: res.instance, tasks: res.tasks || [] }
  } catch (e: any) { ElMessage.error('加载详情失败: ' + (e?.message || '')) }
  finally { detailLoading.value = false }
}

// ---- 状态工具 ----
function instStatusText(s: number) {
  return { 0: '审批中', 1: '已通过', 2: '已拒绝', 3: '已撤回' }[s] ?? '-'
}
function instStatusClass(s: number) {
  return { 0: 'bg-info-lt', 1: 'bg-success-lt', 2: 'bg-danger-lt', 3: 'bg-secondary-lt' }[s] ?? 'bg-secondary-lt'
}
function actionText(a?: number) {
  return { 1: '同意', 2: '拒绝', 3: '转办', 4: '撤回' }[a ?? -1] ?? (a == null ? '待处理' : '-')
}
function actionClass(a?: number) {
  return { 1: 'bg-success-lt', 2: 'bg-danger-lt', 3: 'bg-warning-lt', 4: 'bg-secondary-lt' }[a ?? -1] ?? 'bg-info-lt'
}
function fmtDate(s?: string) {
  if (!s) return '-'
  return s.replace('T', ' ').slice(0, 16)
}

onMounted(() => { load(); loadRoles() })
</script>

<template>
  <div class="workflow-page">
    <!-- 概览统计 -->
    <div class="row g-3 mb-3">
      <div class="col-6 col-md-3">
        <TCard>
          <div class="d-flex align-items-center">
            <span class="avatar bg-info-lt me-3"><i class="ti ti-inbox"></i></span>
            <div><div class="text-muted small">我的待办</div><div class="h3 mb-0">{{ stats.todo }}</div></div>
          </div>
        </TCard>
      </div>
      <div class="col-6 col-md-3">
        <TCard>
          <div class="d-flex align-items-center">
            <span class="avatar bg-warning-lt me-3"><i class="ti ti-clock"></i></span>
            <div><div class="text-muted small">审批中</div><div class="h3 mb-0">{{ stats.running }}</div></div>
          </div>
        </TCard>
      </div>
      <div class="col-6 col-md-3">
        <TCard>
          <div class="d-flex align-items-center">
            <span class="avatar bg-success-lt me-3"><i class="ti ti-circle-check"></i></span>
            <div><div class="text-muted small">已通过</div><div class="h3 mb-0">{{ stats.done }}</div></div>
          </div>
        </TCard>
      </div>
      <div class="col-6 col-md-3">
        <TCard>
          <div class="d-flex align-items-center">
            <span class="avatar bg-danger-lt me-3"><i class="ti ti-circle-x"></i></span>
            <div><div class="text-muted small">已拒绝</div><div class="h3 mb-0">{{ stats.rejected }}</div></div>
          </div>
        </TCard>
      </div>
    </div>

    <!-- 我的待办 -->
    <TCard class="mb-3">
      <template #title><i class="ti ti-inbox me-2"></i>我的待办 <span class="badge bg-info-lt ms-1">{{ myTasks.length }}</span></template>
      <div class="table-responsive">
        <table class="table table-vcenter table-hover">
          <thead><tr><th>审批标题</th><th>当前节点</th><th>发起人</th><th>状态</th><th class="text-end">操作</th></tr></thead>
          <tbody>
            <tr v-for="t in myTasks" :key="t.task.id">
              <td>
                <a href="javascript:;" class="text-reset text-decoration-none fw-semibold" @click="openDetail(t.task.instanceId)">{{ t.title || '-' }}</a>
                <div class="text-muted small">{{ t.task.nodeName }}</div>
              </td>
              <td><span class="badge bg-primary-lt">{{ t.task.nodeName }}</span></td>
              <td>{{ t.initiatorName || '-' }}</td>
              <td><span class="badge" :class="instStatusClass(0)">{{ instStatusText(t.instanceStatus as any as number) }}</span></td>
              <td class="text-end text-nowrap">
                <TButton variant="success" size="sm" @click="openApprove(t, 1)"><i class="ti ti-check me-1"></i>同意</TButton>
                <TButton variant="danger" size="sm" @click="openApprove(t, 2)"><i class="ti ti-x me-1"></i>拒绝</TButton>
                <TButton variant="outline-warning" size="sm" @click="openDelegate(t)"><i class="ti ti-user-share me-1"></i>转办</TButton>
              </td>
            </tr>
            <tr v-if="myTasks.length === 0"><td colspan="5" class="text-muted text-center py-4"><i class="ti ti-checks me-1"></i>暂无待办</td></tr>
          </tbody>
        </table>
      </div>
    </TCard>

    <!-- 我发起的 -->
    <TCard class="mb-3">
      <template #title><i class="ti ti-user-up me-2"></i>我发起的 <span class="badge bg-secondary-lt ms-1">{{ myInstances.length }}</span></template>
      <div class="table-responsive">
        <table class="table table-vcenter table-hover">
          <thead><tr><th>标题</th><th>业务键</th><th>状态</th><th>发起时间</th><th>结束时间</th><th class="text-end">操作</th></tr></thead>
          <tbody>
            <tr v-for="i in myInstances" :key="i.id">
              <td>
                <a href="javascript:;" class="text-reset text-decoration-none fw-semibold" @click="openDetail(i.id)">{{ i.title || '-' }}</a>
                <div v-if="i.summary" class="text-muted small text-truncate" style="max-width: 320px">{{ i.summary }}</div>
              </td>
              <td><code class="small">{{ i.businessKey }}</code></td>
              <td><span class="badge" :class="instStatusClass(i.status)">{{ instStatusText(i.status) }}</span></td>
              <td class="small">{{ fmtDate(i.createdAt) }}</td>
              <td class="small">{{ fmtDate(i.finishedAt) }}</td>
              <td class="text-end text-nowrap">
                <TButton variant="outline-secondary" size="sm" @click="openDetail(i.id)"><i class="ti ti-eye me-1"></i>详情</TButton>
                <TButton v-if="i.status === 0" variant="outline-danger" size="sm" @click="recall(i.id, i.title)"><i class="ti ti-arrow-back-up me-1"></i>撤回</TButton>
              </td>
            </tr>
            <tr v-if="myInstances.length === 0"><td colspan="6" class="text-muted text-center py-4">暂未发起过审批</td></tr>
          </tbody>
        </table>
      </div>
    </TCard>

    <!-- 流程定义 -->
    <TCard>
      <template #title><i class="ti ti-subtask me-2"></i>流程定义</template>
      <template #actions>
        <TButton variant="primary" @click="openCreate"><i class="ti ti-plus me-2"></i>新建流程</TButton>
      </template>
      <div class="table-responsive">
        <table class="table table-vcenter table-hover">
          <thead><tr><th>编码</th><th>名称</th><th>版本</th><th>状态</th><th class="text-end">操作</th></tr></thead>
          <tbody>
            <tr v-for="d in defs" :key="d.id">
              <td><code>{{ d.code }}</code></td>
              <td>{{ d.name }}</td>
              <td><span class="badge bg-azure-lt">v{{ d.version }}</span></td>
              <td><span class="badge" :class="d.status === 1 ? 'bg-success-lt' : 'bg-secondary-lt'">{{ d.status === 1 ? '已部署' : '草稿' }}</span></td>
              <td class="text-end text-nowrap">
                <TButton v-if="d.status !== 1" variant="outline-secondary" size="sm" @click="deploy(d.id)"><i class="ti ti-rocket me-1"></i>部署</TButton>
                <TButton variant="outline-primary" size="sm" @click="openEdit(d)"><i class="ti ti-edit me-1"></i>编辑</TButton>
                <TButton variant="outline-primary" size="sm" @click="openStart(d)"><i class="ti ti-player-play me-1"></i>发起</TButton>
                <TButton variant="outline-danger" size="sm" @click="removeDef(d.id)"><i class="ti ti-trash"></i></TButton>
              </td>
            </tr>
            <tr v-if="defs.length === 0"><td colspan="5" class="text-muted text-center py-4">暂无流程定义</td></tr>
          </tbody>
        </table>
      </div>
    </TCard>

    <!-- 新建/编辑流程(节点编辑器) -->
    <TModal v-model="showDef" :title="editingId === 0 ? '新建流程定义' : '编辑流程定义'" size="xl">
      <div class="row g-3 mb-3">
        <div class="col-md-4"><label class="form-label">流程编码</label><TInput v-model="defForm.code" placeholder="如:leave-approval" /></div>
        <div class="col-md-4"><label class="form-label">流程名称</label><TInput v-model="defForm.name" placeholder="如:请假审批" /></div>
        <div class="col-md-4"><label class="form-label">描述</label><TInput v-model="defForm.description" /></div>
      </div>

      <div class="d-flex align-items-center justify-content-between mb-2">
        <label class="form-label mb-0"><i class="ti ti-list-numbers me-1"></i>审批节点(按顺序流转)</label>
        <TButton variant="outline-primary" size="sm" @click="addNode"><i class="ti ti-plus me-1"></i>添加审批节点</TButton>
      </div>

      <div v-if="nodes.length === 0" class="text-muted text-center border rounded py-4 mb-3">
        暂无节点,点击右上角「添加审批节点」开始
      </div>

      <div v-for="(n, i) in nodes" :key="n.id" class="border rounded p-2 mb-2 d-flex align-items-center gap-2">
        <span class="badge bg-primary-lt fs-5">{{ i + 1 }}</span>
        <div class="flex-grow-1 row g-2 m-0">
          <div class="col-md-6 ps-0">
            <label class="form-label small text-muted mb-0">节点名称</label>
            <TInput v-model="n.name" placeholder="如:部门经理审批" />
          </div>
          <div class="col-md-6">
            <label class="form-label small text-muted mb-0">审批角色</label>
            <TSelect v-model="n.assigneeRoleKey" :options="roleOptions" placeholder="选择角色(该角色用户可见待办)" />
          </div>
        </div>
        <div class="d-flex flex-column">
          <TButton variant="ghost-secondary" size="sm" :disabled="i === 0" @click="moveNode(i, -1)"><i class="ti ti-chevron-up"></i></TButton>
          <TButton variant="ghost-secondary" size="sm" :disabled="i === nodes.length - 1" @click="moveNode(i, 1)"><i class="ti ti-chevron-down"></i></TButton>
        </div>
        <TButton variant="ghost-danger" size="sm" @click="removeNode(i)"><i class="ti ti-trash"></i></TButton>
      </div>

      <div class="alert alert-info small mb-3">
        <i class="ti ti-info-circle me-1"></i>角色可见策略:每个节点的待办对该角色所有用户可见,谁先处理谁领取。节点按顺序流转,全部通过即审批完成。
      </div>

      <div class="text-end mt-3">
        <TButton variant="outline-secondary" class="me-2" @click="showDef = false">取消</TButton>
        <TButton variant="primary" @click="saveDef">{{ editingId === 0 ? '创建' : '保存' }}</TButton>
      </div>
    </TModal>

    <!-- 发起审批 -->
    <TModal v-model="showStart" title="发起审批" size="md">
      <div class="mb-3">
        <label class="form-label">流程</label>
        <div class="form-control-plaintext"><i class="ti ti-subtask me-1"></i>{{ startForm.definitionName }}</div>
      </div>
      <div class="mb-3"><label class="form-label">业务键</label><TInput v-model="startForm.businessKey" placeholder="如:leave:123(留空自动生成)" /></div>
      <div class="mb-3"><label class="form-label">标题 <span class="text-danger">*</span></label><TInput v-model="startForm.title" placeholder="如:张三的请假申请" /></div>
      <div class="mb-3"><label class="form-label">摘要</label><textarea class="form-control" v-model="startForm.summary" rows="3" placeholder="审批事由说明(选填)"></textarea></div>
      <div class="text-end"><TButton variant="primary" @click="start"><i class="ti ti-player-play me-1"></i>发起</TButton></div>
    </TModal>

    <!-- 审批(同意/拒绝) -->
    <TModal v-model="showApprove" :title="approveForm.action === 1 ? '同意审批' : '拒绝审批'" size="md">
      <div class="mb-2">
        <span class="text-muted small">审批事项:</span>
        <span class="fw-semibold">{{ approveForm.title }}</span>
      </div>
      <div class="mb-3 text-muted small">
        <i class="ti me-1" :class="approveForm.action === 1 ? 'ti-circle-check text-success' : 'ti-circle-x text-danger'"></i>
        {{ approveForm.action === 1 ? '确认同意当前节点,流程将流转至下一审批人' : '确认拒绝,流程将终止并通知发起人' }}
      </div>
      <div class="mb-3">
        <label class="form-label">审批意见 <span v-if="approveForm.action === 2" class="text-danger">*</span><span v-else class="text-muted">(选填)</span></label>
        <textarea class="form-control" v-model="approveForm.comment" rows="4" placeholder="请输入审批意见"></textarea>
      </div>
      <div class="text-end">
        <TButton variant="outline-secondary" class="me-2" @click="showApprove = false">取消</TButton>
        <TButton :variant="approveForm.action === 1 ? 'success' : 'danger'" @click="confirmApprove">{{ approveForm.action === 1 ? '确认同意' : '确认拒绝' }}</TButton>
      </div>
    </TModal>

    <!-- 转办 -->
    <TModal v-model="showDelegate" title="转办" size="md">
      <div class="alert alert-warning small">
        <i class="ti ti-info-circle me-1"></i>转办后当前待办将转交给目标角色,该角色所有用户可见并处理。
      </div>
      <div class="mb-3">
        <label class="form-label">转办给角色 <span class="text-danger">*</span></label>
        <TSelect v-model="delegateForm.toRoleKey" :options="roleOptions" placeholder="选择目标角色" />
      </div>
      <div class="mb-3">
        <label class="form-label">转办说明 <span class="text-muted">(选填)</span></label>
        <textarea class="form-control" v-model="delegateForm.comment" rows="3" placeholder="转办原因说明"></textarea>
      </div>
      <div class="text-end">
        <TButton variant="outline-secondary" class="me-2" @click="showDelegate = false">取消</TButton>
        <TButton variant="warning" @click="confirmDelegate"><i class="ti ti-user-share me-1"></i>确认转办</TButton>
      </div>
    </TModal>

    <!-- 实例详情(审批历史时间线) -->
    <TModal v-model="showDetail" title="审批详情" size="lg">
      <div v-if="detailLoading" class="text-center py-4"><i class="ti ti-loader ti-spin me-1"></i>加载中...</div>
      <template v-else-if="detail && detail.instance">
        <div class="row g-3 mb-3">
          <div class="col-md-8">
            <h5 class="mb-1">{{ detail.instance.title }}</h5>
            <div class="text-muted small">{{ detail.instance.summary || '无摘要' }}</div>
          </div>
          <div class="col-md-4 text-md-end">
            <span class="badge" :class="instStatusClass(detail.instance.status)">{{ instStatusText(detail.instance.status) }}</span>
            <div class="text-muted small mt-1">业务键:<code>{{ detail.instance.businessKey }}</code></div>
          </div>
        </div>
        <div class="row g-2 mb-4 small">
          <div class="col-md-4"><span class="text-muted">发起人:</span> {{ detail.instance.initiatorName || '-' }}</div>
          <div class="col-md-4"><span class="text-muted">发起时间:</span> {{ fmtDate(detail.instance.createdAt) }}</div>
          <div class="col-md-4"><span class="text-muted">结束时间:</span> {{ fmtDate(detail.instance.finishedAt) }}</div>
        </div>

        <h6 class="mb-3"><i class="ti ti-history me-1"></i>审批轨迹</h6>
        <div class="timeline">
          <div v-for="(t, i) in detail.tasks" :key="t.id" class="timeline-item">
            <div class="timeline-badge" :class="actionClass(t.action)"><i class="ti" :class="t.action === 1 ? 'ti-check' : t.action === 2 ? 'ti-x' : t.action === 3 ? 'ti-user-share' : t.action === 4 ? 'ti-arrow-back-up' : 'ti-clock'"></i></div>
            <div class="timeline-body">
              <div class="d-flex justify-content-between align-items-start">
                <div>
                  <span class="fw-semibold">{{ t.nodeName }}</span>
                  <span class="badge ms-1" :class="actionClass(t.action)">{{ actionText(t.action) }}</span>
                </div>
                <span class="text-muted small">{{ fmtDate(t.completedAt) }}</span>
              </div>
              <div class="text-muted small">
                <span v-if="t.assigneeName">审批人:{{ t.assigneeName }}</span>
                <span v-else-if="t.assigneeRoleKey">待办角色:{{ t.assigneeRoleKey }}</span>
                <span v-else>未指派</span>
                <span v-if="t.isCurrent" class="badge bg-info-lt ms-1">当前</span>
              </div>
              <div v-if="t.comment" class="small mt-1 p-2 bg-light rounded">{{ t.comment }}</div>
            </div>
          </div>
          <div v-if="!detail.tasks.length" class="text-muted text-center py-3">暂无审批记录</div>
        </div>
      </template>
      <div v-else class="text-center py-4 text-muted">未找到实例</div>
    </TModal>
  </div>
</template>

<style scoped>
.timeline { position: relative; padding-left: 1.5rem; }
.timeline::before { content: ''; position: absolute; left: 11px; top: 0; bottom: 0; width: 2px; background: var(--el-border-color); }
.timeline-item { position: relative; padding-bottom: 1.25rem; }
.timeline-item:last-child { padding-bottom: 0; }
.timeline-badge { position: absolute; left: -1.5rem; width: 24px; height: 24px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; font-size: 0.8rem; }
.timeline-body { padding-left: 0.5rem; }
</style>
