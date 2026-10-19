<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>网站管理</span>
        <div>
          <el-button @click="purgeDialog = true">缓存刷新</el-button>
          <el-button type="primary" @click="openEdit(null)"><el-icon><Plus /></el-icon>创建网站</el-button>
        </div>
      </div>
    </template>
    <el-table :data="servers" v-loading="loading">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column label="域名">
        <template #default="{ row }"><el-tag v-for="d in row.domains" :key="d" size="small" style="margin-right:4px">{{ d }}</el-tag></template>
      </el-table-column>
      <el-table-column prop="serverType" label="类型" width="80" />
      <el-table-column label="源站">
        <template #default="{ row }">{{ (row.origin?.primaries || []).map(o => o.host + ':' + o.port).join(', ') }}</template>
      </el-table-column>
      <el-table-column label="缓存" width="80">
        <template #default="{ row }"><el-tag :type="row.cache?.enabled ? 'success' : 'info'" size="small">{{ row.cache?.enabled ? '开' : '关' }}</el-tag></template>
      </el-table-column>
      <el-table-column label="HTTPS" width="90">
        <template #default="{ row }">{{ (row.httpsPorts || []).length ? row.httpsPorts.join(',') : '-' }}</template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'danger'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag></template>
      </el-table-column>
      <el-table-column label="操作" width="180">
        <template #default="{ row }">
          <el-button size="small" @click="openEdit(row)">编辑</el-button>
          <el-popconfirm title="确认删除该网站？" @confirm="del(row)">
            <template #reference><el-button size="small" type="danger">删除</el-button></template>
          </el-popconfirm>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialog" :title="form.id ? '编辑网站' : '创建网站'" width="720px">
      <el-form :model="form" label-width="110px">
        <el-form-item label="域名">
          <el-select v-model="form.domains" multiple filterable allow-create default-first-option placeholder="输入域名后回车，支持 *.example.com" style="width:100%" />
        </el-form-item>
        <el-form-item label="类型">
          <el-radio-group v-model="form.serverType">
            <el-radio value="http">HTTP 反代</el-radio>
            <el-radio value="tcp">TCP 负载均衡</el-radio>
          </el-radio-group>
        </el-form-item>
        <template v-if="form.serverType === 'http'">
          <el-form-item label="HTTP 端口"><el-input v-model="httpPortsStr" placeholder="80" /></el-form-item>
          <el-form-item label="HTTPS 端口"><el-input v-model="httpsPortsStr" placeholder="443（留空不启用）" /></el-form-item>
          <el-form-item label="强制 HTTPS"><el-switch v-model="form.forceHttps" /></el-form-item>
        </template>
        <el-form-item v-else label="TCP 端口"><el-input v-model="l4PortsStr" placeholder="9001" /></el-form-item>
        <el-form-item label="主源站">
          <div v-for="(o, i) in form.origin.primaries" :key="i" class="origin-row">
            <el-select v-model="o.scheme" style="width:90px"><el-option value="http" /><el-option value="https" /><el-option v-if="form.serverType!=='http'" value="tcp" /></el-select>
            <el-input v-model="o.host" placeholder="IP 或域名" style="width:200px" />
            <el-input-number v-model="o.port" :min="1" :max="65535" style="width:110px" />
            <el-input-number v-model="o.weight" :min="1" :max="100" style="width:100px" />
            <el-button type="danger" circle size="small" @click="form.origin.primaries.splice(i,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <el-button size="small" @click="addOrigin('primaries')">+ 添加主源站</el-button>
        </el-form-item>
        <el-form-item label="备用源站">
          <div v-for="(o, i) in form.origin.backups" :key="i" class="origin-row">
            <el-select v-model="o.scheme" style="width:90px"><el-option value="http" /><el-option value="https" /></el-select>
            <el-input v-model="o.host" style="width:200px" />
            <el-input-number v-model="o.port" :min="1" :max="65535" style="width:110px" />
            <el-button type="danger" circle size="small" @click="form.origin.backups.splice(i,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <el-button size="small" @click="addOrigin('backups')">+ 添加备用源站</el-button>
        </el-form-item>
        <el-form-item label="回源 Host"><el-input v-model="form.origin.hostOverride" placeholder="留空=跟随访问域名" /></el-form-item>
        <el-divider>路径源站（按路径分流到不同源站）</el-divider>
        <div v-for="(pr, pi) in form.origin.pathRules" :key="pi" class="waf-rule">
          <div class="waf-head">
            <el-input v-model="pr.pathPrefix" placeholder="路径前缀，如 /api" style="width:180px" size="small" />
            <el-input v-model="pr.hostOverride" placeholder="回源Host(可选)" style="width:180px" size="small" />
            <el-button type="danger" circle size="small" @click="form.origin.pathRules.splice(pi,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <div v-for="(o, i) in pr.primaries" :key="'p'+i" class="origin-row">
            <el-select v-model="o.scheme" style="width:90px" size="small"><el-option value="http" /><el-option value="https" /></el-select>
            <el-input v-model="o.host" style="width:180px" size="small" />
            <el-input-number v-model="o.port" :min="1" :max="65535" style="width:110px" size="small" />
            <el-button type="danger" circle size="small" @click="pr.primaries.splice(i,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <el-button size="small" @click="pr.primaries.push({scheme:'http',host:'',port:80,weight:1,enabled:true})">+ 主源站</el-button>
        </div>
        <el-form-item label=" ">
          <el-button size="small" @click="form.origin.pathRules.push({pathPrefix:'',primaries:[{scheme:'http',host:'',port:80,weight:1,enabled:true}],backups:[],hostOverride:''})">+ 添加路径源站规则</el-button>
        </el-form-item>
        <el-form-item label="调度算法">
          <el-select v-model="form.origin.scheduleAlgorithm">
            <el-option value="round-robin" label="轮询" /><el-option value="random" label="随机" /><el-option value="ip-hash" label="IP Hash" />
          </el-select>
        </el-form-item>
        <el-divider>缓存设置</el-divider>
        <el-form-item label="启用缓存"><el-switch v-model="form.cache.enabled" /></el-form-item>
        <template v-if="form.cache.enabled">
          <el-form-item label="缓存规则">
            <div v-for="(r, i) in form.cache.rules" :key="i" class="origin-row">
              <el-select v-model="r.matchType" style="width:120px">
                <el-option value="extension" label="扩展名" /><el-option value="path-prefix" label="路径前缀" /><el-option value="homepage" label="首页" /><el-option value="all" label="全部" />
              </el-select>
              <el-input v-model="r.value" placeholder="html,png 或 /static/" style="width:180px" :disabled="['homepage','all'].includes(r.matchType)" />
              <el-input-number v-model="r.ttlSeconds" :min="1" placeholder="TTL秒" style="width:130px" />
              <el-button type="danger" circle size="small" @click="form.cache.rules.splice(i,1)"><el-icon><Delete /></el-icon></el-button>
            </div>
            <el-button size="small" @click="form.cache.rules.push({ matchType: 'extension', value: '', ttlSeconds: 600 })">+ 添加规则</el-button>
          </el-form-item>
        </template>
        <el-divider>安全设置</el-divider>
        <el-form-item label="CC 防护"><el-switch v-model="form.security.cc.enabled" /></el-form-item>
        <template v-if="form.security.cc.enabled">
          <el-form-item label="阈值"><el-input-number v-model="form.security.cc.threshold" :min="10" /> 请求 / 分钟 / IP</el-form-item>
          <el-form-item label="封禁时长"><el-input-number v-model="form.security.cc.banSeconds" :min="10" /> 秒</el-form-item>
        </template>
        <el-form-item label="IP 黑名单">
          <el-select v-model="blacklistCidrs" multiple filterable allow-create default-first-option placeholder="1.2.3.4 或 10.0.0.0/8" style="width:100%" />
        </el-form-item>
        <el-divider>URL 防盗链（时间戳签名）</el-divider>
        <el-form-item label="启用鉴权"><el-switch v-model="form.security.urlAuth.enabled" /></el-form-item>
        <template v-if="form.security.urlAuth.enabled">
          <el-form-item label="签名方案">
            <el-select v-model="form.security.urlAuth.type" style="width:240px">
              <el-option value="a" label="A 方案 md5(uri-ts-rand-key)" />
              <el-option value="b" label="B 方案 md5(key+ts+uri) hex时间" />
              <el-option value="c" label="C 方案 md5(key+uri+hex_ts)" />
              <el-option value="d" label="D 方案 md5(uri-ts-key)-ts" />
            </el-select>
          </el-form-item>
          <el-form-item label="密钥"><el-input v-model="form.security.urlAuth.key" placeholder="签名密钥" show-password /></el-form-item>
          <el-form-item label="有效期"><el-input-number v-model="form.security.urlAuth.expirySeconds" :min="60" /> 秒</el-form-item>
          <el-form-item label=" ">
            <el-text size="small" type="info">请求需携带 ?auth_key=...（按所选方案生成），签名不匹配或过期返回 403</el-text>
          </el-form-item>
        </template>
        <el-divider>5秒盾（JS 挑战）</el-divider>
        <el-form-item label="启用5秒盾"><el-switch v-model="form.security.jsShield.enabled" /></el-form-item>
        <template v-if="form.security.jsShield.enabled">
          <el-form-item label="挑战模式">
            <el-select v-model="form.security.jsShield.mode" style="width:240px">
              <el-option value="js" label="JS 跳转挑战" /><el-option value="meta" label="Meta 刷新" />
            </el-select>
          </el-form-item>
          <el-form-item label="签名密钥"><el-input v-model="form.security.jsShield.secret" placeholder="挑战 token 签名密钥（可留空用内置盐）" show-password /></el-form-item>
          <el-form-item label="Cookie有效期"><el-input-number v-model="form.security.jsShield.cookieSeconds" :min="60" /> 秒</el-form-item>
          <el-form-item label="失败封禁">连续失败 <el-input-number v-model="form.security.jsShield.maxFails" :min="1" /> 次封禁 <el-input-number v-model="form.security.jsShield.banSeconds" :min="60" /> 秒</el-form-item>
          <el-form-item label="例外路径"><el-input v-model="jsShieldExclude" placeholder="逗号分隔，如 /api/callback" /></el-form-item>
        </template>
        <el-divider>边缘脚本（expr-lang 请求改写）</el-divider>
        <div v-for="(es, ei) in form.security.edgeScripts" :key="ei" class="waf-rule">
          <div class="waf-head">
            <el-input v-model="es.name" placeholder="脚本名" style="width:140px" size="small" />
            <el-switch v-model="es.enabled" size="small" />
            <el-button type="danger" circle size="small" @click="form.security.edgeScripts.splice(ei,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <el-input v-model="es.expr" type="textarea" :rows="2" placeholder="method == 'GET' && path startsWith '/old/' ? setPath('/new' + path) : true" />
        </div>
        <el-form-item label=" ">
          <el-button size="small" @click="form.security.edgeScripts.push({name:'',enabled:true,expr:''})">+ 添加边缘脚本</el-button>
          <el-text size="small" type="info">变量：method/path/query/host/ua/ip/header；函数：setPath(p) setQuery(k,v) setHeader(k,v) delHeader(k) respond(status,body)</el-text>
        </el-form-item>
        <el-divider>AI 语义防护（LLM 二次判定）</el-divider>
        <el-form-item label="启用AI防护"><el-switch v-model="form.security.aiGuard.enabled" /></el-form-item>
        <template v-if="form.security.aiGuard.enabled">
          <el-form-item label="API地址"><el-input v-model="form.security.aiGuard.apiUrl" placeholder="OpenAI 兼容 /chat/completions 地址" /></el-form-item>
          <el-form-item label="API密钥"><el-input v-model="form.security.aiGuard.apiKey" show-password /></el-form-item>
          <el-form-item label="模型"><el-input v-model="form.security.aiGuard.model" placeholder="gpt-4o-mini" style="width:240px" /></el-form-item>
          <el-form-item label="采样率"><el-input-number v-model="form.security.aiGuard.sampleRate" :min="1" :max="100" /> %（WAF log 命中请求采样送审）</el-form-item>
          <el-form-item label="拦截阈值"><el-input-number v-model="form.security.aiGuard.blockThreshold" :min="0" :max="1" :step="0.05" /> 恶意分超过则封禁 IP 10 分钟</el-form-item>
        </template>
        <el-divider>WAF 规则引擎</el-divider>
        <div v-for="(w, wi) in form.security.wafRules" :key="wi" class="waf-rule">
          <div class="waf-head">
            <el-input v-model="w.name" placeholder="规则名" style="width:160px" size="small" />
            <el-select v-model="w.action" style="width:110px" size="small">
              <el-option value="block" label="阻止 (403)" /><el-option value="allow" label="放行" />
              <el-option value="redirect" label="重定向" /><el-option value="log" label="仅记录" />
            </el-select>
            <el-input v-if="w.action === 'redirect'" v-model="w.actionValue" placeholder="https://..." style="width:180px" size="small" />
            <el-input-number v-model="w.priority" :min="1" :max="999" size="small" style="width:100px" />
            <el-switch v-model="w.enabled" inline-prompt active-text="启" inactive-text="停" />
            <el-button type="danger" circle size="small" @click="form.security.wafRules.splice(wi,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <div v-for="(c, ci) in w.conditions" :key="ci" class="waf-cond">
            <el-select v-model="c.field" filterable allow-create style="width:150px" size="small">
              <el-option v-for="f in wafFields" :key="f.v" :value="f.v" :label="f.l" />
            </el-select>
            <el-select v-model="c.operator" style="width:130px" size="small">
              <el-option v-for="op in wafOps" :key="op" :value="op" :label="op" />
            </el-select>
            <el-input v-model="c.value" placeholder="值" style="width:170px" size="small" />
            <el-checkbox v-model="c.negate" label="取反" size="small" />
            <el-button type="danger" circle size="small" @click="w.conditions.splice(ci,1)"><el-icon><Delete /></el-icon></el-button>
          </div>
          <el-button size="small" text type="primary" @click="w.conditions.push({ field: 'uri', operator: 'contains', value: '', negate: false })">+ 条件（规则内 AND）</el-button>
        </div>
        <el-form-item label=" ">
          <el-button size="small" type="primary" plain @click="form.security.wafRules.push({ name: '', enabled: true, conditions: [{ field: 'uri', operator: 'contains', value: '', negate: false }], action: 'block', actionValue: '', priority: 100 })">+ 添加 WAF 规则</el-button>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存并发布</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="purgeDialog" title="缓存刷新" width="520px">
      <el-form label-width="100px">
        <el-form-item label="刷新类型">
          <el-radio-group v-model="purge.matchType">
            <el-radio value="url">精确 URL</el-radio>
            <el-radio value="directory">目录前缀</el-radio>
            <el-radio value="host">整站域名</el-radio>
            <el-radio value="all">全部缓存</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="purge.matchType !== 'all'" label="域名"><el-input v-model="purge.host" placeholder="test.example.com" /></el-form-item>
        <el-form-item v-if="['url','directory'].includes(purge.matchType)" label="路径">
          <el-input v-model="purge.value" :placeholder="purge.matchType === 'url' ? '/path/to/file.js' : '/static/'" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="purgeDialog = false">取消</el-button>
        <el-button type="primary" :loading="purging" @click="doPurge">提交刷新</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import api from '../api'

