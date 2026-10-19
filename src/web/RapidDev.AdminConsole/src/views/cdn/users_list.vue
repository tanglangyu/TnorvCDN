<script setup lang="ts">
/** 用户列表 —— 对接 TnorvCDN 主控 /users */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/users'

// 分页 + 关键词筛选（后端 /admin/users 支持 page/pageSize/q）
const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const data = await cdnApi.get(apiUrl, {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    q: filterQ.value || undefined,
  })
  const arr = normalize(data)
  if (data && typeof data === 'object' && !Array.isArray(data) && 'total' in data) {
    pager.value.total = data.total ?? arr.length
  } else {
    pager.value.total = arr.length
  }
  rows.value = arr
  return rows.value
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

// A5：行展开加载该用户摘要（站点列表 + 套餐 + 余额）
const summaries = ref<Record<number, any>>({})
const expandLoading = ref(0)
async function onExpand(row: any, expanded: any[]) {
  if (!expanded.some((x: any) => x.id === row.id)) return
  if (summaries.value[row.id]) return
  expandLoading.value = row.id
  try {
    const r = (await cdnApi.get(`${apiUrl}/${row.id}/summary`)) as any
    summaries.value[row.id] = r?.data ?? r
  } catch { ElMessage.error('加载用户摘要失败') }
  finally { expandLoading.value = 0 }
}

function openAdd() {
  form.value = { username: '', password: '', isAdmin: false, enabled: true }
  dialog.value = true
}

const rowFeatures = ref<any>(null)
function openEdit(row: any) {
  form.value = { id: row.id, username: row.username, password: '', isAdmin: row.isAdmin, enabled: row.enabled, smsPhoneBare: '' }
  rowFeatures.value = null
  loadFeatModes({})
  // 拉详情回填手机号与功能开关
  cdnApi.get(apiUrl + '/' + row.id + '/detail').then((d: any) => {
    const dd = d?.data ?? d
    if (!dd) return
    form.value.smsPhoneBare = dd.smsPhone ? String(dd.smsPhone).replace(/^00\d{2,4}/, '').replace(/\*+/g, '') : ''
    rowFeatures.value = dd.features ?? null
    loadFeatModes(dd.features)
  }).catch(() => {})
  dialog.value = true
}

async function save() {
  if (!form.value.id && !form.value.username) return ElMessage.warning('请填写用户名')
  try {
    if (form.value.id) {
      const body: any = { enabled: form.value.enabled, isAdmin: form.value.isAdmin, features: buildFeaturesJson() }
      if (form.value.password) body.password = form.value.password
      body.smsPhone = (form.value.smsPhoneBare || '').trim()
      await cdnApi.put(`${apiUrl}/${form.value.id}`, body)
    } else {
      await cdnApi.post(apiUrl, { username: form.value.username, password: form.value.password, isAdmin: form.value.isAdmin })
    }
    ElMessage.success('已保存')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
}

async function toggle(row: any) {
  try {
    await cdnApi.put(`${apiUrl}/${row.id}`, { enabled: !row.enabled })
    ElMessage.success(row.enabled ? '已停用' : '已启用')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || '操作失败') }
}

function normalize(data: any): any[] {
  if (Array.isArray(data)) return data
  if (data?.items) return data.items
  if (data?.probes) return data.probes
  if (data?.byReason) return data.byReason
  if (data?.topCcIps) return data.topCcIps
  if (typeof data === 'object' && data !== null) return [data]
  return []
}

const cols: any[] = [
  {k:"id",t:"ID",w:70},
  {k:"username",t:"账号",min:140},
  {k:"smsPhone",t:"手机号",w:150},
  {k:"balance",t:"余额",w:110,fmt:"yuan"},
  {k:"serverCount",t:"站点数",w:90},
  {k:"sessionCount",t:"在线会话",w:100},
  {k:"isAdmin",t:"管理员",w:90,fmt:"bool"},
  {k:"enabled",t:"启用",w:80,fmt:"bool"},
  {k:"createdAt",t:"创建时间",w:170,fmt:"time"},
]

