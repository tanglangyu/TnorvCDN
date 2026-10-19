<script setup lang="ts">
/** CDN 集群列表 —— 对接 TnorvCdn 主控 /api/admin/clusters（含 DDoS 配置、DNS 同步、集群默认配置套件） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const clusters = ref<any[]>([])
const nodes = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})

// 关键词筛选（后端 /admin/clusters 支持 page/pageSize/q；当前集群量小，前端一次拉全量分页）
const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const [c, n] = await Promise.all([
    cdnApi.get('/admin/clusters', { page: 1, pageSize: 200, q: filterQ.value || undefined }),
    cdnApi.get('/admin/nodes'),
  ])
  const arr = Array.isArray(c) ? c : (c?.items || [])
  pager.value.total = Array.isArray(c) ? arr.length : (c?.total ?? arr.length)
  clusters.value = arr
  nodes.value = n
  return arr
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

function openEdit(row: any) {
  form.value = row ? JSON.parse(JSON.stringify(row)) : { name: '', dnsDomain: '', enabled: true, ddos: { enabled: false, connLimit: 100, connRate: 50, burstRate: 100, synFloodLimit: 200, ports: [80, 443] } }
  if (!form.value.ddos) form.value.ddos = { enabled: false, connLimit: 100, connRate: 50, burstRate: 100, synFloodLimit: 200, ports: [80, 443] }
  dialog.value = true
}

async function save() {
  if (!form.value.name) return ElMessage.warning('请填写名称')
  if (form.value.id) await cdnApi.put(`/admin/clusters/${form.value.id}`, form.value)
  else await cdnApi.post('/admin/clusters', form.value)
  ElMessage.success('已保存')
  dialog.value = false
  gate.value?.refresh?.()
}

// 集群设置抽屉：健康检查 + 阈值告警（Ops）+ 默认配置套件（Defaults）
const opsDrawer = ref(false)
const opsForm = ref<any>({})
const opsSaving = ref(false)
const defsSaving = ref(false)
async function openOps(row: any) {
  opsForm.value = { clusterId: row.id, name: row.name, tab: 'ops' }
  try {
    const o = await cdnApi.get(`/admin/clusters/${row.id}/ops`)
    opsForm.value.cpuPercent = o.cpuPercent || 0
    opsForm.value.memPercent = o.memPercent || 0
    opsForm.value.load = o.load || 0
    opsForm.value.probeIntervalSeconds = o.probeIntervalSeconds || 15
    opsForm.value.probeTimeoutMs = o.probeTimeoutMs || 5000
    opsForm.value.probeFailThreshold = o.probeFailThreshold || 3
    opsForm.value.alertsEnabled = o.alertsEnabled !== false
    opsForm.value.metricsEnabled = o.metricsEnabled !== false
    opsForm.value.upgradeWindowStart = o.upgradeWindowStart ?? -1
    opsForm.value.upgradeWindowEnd = o.upgradeWindowEnd ?? -1
    opsForm.value.showNodesOnline = o.showNodesOnline !== false
    opsForm.value.logKeepDays = o.logKeepDays ?? 30
    opsForm.value.auditKeepDays = o.auditKeepDays ?? 90
    opsForm.value.smsKeepDays = o.smsKeepDays ?? 90
  } catch { /* 默认值 */ }
  try {
    const d = await cdnApi.get(`/admin/clusters/${row.id}/defaults`)
    // 默认配置套件：缓存 / 安全 / 高级 三块（站点未配置的子块由集群默认兜底）
    opsForm.value.defCache = d.cache || { enabled: false, rules: [] }
    opsForm.value.defSecurity = d.security || {}
    opsForm.value.defAdvanced = d.advanced || {}
  } catch { /* 空默认 */ }
  // 状态页数据（services / ddos-protection/status / dns/records）
  opsForm.value.services = []
  opsForm.value.ddosStatus = null
  opsForm.value.dnsRecords = null
  cdnApi.get(`/admin/clusters/${row.id}/services`).then((v) => { opsForm.value.services = v }).catch(() => { })
  cdnApi.get(`/admin/clusters/${row.id}/ddos-status`).then((v) => { opsForm.value.ddosStatus = v }).catch(() => { })
  cdnApi.get(`/admin/clusters/${row.id}/dns-records`).then((v) => { opsForm.value.dnsRecords = v }).catch(() => { })
  opsDrawer.value = true
}
async function saveOps() {
  opsSaving.value = true
  try {
    const { clusterId, name, ...cfg } = opsForm.value
    await cdnApi.put(`/admin/clusters/${clusterId}/ops`, {
      cpuPercent: cfg.cpuPercent, memPercent: cfg.memPercent, load: cfg.load,
      probeIntervalSeconds: cfg.probeIntervalSeconds, probeTimeoutMs: cfg.probeTimeoutMs, probeFailThreshold: cfg.probeFailThreshold,
      alertsEnabled: cfg.alertsEnabled !== false,
      metricsEnabled: cfg.metricsEnabled !== false,
      upgradeWindowStart: cfg.upgradeWindowStart ?? -1,
      upgradeWindowEnd: cfg.upgradeWindowEnd ?? -1,
      showNodesOnline: cfg.showNodesOnline !== false,
      logKeepDays: cfg.logKeepDays ?? 30,
      auditKeepDays: cfg.auditKeepDays ?? 90,
      smsKeepDays: cfg.smsKeepDays ?? 90,
    })
    ElMessage.success('阈值/健康检查/维护窗口/日志保留策略已保存')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { opsSaving.value = false }
}
async function saveDefaults() {
  defsSaving.value = true
  try {
    await cdnApi.put(`/admin/clusters/${opsForm.value.clusterId}/defaults`, {
      cache: opsForm.value.defCache,
      security: opsForm.value.defSecurity,
      advanced: opsForm.value.defAdvanced,
    })
    ElMessage.success('默认配置已保存并重发布（站点未配置子块由集群默认兜底）')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { defsSaving.value = false }
}

async function del(row: any) {
  try {
    await cdnApi.delete(`/admin/clusters/${row.id}`)
    ElMessage.success('已删除')
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || '删除失败')
  }
}

async function syncDns(row: any) {
  await cdnApi.post(`/admin/dns/clusters/${row.id}/sync`)
  ElMessage.success('DNS 同步指令已执行')
}

const nodeCount = (id: number) => nodes.value.filter((n) => n.clusterId === id).length

// A6：内联节点指标（clusters 接口已内联 nodes[] 含 cpu/mem/conns）
const nodeOf = (row: any) => row.nodes || nodes.value.filter((n: any) => n.clusterId === row.id).map((n: any) => ({
  id: n.id, name: n.name, isOnline: n.isOnline, cpu: 0, mem: 0, conns: 0,
}))
const pct = (v: any) => Math.max(2, Math.min(100, Math.round(Number(v || 0))))
const barColor = (v: any) => (Number(v || 0) > 85 ? '#F56C6C' : Number(v || 0) > 65 ? '#E6A23C' : '#67C23A')

// A6：安装节点命令复制卡
const installDialog = ref(false)
const installCluster = ref<any>(null)
const installToken = ref('')
async function openInstall(row: any) {
  installCluster.value = row
  installDialog.value = true
  try {
    const r = (await cdnApi.post('/admin/nodes', { name: `edge-${row.id}-${Date.now() % 10000}`, clusterId: row.id })) as any
    installToken.value = r?.installToken ?? r?.data?.installToken ?? ''
  } catch { installToken.value = '' }
}
const installCmd = () => installToken.value
  ? `curl -fsSL ${location.protocol}//${location.host}/downloads/install-node.sh | sudo bash -s -- --server ${location.host} --token ${installToken.value}`
  : '（创建节点失败，请检查权限后重试）'
const copied = ref(false)
async function copyInstall() {
  try { await navigator.clipboard.writeText(installCmd()) } catch {
    const ta = document.createElement('textarea'); ta.value = installCmd(); document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove()
  }
  copied.value = true
  setTimeout(() => (copied.value = false), 1500)
}

function rowCommand(cmd: string, row: any) {
  if (cmd === 'edit') openEdit(row)
  else if (cmd === 'dns') syncDns(row)
  else if (cmd === 'install') openInstall(row)
  else if (cmd === 'del') del(row)
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">集群列表</h2><div class="text-muted">边缘节点 · 对接 TnorvCDN 主控</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openEdit(null)">+ 新增集群</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="集群名称关键词" size="small" clearable style="width: 220px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
          </div>
          <el-table :data="clusters" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column prop="name" label="名称" />
            <el-table-column prop="dnsDomain" label="DNS 子域名" />
            <el-table-column type="expand">
              <template #default="{ row }">
                <div style="padding: 4px 24px 8px">
                  <div v-for="n in nodeOf(row)" :key="n.id" class="d-flex align-items-center gap-3 py-1" style="min-width: 480px">
                    <span style="width: 130px" class="text-truncate">
                      <span class="me-1" :style="{ width: '8px', height: '8px', borderRadius: '50%', display: 'inline-block', background: n.isOnline ? '#67C23A' : '#909399' }"></span>
                      {{ n.name }}
                    </span>
                    <span class="text-muted" style="width: 34px">CPU</span>
                    <el-progress :percentage="pct(n.cpu)" :color="barColor(n.cpu)" :stroke-width="8" style="width: 130px" />
                    <span class="text-muted" style="width: 40px">内存</span>
                    <el-progress :percentage="pct(n.mem)" :color="barColor(n.mem)" :stroke-width="8" style="width: 130px" />
                    <span class="text-muted small">连接 {{ n.conns ?? '—' }}</span>
                  </div>
                  <el-empty v-if="!nodeOf(row).length" description="暂无节点：点击「管理 → 安装节点」获取安装命令" :image-size="50" />
                </div>
              </template>
            </el-table-column>
            <el-table-column label="节点数" width="80"><template #default="{ row }">{{ nodeCount(row.id) }}</template></el-table-column>
            <el-table-column label="DDoS防护" width="100">
              <template #default="{ row }">
                <el-tag :type="row.ddos?.enabled ? 'success' : 'info'" size="small">{{ row.ddos?.enabled ? '已启用' : '未启用' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="150" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
                  <el-button size="small" type="warning" plain @click="openOps(row)">设置</el-button>
                  <el-dropdown @command="(cmd: string) => rowCommand(cmd, row)">
                    <el-button size="small">管理<i class="ti ti-chevron-down ms-1"></i></el-button>
                    <template #dropdown>
                      <el-dropdown-menu>
                        <el-dropdown-item command="edit">编辑</el-dropdown-item>
                        <el-dropdown-item command="install">安装节点</el-dropdown-item>
                        <el-dropdown-item command="dns">同步DNS</el-dropdown-item>
                        <el-dropdown-item command="del" divided><span class="text-danger">删除</span></el-dropdown-item>
                      </el-dropdown-menu>
                    </template>
                  </el-dropdown>
                </div>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
        </el-card>

        <el-dialog align-center v-model="dialog" :title="form.id ? '编辑集群' : '新增集群'" width="560px">
          <el-form label-width="120px">
            <el-form-item label="名称"><el-input v-model="form.name" /></el-form-item>
            <el-form-item label="DNS 子域名"><el-input v-model="form.dnsDomain" placeholder="cdn.example.com（智能 DNS CNAME 目标）" /></el-form-item>
            <el-divider>DDoS 防护（nftables 内核层限速）</el-divider>
            <el-form-item label="启用防护"><el-switch v-model="form.ddos.enabled" /></el-form-item>
            <template v-if="form.ddos.enabled">
              <el-form-item label="并发连接数"><el-input-number v-model="form.ddos.connLimit" :min="10" /></el-form-item>
              <el-form-item label="新建速率"><el-input-number v-model="form.ddos.connRate" :min="5" /> 连接/秒</el-form-item>
              <el-form-item label="SYN防护"><el-input-number v-model="form.ddos.synFloodLimit" :min="10" /> SYN/秒</el-form-item>
              <el-form-item label="生效端口"><el-input :model-value="(form.ddos.ports || []).join(',')" @update:model-value="(v: string) => form.ddos.ports = v.split(',').map((x: string) => parseInt(x)).filter((n: number) => n > 0)" /></el-form-item>
              <el-form-item label="管理网段白名单">
                <el-input :model-value="(form.ddos.adminAllowCidrs || []).join(',')" @update:model-value="(v: string) => form.ddos.adminAllowCidrs = v.split(',').map((s: string) => s.trim()).filter(Boolean)" placeholder="10.0.0.0/8（network-security：永远放行，优先于限速规则）" />
              </el-form-item>
            </template>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" @click="save">保存</el-button>
          </template>
        </el-dialog>

        <el-drawer v-model="opsDrawer" :title="`集群设置 - ${opsForm.name || ''}`" size="560px">
          <el-tabs v-model="opsForm.tab">
            <el-tab-pane label="阈值/健康检查" name="ops">
              <h5 class="mb-2">阈值告警（0 = 不启用，越限发管理员通知，30 分钟节流）</h5>
              <el-form label-width="150px">
                <el-form-item label="CPU 使用率 %"><el-input-number v-model="opsForm.cpuPercent" :min="0" :max="100" /></el-form-item>
                <el-form-item label="内存使用率 %"><el-input-number v-model="opsForm.memPercent" :min="0" :max="100" /></el-form-item>
                <el-form-item label="1min 负载阈值"><el-input-number v-model="opsForm.load" :min="0" :max="100" :step="0.5" /></el-form-item>
              </el-form>
              <el-divider />
              <h5 class="mb-2">健康检查（站点区域探测参数，随配置下发节点）</h5>
              <el-form label-width="150px">
                <el-form-item label="探测间隔（秒）"><el-input-number v-model="opsForm.probeIntervalSeconds" :min="5" :max="300" /></el-form-item>
                <el-form-item label="单次超时（ms）"><el-input-number v-model="opsForm.probeTimeoutMs" :min="500" :max="30000" :step="500" /></el-form-item>
                <el-form-item label="失败阈值（次）"><el-input-number v-model="opsForm.probeFailThreshold" :min="1" :max="10" /></el-form-item>
              </el-form>
              <el-divider />
              <h5 class="mb-2">通知与指标（message / metrics）</h5>
              <el-form label-width="150px">
                <el-form-item label="阈值告警开关">
                  <el-switch v-model="opsForm.alertsEnabled" />
                  <span class="text-muted ms-2 small">关闭后越限不再发管理员通知</span>
                </el-form-item>
                <el-form-item label="实时指标采集">
                  <el-switch v-model="opsForm.metricsEnabled" />
                  <span class="text-muted ms-2 small">关闭后看板实时指标不再更新</span>
                </el-form-item>
              </el-form>
              <el-divider />
              <h5 class="mb-2">运营展示（用户端信息面）</h5>
              <el-form label-width="150px">
                <el-form-item label="节点在线数">
                  <el-switch v-model="opsForm.showNodesOnline" />
                  <span class="text-muted ms-2 small">关闭后用户端站点列表不显示「节点在线」列，接口也不再返回该字段</span>
                </el-form-item>
              </el-form>
              <el-divider />
              <h5 class="mb-2">升级维护窗口（schedule，UTC 小时；-1 = 不限）</h5>
              <el-form label-width="150px">
                <el-form-item label="窗口起始">
                  <el-input-number v-model="opsForm.upgradeWindowStart" :min="-1" :max="23" />
                </el-form-item>
                <el-form-item label="窗口结束">
                  <el-input-number v-model="opsForm.upgradeWindowEnd" :min="-1" :max="23" />
                  <span class="text-muted ms-2 small">窗口外待升级节点保持不动（如 22~6 = 仅凌晨升级）</span>
                </el-form-item>
              </el-form>
              <el-divider />
              <h5 class="mb-2">日志保留策略（0 = 永久保留，每小时后台清理）</h5>
              <el-form label-width="150px">
                <el-form-item label="访问日志（天）">
                  <el-input-number v-model="opsForm.logKeepDays" :min="0" :max="3650" />
                </el-form-item>
                <el-form-item label="审计日志（天）">
                  <el-input-number v-model="opsForm.auditKeepDays" :min="0" :max="3650" />
                </el-form-item>
                <el-form-item label="短信日志（天）">
                  <el-input-number v-model="opsForm.smsKeepDays" :min="0" :max="3650" />
                  <span class="text-muted ms-2 small">多集群取最小值全局生效</span>
                </el-form-item>
              </el-form>
              <div class="mt-3">
                <el-button type="primary" :loading="opsSaving" @click="saveOps">保存</el-button>
              </div>
            </el-tab-pane>

            <el-tab-pane label="默认配置套件" name="defaults">
              <el-alert type="info" :closable="false" class="mb-3"
                title="站点未配置的子块由集群默认兜底下发（站点级优先）" />
              <h5 class="mb-2">默认缓存</h5>
              <el-form label-width="150px" size="small">
                <el-form-item label="启用缓存">
                  <el-switch v-model="opsForm.defCache.enabled" />
                  <span class="text-muted ms-2 small">站点未启用缓存时生效</span>
                </el-form-item>
                <el-form-item v-if="opsForm.defCache.enabled" label="默认规则">
                  <el-table :data="opsForm.defCache.rules" size="small" style="max-width: 340px">
                    <el-table-column label="匹配" min-width="110">
                      <template #default="{ row }">
                        <el-select v-model="row.matchType" size="small" style="width: 100%">
                          <el-option value="extension" label="扩展名" /><el-option value="path-prefix" label="路径前缀" />
                          <el-option value="all" label="全部" /><el-option value="homepage" label="首页" />
                        </el-select>
                      </template>
                    </el-table-column>
                    <el-table-column label="值" min-width="100"><template #default="{ row }"><el-input v-model="row.value" size="small" placeholder="png,jpg" /></template></el-table-column>
                    <el-table-column label="TTL(s)" width="120"><template #default="{ row }"><el-input-number v-model="row.ttlSeconds" :min="1" size="small" controls-position="right" style="width: 100%" /></template></el-table-column>
                    <el-table-column width="60"><template #default="{ $index }"><el-button size="small" text type="danger" @click="opsForm.defCache.rules.splice($index, 1)">删</el-button></template></el-table-column>
                  </el-table>
                  <el-button size="small" class="mt-1" @click="opsForm.defCache.rules.push({ matchType: 'extension', value: 'png,jpg,css,js', ttlSeconds: 3600, clientTtlSeconds: 0, ignoreQueryString: false })">+ 添加</el-button>
                </el-form-item>
              </el-form>

              <el-divider />
              <h5 class="mb-2">默认安全（子块为空时兜底）</h5>
              <el-form label-width="150px" size="small">
                <el-form-item label="默认 CC 防护">
                  <el-switch v-model="opsForm.defSecurity.cc.enabled" />
                  <template v-if="opsForm.defSecurity.cc?.enabled">
                    <el-input-number v-model="opsForm.defSecurity.cc.threshold" :min="10" size="small" controls-position="right" class="mx-2" style="width: 110px" />
                    <span class="small">次/分钟</span>
                  </template>
                </el-form-item>
                <el-form-item label="默认 IP 黑名单">
                  <el-input :model-value="(opsForm.defSecurity.ipBlacklist || []).map((x: any) => x.cidr).join(',')"
                    @update:model-value="(v: string) => opsForm.defSecurity.ipBlacklist = v.split(',').map((s: string) => s.trim()).filter(Boolean).map((c: string) => ({ cidr: c }))"
                    placeholder="1.2.3.4, 10.0.0.0/8" />
                </el-form-item>
                <el-form-item label="默认 IP 白名单">
                  <el-input :model-value="(opsForm.defSecurity.ipWhitelist || []).map((x: any) => x.cidr).join(',')"
                    @update:model-value="(v: string) => opsForm.defSecurity.ipWhitelist = v.split(',').map((s: string) => s.trim()).filter(Boolean).map((c: string) => ({ cidr: c }))"
                    placeholder="命中后放行整条安全链" />
                </el-form-item>
                <el-form-item label="默认 5秒盾">
                  <el-switch v-model="opsForm.defSecurity.jsShield.enabled" />
                </el-form-item>
              </el-form>

              <el-divider />
              <h5 class="mb-2">默认高级（零值字段兜底）</h5>
              <el-form label-width="150px" size="small">
                <el-form-item label="默认压缩"><el-switch v-model="opsForm.defAdvanced.compression" /></el-form-item>
                <el-form-item label="默认 WebP"><el-switch v-model="opsForm.defAdvanced.webpOptimization" /></el-form-item>
                <el-form-item label="默认字符集"><el-input v-model="opsForm.defAdvanced.charset" style="width: 180px" placeholder="utf-8" /></el-form-item>
                <el-form-item label="默认请求限制">
                  <el-input-number v-model="opsForm.defAdvanced.requestLimitPerMinute" :min="0" size="small" controls-position="right" style="width: 150px" />
                  <span class="text-muted ms-2 small">0 = 不限制</span>
                </el-form-item>
                <el-form-item label="默认请求体上限">
                  <el-input-number v-model="opsForm.defAdvanced.maxBodySizeMB" :min="0" size="small" controls-position="right" style="width: 150px" />
                  <span class="text-muted ms-2 small">MB，0 = 不限</span>
                </el-form-item>
              </el-form>
              <div class="mt-3">
                <el-button type="primary" :loading="defsSaving" @click="saveDefaults">保存并重发布</el-button>
              </div>
</el-tab-pane>

            <el-tab-pane label="服务状态" name="services">
              <el-table :data="opsForm.services" size="small" stripe>
                <el-table-column prop="name" label="节点" min-width="110" />
                <el-table-column label="角色" width="70"><template #default="{ row }"><el-tag size="small" :type="row.role === 'edge' ? 'primary' : 'warning'">{{ row.role }}</el-tag></template></el-table-column>
                <el-table-column label="状态" width="80">
                  <template #default="{ row }"><el-tag size="small" :type="row.online ? 'success' : 'danger'">{{ row.online ? '运行中' : '离线' }}</el-tag></template>
                </el-table-column>
                <el-table-column prop="version" label="版本" min-width="90" />
                <el-table-column label="最近心跳" min-width="140">
                  <template #default="{ row }">{{ row.lastHeartbeat ? new Date(row.lastHeartbeat).toLocaleString() : '—' }}</template>
                </el-table-column>
                <el-table-column label="IP" min-width="120"><template #default="{ row }">{{ (row.ips || []).join(', ') || '—' }}</template></el-table-column>
              </el-table>
              <el-empty v-if="!(opsForm.services || []).length" description="暂无节点" :image-size="50" />
            </el-tab-pane>

            <el-tab-pane label="DDoS 状态" name="ddos">
              <template v-if="opsForm.ddosStatus">
                <el-descriptions :column="2" border size="small">
                  <el-descriptions-item label="防护开关">
                    <el-tag size="small" :type="opsForm.ddosStatus.enabled ? 'success' : 'info'">{{ opsForm.ddosStatus.enabled ? '已启用' : '未启用' }}</el-tag>
                  </el-descriptions-item>
                  <el-descriptions-item label="配置版本">{{ opsForm.ddosStatus.clusterConfigVersion }}</el-descriptions-item>
                  <el-descriptions-item label="并发连接上限">{{ opsForm.ddosStatus.connLimit || '—' }}</el-descriptions-item>
                  <el-descriptions-item label="新建速率">{{ opsForm.ddosStatus.connRate || '—' }}/秒</el-descriptions-item>
                  <el-descriptions-item label="SYN 上限">{{ opsForm.ddosStatus.synFloodLimit || '—' }}/秒</el-descriptions-item>
                  <el-descriptions-item label="生效端口">{{ (opsForm.ddosStatus.ports || []).join(', ') || '—' }}</el-descriptions-item>
                </el-descriptions>
                <div class="mt-3">
                  <div class="small text-muted mb-1">节点下发进度：已确认 {{ opsForm.ddosStatus.nodeApplied }} / 在线 {{ opsForm.ddosStatus.nodeOnline }} / 总数 {{ opsForm.ddosStatus.nodeTotal }}</div>
                  <el-progress :percentage="opsForm.ddosStatus.nodeTotal ? Math.round(opsForm.ddosStatus.nodeApplied * 100 / opsForm.ddosStatus.nodeTotal) : 0" :stroke-width="12" />
                </div>
              </template>
              <el-empty v-else description="加载中" :image-size="50" />
            </el-tab-pane>

            <el-tab-pane label="DNS 记录" name="dnsrec">
              <template v-if="opsForm.dnsRecords">
                <div class="small text-muted mb-2">入口域名：<b>{{ opsForm.dnsRecords.dnsDomain || '（未设置）' }}</b> 的实时解析</div>
                <el-table :data="opsForm.dnsRecords.records" size="small" stripe style="max-width: 480px">
                  <el-table-column prop="type" label="类型" width="110" />
                  <el-table-column prop="name" label="主机" min-width="140" />
                  <el-table-column prop="value" label="值" min-width="160" />
                </el-table>
                <el-empty v-if="!(opsForm.dnsRecords.records || []).length" description="无解析记录" :image-size="40" />
                <el-divider />
                <div class="small text-muted mb-2">智能 DNS 域名同步状态（{{ (opsForm.dnsRecords.nsDomains || []).length }}）</div>
                <el-table :data="(opsForm.dnsRecords.nsDomains || []).slice(0, 10)" size="small" stripe style="max-width: 480px">
                  <el-table-column prop="name" label="域名" min-width="150" />
                  <el-table-column prop="type" label="类型" width="70" />
                  <el-table-column label="同步" width="110">
                    <template #default="{ row }">
                      <el-tag size="small" :type="row.syncStatus === 'synced' ? 'success' : (row.syncStatus === 'failed' ? 'danger' : 'info')">
                        {{ row.syncStatus === 'synced' ? '已生效' : (row.syncStatus === 'failed' ? '失败' : '仅本站') }}
                      </el-tag>
                    </template>
                  </el-table-column>
                </el-table>
              </template>
              <el-empty v-else description="加载中" :image-size="50" />
            </el-tab-pane>
          </el-tabs>
        </el-drawer>
        <!-- A6 安装节点命令复制卡 -->
        <el-dialog v-model="installDialog" :title="`安装节点：${installCluster?.name || ''}`" width="640px">
          <el-alert type="info" :closable="false" class="mb-3"
            title="在目标服务器执行以下命令，节点将自动注册并加入集群（token 仅显示一次）" />
          <el-input :model-value="installCmd()" type="textarea" :rows="3" readonly />
          <div class="mt-3 d-flex justify-content-end">
            <el-button type="primary" @click="copyInstall">{{ copied ? '已复制 ✓' : '一键复制安装命令' }}</el-button>
          </div>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