const servers = ref([])
const loading = ref(false)
const dialog = ref(false)
const saving = ref(false)
const form = ref(emptyForm())

const httpPortsStr = ref('80')
const httpsPortsStr = ref('')
const l4PortsStr = ref('')
const blacklistCidrs = computed({
  get: () => (form.value.security.ipBlacklist || []).map(b => b.cidr),
  set: (v) => { form.value.security.ipBlacklist = (v || []).map(c => ({ cidr: c })) },
})

const jsShieldExclude = computed({
  get: () => (form.value.security.jsShield?.excludePaths || []).join(','),
  set: (v) => { form.value.security.jsShield.excludePaths = (v || '').split(',').map(x => x.trim()).filter(Boolean) },
})

function emptyForm() {
  return {
    clusterId: 1, userId: 1, domains: [], serverType: 'http',
    httpPorts: [80], httpsPorts: [], l4Ports: [], forceHttps: false, enabled: true,
    origin: { primaries: [{ scheme: 'http', host: '', port: 80, weight: 1, enabled: true }], backups: [], scheduleAlgorithm: 'round-robin', hostOverride: '', pathRules: [] },
    cache: { enabled: false, rules: [] },
    security: { ipWhitelist: [], ipBlacklist: [], cc: { enabled: false, threshold: 300, windowSeconds: 60, banSeconds: 60, excludePaths: [] }, deniedCountries: [], deniedProvinces: [], uaRules: [], wafRules: [], urlAuth: { enabled: false, type: 'a', key: '', expirySeconds: 1800 }, jsShield: { enabled: false, mode: 'js', secret: '', cookieSeconds: 3600, maxFails: 5, banSeconds: 600, excludePaths: [] }, edgeScripts: [], aiGuard: { enabled: false, apiUrl: '', apiKey: '', model: 'gpt-4o-mini', sampleRate: 10, blockThreshold: 0.8 } },
  }
}
function addOrigin(key) { form.value.origin[key].push({ scheme: form.value.serverType === 'http' ? 'http' : 'tcp', host: '', port: 80, weight: 1, enabled: true }) }
const parsePorts = (s) => (s || '').split(',').map(x => parseInt(x.trim())).filter(n => n > 0)

