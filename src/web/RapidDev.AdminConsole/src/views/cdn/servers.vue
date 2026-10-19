<script setup lang="ts">
/** CDN 网站列表 —— 对接 TnorvCdn 主控 /api/admin/servers。
 *  编辑弹窗只保留基础信息/源站/L4；HTTPS、缓存、安全防护走独立的分层配置页，
 *  备案在编辑弹窗内只读（修改走「备案审核」页）。 */
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const router = useRouter()
const gate = ref()
const servers = ref<any[]>([])
const clusters = ref<any[]>([])
const dialog = ref(false)
const saving = ref(false)
const form = ref<any>({})
const batchDialog = ref(false)
const selected = ref<number[]>([])
const batchForm = ref({ host: '', port: 80 })

// 一键配置模板
const tplDialog = ref(false)
const templates = ref<any[]>([])
const tplTarget = ref<any>(null)
const tplPicked = ref<number | null>(null)

async function batchReplace() {
  if (!selected.value.length) return ElMessage.warning('请先勾选站点')
  if (!batchForm.value.host) return ElMessage.warning('请填写新源站地址')
  try {
    const r = await cdnApi.post('/admin/servers/batch-origin', { serverIds: selected.value, newHost: batchForm.value.host, newPort: batchForm.value.port })
    ElMessage.success(`已替换 ${r.changed} 个站点`)
    batchDialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || '替换失败') }
}

// 列表筛选（集群/用户/关键词）+ 服务端分页/排序（B2）
const filterCluster = ref<number | null>(null)
const filterUser = ref<number | null>(null)
const filterQ = ref('')
const filterUserId = ref<number | null>(null)
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const sortKey = ref('')
const sortOrder = ref('')
const chips = ref({ all: 0, running: 0, disabled: 0, sslExpiring: 0 })
const chipActive = ref<'' | 'running' | 'disabled' | 'sslExpiring'>('')

const loadData = async () => {
  const params: any = {
    clusterId: filterCluster.value ?? undefined,
    userId: filterUser.value ?? undefined,
    q: filterQ.value || undefined,
    page: pager.value.page,
    pageSize: pager.value.pageSize,
  }
  if (chipActive.value === 'sslExpiring') params.sslExpiring = '1'
  if (chipActive.value === 'disabled') params.userId = -1 // 占位：禁用筛选在前端做
  if (sortKey.value) { params.sort = sortKey.value; params.order = sortOrder.value || 'desc' }
  const [res, c, all, users] = await Promise.all([
    cdnApi.get('/admin/servers', params),
    cdnApi.get('/admin/clusters'),
    cdnApi.get('/admin/servers'),           // 全量（统计 chips + 前端状态筛选兜底）
    filterUser.value === null ? cdnApi.get('/admin/users') : Promise.resolve(null),
  ])
  let items: any[] = res.items || []
  if (chipActive.value === 'disabled') items = items.filter((x: any) => !x.enabled)
  else if (chipActive.value === 'running') items = items.filter((x: any) => x.enabled)
  servers.value = items
  pager.value.total = res.total ?? items.length
  clusters.value = c
  allServersCache.value = all
  chips.value = {
    all: all.length,
    running: all.filter((x: any) => x.enabled).length,
    disabled: all.filter((x: any) => !x.enabled).length,
    sslExpiring: all.filter((x: any) => x.sslMode !== 'off' && x.sslExpiresAt && new Date(x.sslExpiresAt) < new Date(Date.now() + 14 * 864e5)).length,
  }
  if (users) allUsers.value = users
  return items
}
const allServersCache = ref<any[]>([])
const allUsers = ref<any[]>([])

const onSort = ({ prop, order }: any) => {
  sortKey.value = order ? prop : ''
  sortOrder.value = order === 'ascending' ? 'asc' : 'desc'
  pager.value.page = 1
  gate.value?.refresh?.()
}
const chipClick = (k: any) => {
  chipActive.value = chipActive.value === k ? '' : k
  pager.value.page = 1
  gate.value?.refresh?.()
}

// 批量启用/停用/删除通用设计
const batchBusy = ref(false)
async function batchAction(action: string) {
  if (!selected.value.length) return ElMessage.warning('请先勾选站点')
  const names: any = { enable: '启用', disable: '停用', delete: '删除' }
  await ElMessageBox.confirm(`确认${names[action]}选中的 ${selected.value.length} 个站点？`, '批量操作', { type: 'warning' })
  batchBusy.value = true
  try {
    const r = await cdnApi.post('/admin/servers/batch', { ids: selected.value, action })
    ElMessage.success(`已${names[action]} ${r.affected} 个站点`)
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || '操作失败') }
  finally { batchBusy.value = false }
}