// ---- 用户级功能开关（三态：null 跟随套餐 / true / false）----
const FEATURE_LABELS: [string, string][] = [
  ['cache', '缓存加速'], ['https', 'HTTPS/SSL'], ['compression', '内容压缩'], ['webp', 'WebP 自适应'],
  ['websocket', 'WebSocket'], ['grpc', 'gRPC 回源'], ['customPages', '自定义页面'],
  ['waf', 'WAF 规则'], ['cc', 'CC 防护'], ['jsShield', '5秒盾'], ['urlAuth', 'URL 鉴权'],
  ['referers', '防盗链'], ['edgeScripts', '边缘脚本'], ['aiGuard', 'AI 语义防护'],
  ['locations', '路径规则'], ['lb', '负载均衡(L4)'], ['ns', '智能DNS'], ['antiDdos', '高防'],
  ['api', 'API 密钥'], ['tickets', '工单'], ['finance', '财务'],
]
const featMode = ref<Record<string, string>>({})   // 'inherit' | 'on' | 'off'
function loadFeatModes(features: any) {
  const m: Record<string, string> = {}
  for (const [k] of FEATURE_LABELS) m[k] = features && typeof features === 'object' && k in features ? (features[k] ? 'on' : 'off') : 'inherit'
  featMode.value = m
}
function buildFeaturesJson() {
  const obj: any = {}
  let touched = false
  for (const [k] of FEATURE_LABELS) {
    if (featMode.value[k] !== 'inherit') { obj[k] = featMode.value[k] === 'on'; touched = true }
  }
  return touched ? obj : null
}

// ---- 详情抽屉 ----
const drawer = ref(false)
const detail = ref<any>(null)
const detailLoading = ref(false)
const detailTab = ref('base')
async function openDetail(row: any) {
  detail.value = null; detailTab.value = 'base'; drawer.value = true; detailLoading.value = true
  try { detail.value = (await cdnApi.get(apiUrl + '/' + row.id + '/detail')) as any }
  catch { ElMessage.error('加载用户详情失败') } finally { detailLoading.value = false }
}

// ---- 余额调整 ----
const balDialog = ref(false)
const balForm = ref({ userId: 0, username: '', amount: 0, reason: '' })
function openBalance(row: any) {
  balForm.value = { userId: row.id, username: row.username, amount: 0, reason: '' }
  balDialog.value = true
}
async function saveBalance() {
  if (!balForm.value.amount || !balForm.value.reason.trim()) return ElMessage.warning('请填写金额与调整原因')
  try {
    await cdnApi.post('/admin/finance/adjust-balance', { userId: balForm.value.userId, deltaCents: Math.round(balForm.value.amount * 100), reason: balForm.value.reason })
    ElMessage.success('余额已调整'); balDialog.value = false; gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || '调整失败') }
}

// ---- 强制下线 ----
async function forceLogout(row: any) {
  try { await ElMessageBox.confirm('确定强制下线用户 ' + row.username + ' 的全部登录会话？', '强制下线', { type: 'warning' }) } catch { return }
  try {
    const r: any = await cdnApi.post(apiUrl + '/' + row.id + '/force-logout', {})
    ElMessage.success('已下线 ' + (r?.revoked ?? 0) + ' 个会话')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || '操作失败') }
}

// ---- 快捷重置密码 ----
const pwdDialog = ref(false)
const pwdForm = ref({ userId: 0, username: '', password: '' })
function openResetPwd(row: any) {
  pwdForm.value = { userId: row.id, username: row.username, password: '' }
  pwdDialog.value = true
}
async function saveResetPwd() {
  if ((pwdForm.value.password || '').length < 6) return ElMessage.warning('密码至少 6 位')
  try {
    await cdnApi.put(apiUrl + '/' + pwdForm.value.userId, { password: pwdForm.value.password })
    ElMessage.success('密码已重置'); pwdDialog.value = false
  } catch (e: any) { ElMessage.error(e?.response?.data || '重置失败') }
}