const wafFields = [
  { v: 'uri', l: 'URI 路径' }, { v: 'uri-full', l: 'URI 含参数' }, { v: 'ip', l: '客户端 IP' },
  { v: 'user_agent', l: 'User-Agent' }, { v: 'referer', l: 'Referer' }, { v: 'method', l: '请求方法' },
  { v: 'host', l: '访问域名' }, { v: 'body_length', l: 'Body 大小' },
  { v: 'header_', l: '自定义 Header（header_名称）' }, { v: 'query_', l: 'URL 参数（query_名称）' },
]
const wafOps = ['eq','ne','contains','not-contains','prefix','suffix','regex','in','not-in','gt','lt','gte','lte','exists','not-exists','in-cidr','not-in-cidr','empty','not-empty']

async function load() {
  loading.value = true
  try { servers.value = await api.get('/admin/servers') } finally { loading.value = false }
}
onMounted(load)

function openEdit(row) {
  if (row) {
    form.value = JSON.parse(JSON.stringify(row))
    if (!form.value.origin) form.value.origin = emptyForm().origin
    if (!form.value.cache) form.value.cache = emptyForm().cache
    if (!form.value.security) form.value.security = emptyForm().security
    if (!form.value.security.uaRules) form.value.security.uaRules = []
    if (!form.value.security.wafRules) form.value.security.wafRules = []
    if (!form.value.security.urlAuth) form.value.security.urlAuth = emptyForm().security.urlAuth
    if (!form.value.security.jsShield) form.value.security.jsShield = emptyForm().security.jsShield
    if (!form.value.origin.pathRules) form.value.origin.pathRules = []
    if (!form.value.security.edgeScripts) form.value.security.edgeScripts = []
    if (!form.value.security.aiGuard) form.value.security.aiGuard = emptyForm().security.aiGuard
    httpPortsStr.value = (row.httpPorts || []).join(',')
    httpsPortsStr.value = (row.httpsPorts || []).join(',')
    l4PortsStr.value = (row.l4Ports || []).join(',')
  } else {
    form.value = emptyForm()
    httpPortsStr.value = '80'; httpsPortsStr.value = ''; l4PortsStr.value = ''
  }
  dialog.value = true
}