// 检查域名解析（行操作）
const dnsChecking = ref<number | null>(null)
async function checkDns(row: any) {
  dnsChecking.value = row.id
  try {
    const r = await cdnApi.get(`/admin/servers/${row.id}/dns-check`)
    if (r.ok) {
      ElMessage[r.pointsToCluster ? 'success' : 'warning'](
        r.pointsToCluster ? `${r.domain} 已解析到集群入口` : `${r.domain} 尚未指向集群（${r.clusterDns || '未配置'}）`)
    } else ElMessage.error(r.error || '检测失败')
  } catch (e: any) { ElMessage.error(e?.response?.data || '检测失败') }
  finally { dnsChecking.value = null }
}

const clusterName = (id: number) => clusters.value.find((c) => c.id === id)?.name || id

function openEdit(row: any) {
  if (row) {
    const base = JSON.parse(JSON.stringify(row))
    base.origin = base.origin || { primaries: [], backups: [], scheduleAlgorithm: 'round-robin', hostOverride: '', pathRules: [] }
    base.origin.primaries = base.origin.primaries?.length ? base.origin.primaries : [{ scheme: 'http', host: '', port: 80, weight: 1, enabled: true }]
    base.l4Ports = base.l4Ports || []
    form.value = base
  } else {
    form.value = {
      clusterId: clusters.value[0]?.id, userId: 1, domains: [], region: 'global', icpStatus: 'none', icpNumber: '', icpNote: '',
      httpPorts: [80], httpsPorts: [443], enabled: true, sslMode: 'auto', forceHttps: false, serverType: 'http', l4Ports: [],
      origin: { primaries: [{ scheme: 'http', host: '', port: 80, weight: 1, enabled: true }], backups: [], scheduleAlgorithm: 'round-robin', hostOverride: '', pathRules: [] },
      cache: { enabled: false, rules: [] },
      security: {
        deniedCidrs: [], ipWhitelist: [], ipBlacklist: [], uaRules: [], deniedCountries: [],
        cc: { enabled: false, threshold: 300, windowSeconds: 60, banSeconds: 60, excludePaths: [] },
        jsShield: { enabled: false, mode: 'js', cookieSeconds: 3600, maxFails: 5, banSeconds: 600, excludePaths: [] },
        urlAuth: { enabled: false, type: 'a', key: '', expirySeconds: 1800 },
      },
    }
  }
  dialog.value = true
}

async function save() {
  const f = form.value
  if (!f.domains?.length || !f.domains[0]) return ElMessage.warning('请填写域名（换行/逗号分隔多个）')
  if (!f.origin?.primaries?.[0]?.host) return ElMessage.warning('请填写主源站地址')
  saving.value = true
  try {
    if (f.id) await cdnApi.put(`/admin/servers/${f.id}`, f)
    else await cdnApi.post('/admin/servers', f)
    ElMessage.success('已保存')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || e?.message || '保存失败')
  } finally {
    saving.value = false
  }
}

const l4PortsText = computed({
  get: () => (form.value.l4Ports || []).join(','),
  set: (v: string) => { form.value.l4Ports = v.split(',').map((x: string) => parseInt(x.trim())).filter((n: number) => n > 0) },
})

const icpLabel = (st: string) => ({ none: '未备案', pending: '待审核', approved: '已备案', rejected: '已驳回' } as any)[st] || st
const icpTagType = (st: string) => ({ none: 'info', pending: 'warning', approved: 'success', rejected: 'danger' } as any)[st] || 'info'
const sslLabel = (m: string) => ({ auto: '自动签发', manual: '手动证书', off: '关闭' } as any)[m] || m || '自动签发'
const sslStateLabel = (s: string) => ({ pending_dns: '未解析', pending_issuing: '签发中', issued: '已签发', failed: '签发失败' } as any)[s] || s
const sslStateTag = (s: string) => ({ pending_dns: 'warning', pending_issuing: 'primary', issued: 'success', failed: 'danger' } as any)[s] || 'info'
const sslTagType = (row: any) => row.sslMode === 'off' ? 'info' : (row.sslMode === 'auto' && row.sslState === 'failed' ? 'danger' : 'success')