function fmtVal(row: any, col: any) {
  const v = row[col.k]
  if (col.fmt === 'time') return v ? new Date(v).toLocaleString() : '-'
  if (col.fmt === 'bytes') return v >= 1073741824 ? (v / 1073741824).toFixed(2) + ' GB' : v >= 1048576 ? (v / 1048576).toFixed(1) + ' MB' : (v || 0) + ' B'
  if (col.fmt === 'bool') return v ? '是' : '否'
  if (col.fmt === 'yuan') return '¥' + ((v || 0) / 100).toFixed(2)
  if (col.fmt === 'join') return Array.isArray(v) ? v.join(', ') : v
  return v ?? '-'
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">用户列表</h2><div class="text-muted">TnorvCDN · 用户列表</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center flex-wrap">
            <el-button type="primary" size="small" @click="openAdd()">+ 创建用户</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="用户名 / 手机号" size="small" clearable style="width: 220px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
          </div>
          <el-table :data="rows" stripe @expand-change="onExpand">
            <el-table-column type="expand">
              <template #default="{ row }">
                <div v-loading="expandLoading === row.id" style="padding: 8px 24px">
                  <template v-if="summaries[row.id]">
                    <div class="d-flex gap-4 flex-wrap mb-2">
                      <el-tag type="warning" size="small">余额 ¥{{ ((summaries[row.id].balanceCents || 0) / 100).toFixed(2) }}</el-tag>
                      <el-tag size="small">站点 {{ (summaries[row.id].servers || []).length }} 个</el-tag>
                      <el-tag v-for="p in summaries[row.id].plans || []" :key="p.planName" type="success" size="small">
                        {{ p.planName }}（至 {{ new Date(p.expiresAt).toLocaleDateString() }}）
                      </el-tag>
                      <el-tag v-if="!(summaries[row.id].plans || []).length" type="info" size="small">无生效套餐</el-tag>
                    </div>
                    <el-table :data="summaries[row.id].servers || []" size="small" border>
                      <el-table-column label="域名" min-width="220">
                        <template #default="{ row: sv }">{{ (sv.domains || []).join(', ') || '—' }}</template>
                      </el-table-column>
                      <el-table-column prop="serverType" label="类型" width="90" />
                      <el-table-column label="启用" width="80">
                        <template #default="{ row: sv }"><el-tag size="small" :type="sv.enabled ? 'success' : 'info'">{{ sv.enabled ? '是' : '否' }}</el-tag></template>
                      </el-table-column>
                      <el-table-column label="SSL" width="110">
                        <template #default="{ row: sv }">{{ sv.status || '—' }}</template>
                      </el-table-column>
                    </el-table>
                    <el-empty v-if="!(summaries[row.id].servers || []).length" description="该用户暂无站点" :image-size="50" />
                  </template>
                </div>
              </template>
            </el-table-column>
            <el-table-column v-for="col in cols" :key="col.k" :label="col.t" :width="col.w" :min-width="col.min">
              <template #default="{ row }">{{ fmtVal(row, col) }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
                <el-button size="small" @click="openDetail(row)">详情</el-button>
                <el-button size="small" @click="openEdit(row)">编辑</el-button>
                <el-button size="small" :type="row.enabled ? 'warning' : 'success'" plain @click="toggle(row)">{{ row.enabled ? '停用' : '启用' }}</el-button>
                <el-button size="small" type="primary" plain @click="openBalance(row)">调余额</el-button>
                <el-button size="small" type="warning" plain @click="openResetPwd(row)">重置密码</el-button>
                <el-button size="small" type="danger" plain @click="forceLogout(row)">强下线</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />

        <el-dialog v-model="dialog" :title="form.id ? '编辑用户' : '创建用户'" width="480px">
          <el-form label-width="100px">
            <el-form-item label="用户名"><el-input v-model="form.username" :disabled="!!form.id" /></el-form-item>
            <el-form-item :label="form.id ? '重置密码' : '密码'">
              <el-input v-model="form.password" type="password" show-password :placeholder="form.id ? '留空不改' : '必填：新用户登录密码'" />
            </el-form-item>
            <el-form-item label="管理员"><el-switch v-model="form.isAdmin" /></el-form-item>
            <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
            <el-form-item label="绑定手机号">
              <el-input v-model="form.smsPhoneBare" placeholder="输入 11 位手机号绑定/换绑；清空并保存则解绑" maxlength="11" />
            </el-form-item>
          </el-form>
          <el-divider content-position="left">用户级功能开关（跟随套餐 / 强制开 / 强制关）</el-divider>
          <div class="d-flex flex-wrap" style="gap: 8px 4px">
            <div v-for="[k, label] in FEATURE_LABELS" :key="k" class="d-inline-flex align-items-center" style="gap: 6px; width: 200px">
              <span style="font-size: 12px">{{ label }}</span>
              <el-radio-group v-model="featMode[k]" size="small">
                <el-radio-button value="inherit">跟随</el-radio-button>
                <el-radio-button value="on">开</el-radio-button>
                <el-radio-button value="off">关</el-radio-button>
              </el-radio-group>
            </div>
          </div>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" @click="save">保存</el-button>
          </template>
        </el-dialog>
          <el-empty v-if="!rows.length" description="暂无数据" />

        <!-- 用户详情抽屉 -->
        <el-drawer v-model="drawer" :title="detail ? ('用户详情 · ' + detail.username) : '用户详情'" size="620px">
          <div v-loading="detailLoading">
            <template v-if="detail">
              <el-tabs v-model="detailTab">
                <el-tab-pane label="基本信息" name="base">
                  <el-descriptions :column="1" border size="small">
                    <el-descriptions-item label="ID">{{ detail.id }}</el-descriptions-item>
                    <el-descriptions-item label="账号">{{ detail.username }}</el-descriptions-item>
                    <el-descriptions-item label="手机号">{{ detail.smsPhone || '未绑定' }}</el-descriptions-item>
                    <el-descriptions-item label="余额">¥{{ ((detail.balanceCents || 0) / 100).toFixed(2) }}</el-descriptions-item>
                    <el-descriptions-item label="管理员">{{ detail.isAdmin ? '是' : '否' }} · {{ detail.enabled ? '已启用' : '已停用' }}</el-descriptions-item>
                    <el-descriptions-item label="实名认证">
                      <template v-if="detail.identity">{{ detail.identity.type === 'enterprise' ? '企业' : '个人' }} · {{ detail.identity.realName || detail.identity.enterpriseName }} · {{ detail.identity.status === 'approved' ? '已通过' : detail.identity.status === 'pending' ? '待审核' : '已驳回' }}</template>
                      <template v-else>未提交</template>
                    </el-descriptions-item>
                    <el-descriptions-item label="注册时间">{{ new Date(detail.createdAt).toLocaleString() }}</el-descriptions-item>
                    <el-descriptions-item label="提醒订阅">{{ (detail.smsEvents && JSON.parse(detail.smsEvents).length) ? JSON.parse(detail.smsEvents).join(', ') : '未订阅' }}</el-descriptions-item>
                  </el-descriptions>
                </el-tab-pane>
                <el-tab-pane label="站点与套餐" name="plans">
                  <h6 class="mb-2">订阅（{{ (detail.subscriptions || []).length }}）</h6>
                  <el-table :data="detail.subscriptions || []" size="small" border class="mb-3">
                    <el-table-column prop="planName" label="套餐" />
                    <el-table-column label="状态" width="80"><template #default="{ row }"><el-tag size="small" :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '生效' : '停用' }}</el-tag></template></el-table-column>
                    <el-table-column label="自动续费" width="90"><template #default="{ row }">{{ row.autoRenew ? '是' : '否' }}</template></el-table-column>
                    <el-table-column label="到期" width="110"><template #default="{ row }">{{ new Date(row.expiresAt).toLocaleDateString() }}</template></el-table-column>
                  </el-table>
                  <h6 class="mb-2">站点（{{ (detail.servers || []).length }}）</h6>
                  <el-table :data="detail.servers || []" size="small" border>
                    <el-table-column label="域名" min-width="200"><template #default="{ row }">{{ (row.domains || []).join(', ') || '—' }}</template></el-table-column>
                    <el-table-column prop="serverType" label="类型" width="90" />
                    <el-table-column label="启用" width="70"><template #default="{ row }">{{ row.enabled ? '是' : '否' }}</template></el-table-column>
                    <el-table-column label="SSL" width="110"><template #default="{ row }">{{ row.status || '—' }}</template></el-table-column>
                  </el-table>
                </el-tab-pane>
                <el-tab-pane :label="'登录会话（' + (detail.sessions || []).length + '）'" name="sessions">
                  <el-table :data="detail.sessions || []" size="small" border>
                    <el-table-column prop="ip" label="IP" width="140" />
                    <el-table-column prop="device" label="设备" min-width="180" />
                    <el-table-column label="最近活跃" width="160"><template #default="{ row }">{{ new Date(row.lastSeenAt).toLocaleString() }}</template></el-table-column>
                  </el-table>
                  <div class="mt-3"><el-button size="small" type="danger" plain @click="forceLogout({ id: detail.id, username: detail.username })">强制下线全部</el-button></div>
                </el-tab-pane>
                <el-tab-pane label="短信日志" name="sms">
                  <el-table :data="detail.smsLogs || []" size="small" border>
                    <el-table-column prop="event" label="事件" width="110" />
                    <el-table-column prop="mobile" label="手机号" width="150" />
                    <el-table-column prop="status" label="状态" width="90" />
                    <el-table-column label="时间" width="160"><template #default="{ row }">{{ new Date(row.createdAt).toLocaleString() }}</template></el-table-column>
                  </el-table>
                </el-tab-pane>
                <el-tab-pane label="订单与工单" name="orders">
                  <h6 class="mb-2">工单：{{ detail.ticketCount || 0 }} 个（未关闭 {{ detail.openTickets || 0 }}）</h6>
                  <el-table :data="detail.orders || []" size="small" border>
                    <el-table-column prop="orderNo" label="订单号" min-width="180" />
                    <el-table-column prop="kind" label="类型" width="110" />
                    <el-table-column label="金额" width="100"><template #default="{ row }">¥{{ ((row.amountCents || 0) / 100).toFixed(2) }}</template></el-table-column>
                    <el-table-column prop="status" label="状态" width="90" />
                    <el-table-column label="时间" width="160"><template #default="{ row }">{{ new Date(row.createdAt).toLocaleString() }}</template></el-table-column>
                  </el-table>
                </el-tab-pane>
              </el-tabs>
            </template>
          </div>
        </el-drawer>

        <!-- 调整余额 -->
        <el-dialog v-model="balDialog" :title="'调整余额 · ' + balForm.username" width="420px">
          <el-form label-width="90px">
            <el-form-item label="金额(元)">
              <el-input-number v-model="balForm.amount" :precision="2" :step="10" />
              <div class="text-muted small">正数充值，负数扣减</div>
            </el-form-item>
            <el-form-item label="原因"><el-input v-model="balForm.reason" placeholder="必填，记入财务日志" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="balDialog = false">取消</el-button>
            <el-button type="primary" @click="saveBalance">确认调整</el-button>
          </template>
        </el-dialog>

        <!-- 快捷重置密码 -->
        <el-dialog v-model="pwdDialog" :title="'重置密码 · ' + pwdForm.username" width="380px">
          <el-form label-width="90px">
            <el-form-item label="新密码"><el-input v-model="pwdForm.password" type="password" show-password placeholder="至少 6 位" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="pwdDialog = false">取消</el-button>
            <el-button type="primary" @click="saveResetPwd">确认重置</el-button>
          </template>
        </el-dialog>
        </el-card>

      </template>
    </CdnGate>
  </div>
</template>
