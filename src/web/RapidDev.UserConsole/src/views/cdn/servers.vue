<script setup lang="ts">
/** 我的网站（U2）—— 卡片视图（默认，今日流量/请求/命中率 + 启停开关 + 快速接入）/表格视图切换 + 分组筛选 chips */
import { ref, computed, nextTick } from 'vue'
import { useRouter } from 'vue-router'
const router = useRouter()
import { ElMessage, ElMessageBox } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const saving = ref(false)
const form = ref<any>({})
const quota = ref<any>(null)

const regionLabel = (r: string) => ({ global: '全球', cn: '中国大陆', overseas: '非中国大陆' } as any)[r] || r
const icpLabel = (st: string) => ({ none: '未备案', pending: '审核中', approved: '已备案', rejected: '已驳回' } as any)[st] || st
const icpTag = (st: string) => ({ none: 'info', pending: 'warning', approved: 'success', rejected: 'danger' } as any)[st] || 'info'
const sslLabel = (m: string) => ({ auto: '自动签发', manual: '手动证书', off: '关闭' } as any)[m] || m || '自动签发'
const statusLabel = (s: string) => ({ normal: '正常', not_resolved: '未解析', configuring: '配置中', updating: '更新中', issuing: '证书签发中', cert_failed: '证书签发失败', disabled: '已停用' } as any)[s] || s || '正常'
const statusTag = (s: string) => ({ normal: 'success', not_resolved: 'warning', configuring: 'info', updating: 'primary', issuing: 'primary', cert_failed: 'danger', disabled: 'info' } as any)[s] || 'info'

const showNodes = ref(true)
const filterQ = ref('')
const groupFilter = ref('')
const viewMode = ref<'card' | 'table'>('card')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

// U2：卡片模式请求 stats=true 获取今日流量/请求/命中率
const loadData = async () => {
  const wantStats = viewMode.value === 'card'
  const r = await cdnApi.get('/user/servers', {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    q: filterQ.value || undefined,
    stats: wantStats || undefined,
  }) as any
  const d = r?.data ?? r
  if (d && !Array.isArray(d) && 'total' in d) {
    pager.value.total = d.total
    rows.value = d.items || []
  } else {
    rows.value = d || (Array.isArray(r) ? r : [])
    pager.value.total = rows.value.length
  }
  showNodes.value = rows.value.some((x: any) => 'nodesOnline' in x)
  await nextTick()
  return rows.value
}

// 分组标签（从当前站点集合提取）
const groups = computed(() => Array.from(new Set(rows.value.map((r: any) => r.group).filter(Boolean))) as string[])
const filteredRows = computed(() =>
  groupFilter.value ? rows.value.filter((r: any) => r.group === groupFilter.value) : rows.value
)

const fmtBytes = (b: any) => {
  const n = Number(b) || 0
  if (n >= 1 << 30) return (n / (1 << 30)).toFixed(2) + ' GB'
  if (n >= 1 << 20) return (n / (1 << 20)).toFixed(1) + ' MB'
  if (n >= 1 << 10) return (n / (1 << 10)).toFixed(1) + ' KB'
  return n + ' B'
}
const statusDot = (s: string) => ({ normal: '#67C23A', not_resolved: '#E6A23C', configuring: '#909399', updating: '#409EFF', issuing: '#409EFF', cert_failed: '#F56C6C', disabled: '#909399' } as any)[s] || '#909399'

const onSearch = () => { pager.value.page = 1; gate.value?.refresh?.() }
const switchView = (m: any) => { viewMode.value = m; gate.value?.refresh?.() }

// ---- 启停开关（U2 卡片行内）----
const toggling = ref(0)
async function toggleEnabled(row: any, v: boolean) {
  toggling.value = row.id
  try {
    await cdnApi.put(`/user/servers/${row.id}/enabled`, { enabled: v })
    row.enabled = v
    row.status = v ? 'normal' : 'disabled'
    ElMessage.success(v ? '站点已启用' : '站点已停用（配置保留，可随时重新启用）')
  } catch (e: any) {
    row.enabled = !v
    ElMessage.error(e?.response?.data?.message || '操作失败')
  } finally { toggling.value = 0 }
}

// ---- 快速接入三步引导 ----
const wizard = ref(false)
const wizardStep = ref(0)
const wizardRow = ref<any>(null)
const wizardOrigin = ref('')
const wizardSaving = ref(false)
const clusterDns = '接入节点 CNAME（见下方节点 IP / 集群接入地址）'