// 行内：刷新缓存（全部）
const purging = ref(0)
async function purgeRow(row: any) {
  purging.value = row.id
  try {
    await cdnApi.post('/admin/servers/cache/purge', { matchType: 'host', host: (row.domains || [])[0] })
    ElMessage.success('缓存刷新指令已下发全部节点')
  } finally { purging.value = 0 }
}

// 行内：复制配置（打开复制弹窗并预选来源）
const copyVisible = ref(false)
const copyFromId = ref<number | null>(null)
const copyTargetId = ref<number | null>(null)
async function openCopyRow(row: any) {
  copyTargetId.value = row.id
  copyFromId.value = null
  if (!allServersCache.value.length) allServersCache.value = await cdnApi.get('/admin/servers')
  copyVisible.value = true
}
const copyParts = ref<string[]>(['cache', 'security', 'advanced'])
const copyOptions = [
  { key: 'cache', label: '缓存' },
  { key: 'security', label: '安全防护' },
  { key: 'advanced', label: '高级功能' },
  { key: 'https', label: 'HTTPS' },
  { key: 'origin', label: '源站' },
]
async function doCopyRow() {
  if (!copyFromId.value) return ElMessage.warning('请选择来源站点')
  await cdnApi.post(`/admin/servers/${copyTargetId.value}/copy-from`, { fromId: copyFromId.value, parts: copyParts.value })
  ElMessage.success('配置已复制并下发')
  copyVisible.value = false
  gate.value?.refresh?.()
}

// 行下拉命令分发
function rowCommand(cmd: string, row: any) {
  if (cmd === 'edit') openEdit(row)
  else if (cmd === 'tpl') openTpl(row)
  else if (cmd === 'copy') openCopyRow(row)
  else if (cmd === 'dns') checkDns(row)
  else if (cmd === 'purge') purgeRow(row)
  else if (cmd === 'logs') router.push(`/cdn/servers/logs?serverId=${row.id}`)
  else if (cmd === 'stats') router.push(`/cdn/servers/traffic-stats?serverId=${row.id}`)
  else if (cmd === 'realtime') toggleRealtime(row)
  else if (cmd === 'publish') publishNow(row)
  else if (cmd === 'del') del(row)
}

// 下发策略：站点级实时下发开关（开启后该站点配置变更立即发布，默认批量 5 分钟生效）
async function toggleRealtime(row: any) {
  const next = !row.realtimePublish
  await cdnApi.put(`/admin/servers/${row.id}/realtime`, { enabled: next })
  row.realtimePublish = next
  ElMessage.success(next ? '已开启实时下发：该站点配置变更将立即发布' : '已关闭实时下发：配置变更将按周期批量发布')
}

// 立即下发：不等延迟批，马上 bump 集群版本（节点 10s 轮询内生效）
async function publishNow(row: any) {
  await cdnApi.post(`/admin/servers/${row.id}/publish`, {})
  ElMessage.success('已触发立即下发，节点将在 10 秒内拉取生效')
}

async function del(row: any) {
  await ElMessageBox.confirm(`确认删除站点 ${(row.domains || []).join(', ')}？删除后不可恢复。`, '删除站点', { type: 'warning' })
  await cdnApi.delete(`/admin/servers/${row.id}`)
  ElMessage.success('已删除')
  gate.value?.refresh?.()
}

async function openTpl(row: any) {
  tplTarget.value = row
  tplPicked.value = null
  templates.value = await cdnApi.get('/admin/site-templates')
  tplDialog.value = true
}