async function save() {
  const f = form.value
  if (!f.domains.length) return ElMessage.warning('至少绑定一个域名')
  if (!f.origin.primaries.length || !f.origin.primaries[0].host) return ElMessage.warning('请填写主源站')
  if (f.serverType === 'http') {
    f.httpPorts = parsePorts(httpPortsStr.value)
    f.httpsPorts = parsePorts(httpsPortsStr.value)
    f.l4Ports = []
    if (!f.httpPorts.length) return ElMessage.warning('请填写 HTTP 端口')
  } else {
    f.l4Ports = parsePorts(l4PortsStr.value)
    f.httpPorts = []; f.httpsPorts = []
    if (!f.l4Ports.length) return ElMessage.warning('请填写 TCP 端口')
  }
  saving.value = true
  try {
    if (f.id) await api.put(`/admin/servers/${f.id}`, f)
    else await api.post('/admin/servers', f)
    ElMessage.success('已保存并发布配置')
    dialog.value = false
    load()
  } catch (e) {
    ElMessage.error(e.response?.data || '保存失败')
  } finally { saving.value = false }
}

async function del(row) {
  await api.delete(`/admin/servers/${row.id}`)
  ElMessage.success('已删除')
  load()
}

const purgeDialog = ref(false)
const purging = ref(false)
const purge = ref({ matchType: 'url', host: '', value: '' })

async function doPurge() {
  const p = purge.value
  if (p.matchType !== 'all' && !p.host) return ElMessage.warning('请填写域名')
  purging.value = true
  try {
    await api.post('/admin/servers/cache/purge', p)
    ElMessage.success('刷新指令已下发到全部节点（≤10s 生效）')
    purgeDialog.value = false
  } catch (e) { ElMessage.error(e.response?.data || '刷新失败') } finally { purging.value = false }
}
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
.origin-row { display: flex; gap: 8px; margin-bottom: 8px; align-items: center; }
.waf-rule { border: 1px solid var(--el-border-color); border-radius: 6px; padding: 8px 12px; margin-bottom: 10px; }
.waf-head { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
.waf-cond { display: flex; gap: 8px; align-items: center; margin-bottom: 6px; }
</style>