function openWizard(row: any) {
  wizardRow.value = row
  wizardStep.value = 0
  wizardOrigin.value = row?.originFull?.[0]?.host || ''
  wizard.value = true
}
async function wizardSave() {
  if (!wizardOrigin.value.trim()) return ElMessage.warning('请填写源站地址')
  wizardSaving.value = true
  try {
    await cdnApi.put(`/user/servers/${wizardRow.value.id}`, { originHost: wizardOrigin.value.trim() })
    ElMessage.success('源站已保存')
    wizardStep.value = 2
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || '保存失败')
  } finally { wizardSaving.value = false }
}

const openCreate = () => {
  form.value = {
    domainsText: '', region: 'global', serverType: 'http', sslMode: 'auto', forceHttps: false, cacheEnabled: true,
    originScheme: 'http', originHost: '', originPort: 80, icpNumber: '', accessMode: 'cname',
  }
  dialog.value = true
}

const openEdit = (row: any) => {
  form.value = {
    id: row.id,
    domainsText: (row.domains || []).join('\n'),
    region: row.region,
    serverType: row.serverType || 'http',
    sslMode: row.sslMode || 'auto',
    forceHttps: !!row.forceHttps,
    cacheEnabled: !!(row.cache?.enabled ?? row.cacheEnabled),
    originScheme: row.origin?.primaries?.[0]?.scheme || 'http',
    originHost: row.origin?.primaries?.[0]?.host || '',
    originPort: row.origin?.primaries?.[0]?.port || 80,
    icpNumber: row.icpNumber || '',
  }
  dialog.value = true
}

async function save() {
  const f = form.value
  const domains = (f.domainsText || '').split(/\n|,/).map((x: string) => x.trim()).filter(Boolean)
  if (!f.id && !domains.length) return ElMessage.warning('请填写域名')
  if (!f.id && !f.originHost) return ElMessage.warning('请填写源站地址')
  saving.value = true
  try {
    if (f.id) {
      await cdnApi.put(`/user/servers/${f.id}`, {
        originHost: f.originHost, originPort: Number(f.originPort) || 80, originScheme: f.originScheme,
        sslMode: f.sslMode, forceHttps: f.forceHttps, cacheEnabled: f.cacheEnabled,
      })
      ElMessage.success('已保存')
    } else {
      await cdnApi.post('/user/servers', {
        domains, region: f.region, serverType: f.serverType, sslMode: f.sslMode, forceHttps: f.forceHttps,
        cacheEnabled: f.cacheEnabled, originScheme: f.originScheme, originHost: f.originHost,
        originPort: Number(f.originPort) || 80, icpNumber: f.icpNumber || null,
        accessMode: f.accessMode === 'ns' ? 'ns' : 'cname',
      })
      ElMessage.success(f.region === 'cn' ? '已创建，中国大陆区域需备案审核通过后自动启用' : '创建成功，域名解析到节点 IP 即可生效')
    }
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || e?.message || '保存失败')
  } finally { saving.value = false }
}

async function del(row: any) {
  await ElMessageBox.confirm(`确认删除站点 ${(row.domains || []).join(', ')}？删除后不可恢复。`, '删除站点', { type: 'warning' })
  try {
    await cdnApi.delete(`/user/servers/${row.id}`)
    ElMessage.success('已删除')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '删除失败') }
}

// 检查解析（行内快捷入口）
const dnsChecking = ref(0)
async function checkDns(row: any) {
  dnsChecking.value = row.id
  try {
    const r = await cdnApi.get(`/user/servers/${row.id}/dns-check`) as any
    const d = r?.data ?? r
    if (d?.ok === false) { ElMessage.error(d?.error || '解析失败'); return }
    const pointed = d?.pointsToCluster
    ElMessage({
      type: pointed ? 'success' : 'warning',
      message: pointed
        ? `${d.domain} 已正确解析到节点`
        : `${d?.domain || '域名'} 尚未解析到集群 CNAME（${d?.clusterDns || '—'}），请先完成解析`,
      duration: 5000,
    })
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '检测失败') }
  finally { dnsChecking.value = 0 }
}