async function applyTpl() {
  if (!tplPicked.value) return ElMessage.warning('请选择模板')
  const t = templates.value.find((x: any) => x.id === tplPicked.value)
  await ElMessageBox.confirm(
    `将把模板「${t?.name}」的配置（${t?.description || ''}）覆盖应用到站点 ${(tplTarget.value?.domains || []).join(', ')}，继续？`,
    '应用模板', { type: 'warning' })
  try {
    const r = await cdnApi.post(`/admin/site-templates/${tplPicked.value}/apply`, { serverId: tplTarget.value.id })
    ElMessage.success(`已应用：${(r.applied || []).join('、') || '无变更'}`)
    tplDialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '应用失败') }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">网站列表</h2><div class="text-muted">TnorvCDN · CDN加速</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <div class="mb-3 d-flex gap-2 flex-wrap">
          <el-check-tag :checked="chipActive === ''" @change="chipClick('')" size="small">全部 {{ chips.all }}</el-check-tag>
          <el-check-tag :checked="chipActive === 'running'" @change="chipClick('running')" size="small">运行中 {{ chips.running }}</el-check-tag>
          <el-check-tag :checked="chipActive === 'disabled'" @change="chipClick('disabled')" size="small">停用 {{ chips.disabled }}</el-check-tag>
          <el-check-tag :checked="chipActive === 'sslExpiring'" @change="chipClick('sslExpiring')" size="small">证书临期 {{ chips.sslExpiring }}</el-check-tag>
        </div>
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center flex-wrap">
            <el-button type="primary" size="small" @click="openEdit(null)">+ 新增网站</el-button>
            <el-select v-model="filterCluster" placeholder="集群" clearable size="small" style="width: 140px" @change="refresh">
              <el-option v-for="c in clusters" :key="c.id" :value="c.id" :label="c.name" />
            </el-select>
            <el-select v-model="filterUser" placeholder="用户" clearable filterable size="small" style="width: 150px" @change="refresh">
              <el-option v-for="u in allUsers" :key="u.id" :value="u.id" :label="u.username" />
            </el-select>
            <el-input v-model="filterQ" placeholder="域名关键词" clearable size="small" style="width: 180px" @change="refresh" />
            <el-button size="small" @click="refresh()">刷新</el-button>
            <span class="flex-fill"></span>
            <el-button size="small" :disabled="!selected.length" :loading="batchBusy" @click="batchAction('enable')">批量启用</el-button>
            <el-button size="small" :disabled="!selected.length" :loading="batchBusy" @click="batchAction('disable')">批量停用</el-button>
            <el-button size="small" type="danger" plain :disabled="!selected.length" :loading="batchBusy" @click="batchAction('delete')">批量删除</el-button>
            <el-button size="small" @click="batchDialog = true">批量替换源站</el-button>
          </div>
          <el-table :data="servers" stripe @selection-change="(sel: any[]) => selected = sel.map(x => x.id)" @sort-change="onSort">
            <el-table-column type="selection" width="45" />
            <el-table-column prop="id" label="ID" width="60" sortable="custom" :order="sortKey === 'id' ? (sortOrder === 'asc' ? 'ascending' : 'descending') : undefined" />
            <el-table-column prop="domains" label="域名" min-width="200" sortable="custom" :order="sortKey === 'domains' ? (sortOrder === 'asc' ? 'ascending' : 'descending') : undefined">
              <template #default="{ row }">{{ (row.domains || []).join(', ') }}</template>
            </el-table-column>
            <el-table-column label="加速区域" width="100">
              <template #default="{ row }">
                <el-tag size="small" :type="row.region === 'cn' ? 'warning' : row.region === 'overseas' ? 'primary' : 'info'">
                  {{ ({ global: '全球', cn: '中国大陆', overseas: '非中国大陆' } as any)[row.region] || row.region }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="备案" width="90">
              <template #default="{ row }">
                <el-tag v-if="row.region === 'cn'" size="small" :type="({ none: 'info', pending: 'warning', approved: 'success', rejected: 'danger' } as any)[row.icpStatus || 'none']">
                  {{ ({ none: '未备案', pending: '待审核', approved: '已备案', rejected: '已驳回' } as any)[row.icpStatus || 'none'] }}
                </el-tag>
                <span v-else class="text-muted small">—</span>
              </template>
            </el-table-column>
            <el-table-column label="SSL" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="sslTagType(row)">{{ sslLabel(row.sslMode) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="证书状态" width="110">
              <template #default="{ row }">
                <el-tag v-if="row.sslMode === 'auto' && row.sslState" size="small" :type="sslStateTag(row.sslState)">
                  {{ sslStateLabel(row.sslState) }}
                </el-tag>
                <span v-else-if="row.sslMode === 'auto'" class="text-muted small">待签发</span>
                <span v-else class="text-muted small">—</span>
              </template>
            </el-table-column>
            <el-table-column label="集群" width="110">
              <template #default="{ row }">{{ clusterName(row.clusterId) }}</template>
            </el-table-column>
            <el-table-column label="源站" min-width="180">
              <template #default="{ row }">{{ (row.origin?.primaries || []).map((o: any) => `${o.host}:${o.port}`).join(', ') }}</template>
            </el-table-column>
            <el-table-column label="状态" width="80">
              <template #default="{ row }">
                <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="下发" width="80">
              <template #default="{ row }">
                <el-tooltip content="实时=配置变更立即发布（节点约10s生效）；批量=按周期合并发布（默认5分钟）" placement="top">
                  <el-tag :type="row.realtimePublish ? 'warning' : 'info'" size="small">{{ row.realtimePublish ? '实时' : '批量' }}</el-tag>
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column label="证书到期" width="120" prop="sslExpiresAt" sortable="custom" :order="sortKey === 'ssl' ? (sortOrder === 'asc' ? 'ascending' : 'descending') : undefined">
              <template #default="{ row }">
                <span v-if="row.sslExpiresAt && row.sslMode !== 'off'" class="small" :class="{ 'text-danger': new Date(row.sslExpiresAt) < new Date(Date.now() + 7 * 864e5), 'text-warning': new Date(row.sslExpiresAt) >= new Date(Date.now() + 7 * 864e5) && new Date(row.sslExpiresAt) < new Date(Date.now() + 14 * 864e5) }">
                  {{ new Date(row.sslExpiresAt).toLocaleDateString() }}
                </span>
                <span v-else class="text-muted small">—</span>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="150" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 6px">
                  <el-button size="small" type="primary" plain @click="router.push(`/cdn/servers/config/${row.id}`)">配置</el-button>
                  <el-dropdown @command="(cmd: string) => rowCommand(cmd, row)">
                    <el-button size="small">管理<i class="ti ti-chevron-down ms-1"></i></el-button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item command="edit">编辑基本信息</el-dropdown-item>
                        <el-dropdown-item command="tpl">应用配置模板</el-dropdown-item>
                        <el-dropdown-item command="copy">从其他站点复制…</el-dropdown-item>
                        <el-dropdown-item command="realtime" divided>{{ row.realtimePublish ? '关闭实时下发' : '开启实时下发' }}</el-dropdown-item>
                        <el-dropdown-item command="publish">立即下发配置</el-dropdown-item>
                        <el-dropdown-item command="dns" divided>检查解析</el-dropdown-item>
                        <el-dropdown-item command="purge" :disabled="purging === row.id">刷新缓存</el-dropdown-item>
                        <el-dropdown-item command="logs" divided>访问日志</el-dropdown-item>
                        <el-dropdown-item command="stats">用量统计</el-dropdown-item>
                        <el-dropdown-item command="del" divided>
                          <span class="text-danger">删除站点</span>
                        </el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </div>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize"
            :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
        </el-card>

        <el-dialog v-model="copyVisible" title="从其他站点复制配置" width="480px" align-center>
          <el-form label-width="90px">
            <el-form-item label="来源站点">
              <el-select v-model="copyFromId" filterable placeholder="选择来源" style="width: 100%">
                <el-option v-for="o in allServersCache.filter((x: any) => x.id !== copyTargetId)" :key="o.id" :value="o.id" :label="(o.domains && o.domains[0]) || ('站点#' + o.id)" />
              </el-select>
            </el-form-item>
            <el-form-item label="复制内容">
              <el-checkbox-group v-model="copyParts">
                <el-checkbox v-for="o in copyOptions" :key="o.key" :value="o.key">{{ o.label }}</el-checkbox>
              </el-checkbox-group>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="copyVisible = false">取消</el-button>
            <el-button type="primary" @click="doCopyRow">开始复制</el-button>
          </template>
        </el-dialog>

        <el-dialog v-model="batchDialog" title="批量替换源站" width="480px">
          <el-alert type="warning" :closable="false" class="mb-3" :title="`将把 ${selected.length} 个已选站点的主源站地址全部替换`" />
          <el-form label-width="110px">
            <el-form-item label="新源站"><el-input v-model="batchForm.host" placeholder="源站地址" style="width:200px" class="mr-1" />
              <el-input-number v-model="batchForm.port" :min="1" :max="65535" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="batchDialog = false">取消</el-button>
            <el-button type="primary" @click="batchReplace">执行替换</el-button>
          </template>
        </el-dialog>

        <el-dialog v-model="tplDialog" title="一键应用配置模板" width="520px">
          <el-alert type="info" :closable="false" class="mb-3"
            :title="`目标站点：${(tplTarget?.domains || []).join(', ')}`"
            description="模板将覆盖站点的 HTTPS/缓存/安全配置，源站与域名不受影响。" />
          <el-select v-model="tplPicked" placeholder="选择配置模板" style="width:100%">
            <el-option v-for="t in templates" :key="t.id" :value="t.id" :label="t.name + (t.builtIn ? '（内置）' : '')">
              <div><span>{{ t.name }}</span><span class="text-muted small ms-2">{{ t.description }}</span></div>
            </el-option>
          </el-select>
          <template #footer>
            <el-button @click="tplDialog = false">取消</el-button>
            <el-button type="primary" @click="applyTpl">应用</el-button>
          </template>
        </el-dialog>

        <!-- 基础编辑弹窗：只含域名/区域/集群/源站/L4；备案只读 -->
        <el-dialog v-model="dialog" :title="form.id ? '编辑网站（基础信息）' : '新增网站'" width="640px">
          <el-form label-width="110px">
            <el-form-item label="域名">
              <el-input :model-value="(form.domains || []).join('\n')" type="textarea" :rows="2"
                placeholder="每行一个域名，如 test.example.com"
                @update:model-value="(v: string) => form.domains = v.split(/\n|,/).map((x: string) => x.trim()).filter(Boolean)" />
            </el-form-item>
            <el-form-item label="加速区域">
              <el-radio-group v-model="form.region" :disabled="!!form.id">
                <el-radio-button value="global">全球</el-radio-button>
                <el-radio-button value="cn">中国大陆</el-radio-button>
                <el-radio-button value="overseas">非中国大陆</el-radio-button>
              </el-radio-group>
              <div v-if="form.region === 'cn'" class="text-muted small mt-1">中国大陆节点要求域名已备案</div>
            </el-form-item>
            <el-form-item label="集群">
              <el-select v-model="form.clusterId" style="width:240px">
                <el-option v-for="c in clusters" :key="c.id" :value="c.id" :label="c.name" />
              </el-select>
            </el-form-item>
            <el-form-item label="主源站">
              <el-select v-model="form.origin.primaries[0].scheme" style="width:90px"><el-option value="http" /><el-option value="https" /></el-select>
              <el-input v-model="form.origin.primaries[0].host" placeholder="源站地址" style="width:220px" class="mx-1" />
              <el-input-number v-model="form.origin.primaries[0].port" :min="1" :max="65535" />
            </el-form-item>
            <el-form-item label="回源 Host"><el-input v-model="form.origin.hostOverride" placeholder="留空=跟随访问域名" /></el-form-item>
            <el-form-item label="HTTP端口"><el-input :model-value="(form.httpPorts || []).join(',')" @update:model-value="(v: string) => form.httpPorts = v.split(',').map((x: string) => parseInt(x)).filter((n: number) => n > 0)" /></el-form-item>
            <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
            <el-divider>TCP/UDP 转发（L4）</el-divider>
            <el-form-item label="协议类型">
              <el-radio-group v-model="form.serverType">
                <el-radio-button value="http">HTTP(S) 网站加速</el-radio-button>
                <el-radio-button value="tcp">TCP 端口转发</el-radio-button>
                <el-radio-button value="udp">UDP 端口转发</el-radio-button>
              </el-radio-group>
            </el-form-item>
            <el-form-item v-if="form.serverType && form.serverType !== 'http'" label="转发端口">
              <el-input v-model="l4PortsText" placeholder="如 3306, 6379" style="width:280px" />
              <span class="ms-2 text-muted small">逗号分隔；流量按 L4 直接转发到源站同端口</span>
            </el-form-item>
            <el-divider v-if="form.id && form.region === 'cn'">域名备案（只读）</el-divider>
            <el-form-item v-if="form.id && form.region === 'cn'" label="备案状态">
              <el-tag :type="icpTagType(form.icpStatus)" size="small">{{ icpLabel(form.icpStatus) }}</el-tag>
              <span v-if="form.icpNumber" class="ms-2 text-muted small">{{ form.icpNumber }}</span>
              <span class="ms-2 text-muted small">如需修改请到「域名解析 → 备案审核」页操作</span>
            </el-form-item>
            <el-alert v-if="form.id" type="info" :closable="false" class="mt-2"
              title="HTTPS/SSL、缓存、安全防护（CC/5秒盾/防盗链/IP名单）等配置请点击列表中的「配置」进入分层配置页管理" />
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" :loading="saving" @click="save">保存</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