// 行下拉命令分发
function rowCommand(cmd: string, row: any) {
  if (cmd === 'edit') openEdit(row)
  else if (cmd === 'dns') checkDns(row)
  else if (cmd === 'logs') router.push(`/waf/logs?serverId=${row.id}`)
  else if (cmd === 'stats') router.push(`/servers/traffic-stats?serverId=${row.id}`)
  else if (cmd === 'del') del(row)
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">我的网站</h2><div class="text-muted">CDN加速 · 自助接入与管理</div></div>
        <div class="col-auto ms-auto d-flex gap-2">
          <el-radio-group v-model="viewMode" size="small" @change="switchView">
            <el-radio-button value="card"><i class="ti ti-layout-grid"></i> 卡片</el-radio-button>
            <el-radio-button value="table"><i class="ti ti-list"></i> 表格</el-radio-button>
          </el-radio-group>
          <el-input v-model="filterQ" placeholder="域名关键词" size="small" clearable style="width: 200px"
            @keyup.enter="onSearch" @clear="onSearch">
            <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
          </el-input>
          <el-button type="primary" size="small" @click="openCreate">+ 新增网站</el-button>
        </div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ data, refresh }">
        <!-- 分组筛选 chips -->
        <div v-if="groups.length" class="mb-3 d-flex gap-2 flex-wrap align-items-center">
          <el-check-tag :checked="groupFilter === ''" size="small" @change="groupFilter = ''">全部</el-check-tag>
          <el-check-tag v-for="g in groups" :key="g" :checked="groupFilter === g" size="small" @change="groupFilter = g">{{ g }}</el-check-tag>
        </div>

        <!-- ============ 卡片视图 ============ -->
        <template v-if="viewMode === 'card'">
          <el-row v-if="filteredRows.length" :gutter="16">
            <el-col v-for="row in filteredRows" :key="row.id" :xs="24" :sm="12" :lg="8" class="mb-3">
              <el-card shadow="hover">
                <div class="d-flex align-items-start">
                  <div class="flex-fill" style="min-width: 0">
                    <div class="d-flex align-items-center">
                      <span class="me-2" :style="{ width: '8px', height: '8px', borderRadius: '50%', background: statusDot(row.status), display: 'inline-block' }"></span>
                      <span class="font-weight-medium text-truncate" style="font-size: 15px" :title="(row.domains || []).join(', ')">{{ (row.domains || []).join(', ') }}</span>
                    </div>
                    <div class="mt-1 d-flex gap-1 flex-wrap">
                      <el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
                      <el-tag size="small" type="info">{{ regionLabel(row.region) }}</el-tag>
                      <el-tag v-if="row.region === 'cn'" size="small" :type="icpTag(row.icpStatus)">{{ icpLabel(row.icpStatus) }}</el-tag>
                      <el-tag size="small" :type="row.sslMode === 'off' ? 'info' : 'success'">{{ sslLabel(row.sslMode) }}</el-tag>
                      <el-tag v-if="row.group" size="small">{{ row.group }}</el-tag>
                    </div>
                  </div>
                  <el-switch :model-value="row.enabled" :loading="toggling === row.id" inline-prompt
                    active-text="启" inactive-text="停" @change="(v: any) => toggleEnabled(row, !!v)" />
                </div>
                <!-- 今日三迷你数 -->
                <div class="d-flex mt-3 text-center">
                  <div class="flex-fill">
                    <div class="h4 mb-0">{{ fmtBytes(row.todayBytes) }}</div>
                    <div class="text-muted" style="font-size: 11px">今日流量</div>
                  </div>
                  <div class="flex-fill">
                    <div class="h4 mb-0">{{ (row.todayRequests ?? 0).toLocaleString() }}</div>
                    <div class="text-muted" style="font-size: 11px">今日请求</div>
                  </div>
                  <div class="flex-fill">
                    <div class="h4 mb-0">{{ row.todayHitRate ?? 0 }}%</div>
                    <div class="text-muted" style="font-size: 11px">命中率</div>
                  </div>
                </div>
                <el-divider class="my-2" />
                <div class="d-flex align-items-center" style="gap: 6px">
                  <el-button size="small" type="primary" plain @click="$router.push(`/servers/config/${row.id}`)">配置</el-button>
                  <el-button v-if="row.status === 'not_resolved' || !row.originFull?.[0]?.host" size="small" type="warning" plain @click="openWizard(row)">
                    快速接入
                  </el-button>
                  <el-dropdown class="ms-auto" @command="(cmd: string) => rowCommand(cmd, row)">
                    <el-button size="small">管理<i class="ti ti-chevron-down ms-1"></i></el-button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item command="edit">编辑信息</el-dropdown-item>
                        <el-dropdown-item command="dns">检查解析</el-dropdown-item>
                        <el-dropdown-item command="logs">访问日志</el-dropdown-item>
                        <el-dropdown-item command="stats">用量统计</el-dropdown-item>
                        <el-dropdown-item command="del" divided><span class="text-danger">删除站点</span></el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </div>
              </el-card>
            </el-col>
          </el-row>
          <el-card v-else><el-empty description="还没有接入网站，点击右上角「新增网站」开始" /></el-card>
        </template>

        <!-- ============ 表格视图 ============ -->
        <el-card v-else>
          <el-table :data="filteredRows" stripe>
            <el-table-column label="域名" min-width="200">
              <template #default="{ row }">{{ (row.domains || []).join(', ') }}</template>
            </el-table-column>
            <el-table-column label="加速区域" width="100">
              <template #default="{ row }">
                <el-tag size="small" :type="row.region === 'cn' ? 'warning' : 'info'">{{ regionLabel(row.region) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="备案" width="90">
              <template #default="{ row }">
                <el-tag v-if="row.region === 'cn'" size="small" :type="icpTag(row.icpStatus)">{{ icpLabel(row.icpStatus) }}</el-tag>
                <span v-else class="text-muted small">—</span>
              </template>
            </el-table-column>
            <el-table-column label="SSL" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="row.sslMode === 'off' ? 'info' : 'success'">{{ sslLabel(row.sslMode) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="源站" min-width="160">
              <template #default="{ row }">{{ (row.origin || []).join(', ') }}</template>
            </el-table-column>
            <el-table-column v-if="showNodes" label="节点在线" width="90">
              <template #default="{ row }">
                <el-tag :type="row.nodesOnline > 0 ? 'success' : 'danger'" size="small">{{ row.nodesOnline }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="120">
              <template #default="{ row }">
                <el-tooltip :content="row.status === 'not_resolved' ? '域名尚未解析到节点，请先完成 CNAME 解析' : row.status === 'issuing' ? '系统正在为域名签发 HTTPS 证书' : row.status === 'configuring' ? '站点配置准备中，备案审核通过后自动启用' : row.status === 'updating' ? '配置正在下发到节点' : ''" placement="top">
                  <el-tag :type="statusTag(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="170" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 6px">
                  <el-button size="small" type="primary" plain @click="$router.push(`/servers/config/${row.id}`)">配置</el-button>
                  <el-dropdown @command="(cmd: string) => rowCommand(cmd, row)">
                    <el-button size="small">管理<i class="ti ti-chevron-down ms-1"></i></el-button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item command="edit">编辑信息</el-dropdown-item>
                        <el-dropdown-item command="dns" divided>检查解析</el-dropdown-item>
                        <el-dropdown-item command="logs">访问日志</el-dropdown-item>
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
          <el-empty v-if="!(data || []).length" description="还没有接入网站，点击右上角「新增网站」开始" />
        </el-card>

        <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
          layout="total, sizes, prev, pager, next" :total="pager.total"
          :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
          @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
          @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
      </template>
    </CdnGate>

    <!-- 快速接入三步向导（按接入方式分 CNAME / NS 托管两种引导） -->
    <el-dialog v-model="wizard" title="快速接入" width="560px">
      <el-steps :active="wizardStep" align-center finish-status="success" class="mb-4">
        <el-step :title="wizardRow?.accessMode === 'ns' ? 'NS 托管' : 'CNAME 解析'" />
        <el-step title="填写源站" />
        <el-step title="完成" />
      </el-steps>
      <template v-if="wizardStep === 0 && wizardRow && wizardRow.accessMode !== 'ns'">
        <p>请将站点域名 <b>{{ (wizardRow.domains || []).join(', ') }}</b> 的 CNAME 解析记录指向集群接入地址：</p>
        <el-alert type="info" :closable="false" class="mb-3">
          <template #title>{{ clusterDns }}（可在「站点配置 → 基本信息」中查看具体接入地址）</template>
        </el-alert>
        <div class="d-flex justify-content-end">
          <el-button type="primary" @click="wizardStep = 1">已添加解析，下一步</el-button>
        </div>
      </template>
      <template v-else-if="wizardStep === 0 && wizardRow">
        <p>站点 <b>{{ (wizardRow.domains || []).join(', ') }}</b> 为 <b>NS 托管接入</b>（智能 DNS 直接应答节点 IP，无需 CNAME）：</p>
        <el-alert type="success" :closable="false" class="mb-3">
          <template #title>请到「域名解析 → NS托管接入」确认该域名已登记，并将子域 NS 记录指向接入主机（NS 接入主机，见集群 DNS 域名设置）。可点击「委派检测」验证生效。</template>
        </el-alert>
        <div class="d-flex justify-content-end">
          <el-button type="primary" @click="wizardStep = 1">下一步</el-button>
        </div>
      </template>
      <template v-else-if="wizardStep === 1">
        <el-form label-width="90px">
          <el-form-item label="源站地址" required>
            <el-input v-model="wizardOrigin" placeholder="如 1.2.3.4 或 origin.example.com" />
          </el-form-item>
        </el-form>
        <div class="d-flex justify-content-end">
          <el-button @click="wizardStep = 0">上一步</el-button>
          <el-button type="primary" :loading="wizardSaving" @click="wizardSave">保存并完成</el-button>
        </div>
      </template>
      <template v-else>
        <el-result icon="success" title="接入完成" sub-title="解析生效与证书签发需要几分钟，稍后可在用量统计中查看流量" />
        <div class="d-flex justify-content-end">
          <el-button type="primary" @click="wizard = false">关闭</el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog v-model="dialog" :title="form.id ? '编辑网站' : '新增网站'" width="560px">
      <el-form label-width="100px">
        <el-form-item label="域名" required>
          <el-input v-model="form.domainsText" type="textarea" :rows="2" :disabled="!!form.id" placeholder="每行一个域名，如 www.example.com" />
          <div v-if="!form.id" class="text-muted small mt-1">创建后请按所选接入方式完成解析（CNAME 或 NS 托管）</div>
        </el-form-item>
        <el-form-item v-if="!form.id" label="接入方式">
          <el-radio-group v-model="form.accessMode">
            <el-radio-button value="cname">CNAME 接入</el-radio-button>
            <el-radio-button value="ns">NS 托管接入</el-radio-button>
          </el-radio-group>
          <div class="text-muted small mt-1">
            CNAME：解析指向集群接入域名，切换节点灵活；NS 托管：智能 DNS 直接应答节点 IP（DNS 拉平，无 CNAME 中间层），需到「域名解析 → NS托管接入」完成 NS 委派
          </div>
        </el-form-item>
        <el-form-item v-if="!form.id" label="加速区域">
          <el-radio-group v-model="form.region">
            <el-radio-button value="global">全球</el-radio-button>
            <el-radio-button value="cn">中国大陆</el-radio-button>
            <el-radio-button value="overseas">非中国大陆</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="!form.id && form.region === 'cn'" label="备案号">
          <el-input v-model="form.icpNumber" placeholder="如 粤ICP备2026000001号" style="width:280px" />
          <div class="text-muted small mt-1">中国大陆区域要求一级域名已备案通过；未备案将无法创建，请先到「备案管理」提交备案</div>
        </el-form-item>
        <el-form-item label="源站" required>
          <el-select v-model="form.originScheme" style="width:90px"><el-option value="http" label="http" /><el-option value="https" label="https" /><el-option value="s3" label="s3" /></el-select>
          <el-input v-model="form.originHost" placeholder="源站地址" style="width:180px" class="mx-1" />
          <el-input-number v-model="form.originPort" :min="1" :max="65535" />
        </el-form-item>
        <el-form-item label="SSL 证书">
          <el-radio-group v-model="form.sslMode" size="small">
            <el-radio-button value="auto">自动签发</el-radio-button>
            <el-radio-button value="manual">手动上传</el-radio-button>
            <el-radio-button value="off">关闭 HTTPS</el-radio-button>
          </el-radio-group>
          <div class="text-muted small mt-1" v-if="form.sslMode === 'auto'">系统自动向 Let's Encrypt 申请并续期证书，免费</div>
          <div class="text-muted small mt-1" v-else-if="form.sslMode === 'manual'">请在「证书管理」页上传证书</div>
        </el-form-item>
        <el-form-item label="强制HTTPS"><el-switch v-model="form.forceHttps" /></el-form-item>
        <el-form-item label="页面缓存"><el-switch v-model="form.cacheEnabled" /><span class="ms-2 text-muted small">开启后静态内容边缘缓存加速</span></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
