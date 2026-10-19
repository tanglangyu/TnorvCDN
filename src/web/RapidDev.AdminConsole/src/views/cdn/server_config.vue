<script setup lang="ts">
/** 站点分层配置页 —— HTTPS/SSL、缓存、安全防护独立于网站编辑，分 Tab 分层管理。
 *  每个区块独立保存（PUT /admin/servers/{id}/ssl|cache|security），互不影响。
 *  支持一键应用配置模板（覆盖 HTTPS/缓存/安全配置块）。 */
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'

const route = useRoute()
const router = useRouter()
const serverId = Number(route.params.id)
const server = ref<any>(null)
const loading = ref(true)
const activeTab = ref('ssl')

const sslForm = ref({ sslMode: 'auto', forceHttps: false, httpsPorts: '443' })
const cacheForm = ref<any>({ enabled: false, rules: [] })
const secForm = ref<any>({
  cc: { enabled: false, threshold: 300, windowSeconds: 60, banSeconds: 60, excludePaths: [] },
  jsShield: { enabled: false, mode: 'js', cookieSeconds: 3600, maxFails: 5, banSeconds: 600, excludePaths: [] },
  urlAuth: { enabled: false, type: 'a', key: '', expirySeconds: 1800 },
  ipWhitelist: [], ipBlacklist: [], uaRules: [], deniedCidrs: [], deniedCountries: [],
})

const savingAdv = ref(false)
const advForm = ref<any>({
  redirects: [], rewrites: [], headers: [],
  basicAuth: { enabled: false, users: [] },
  compression: true, webpOptimization: false, charset: '',
  requestLimitPerMinute: 0, maxBodySizeMB: 0, websocket: true,
})
function addRedirect() { advForm.value.redirects.push({ pattern: '/old/', target: '/new', code: 301, enabled: true }) }
function addRewrite() { advForm.value.rewrites.push({ pattern: '^/a/(.*)$', replacement: '/b/$1', enabled: true }) }
function addHeader(dir: string) { advForm.value.headers.push({ direction: dir, action: 'set', name: '', value: '', enabled: true }) }
function addBasicUser() { advForm.value.basicAuth.users.push({ username: '', plain: '' }) }
async function saveAdvanced() {
  savingAdv.value = true
  try {
    await cdnApi.put(`/admin/servers/${serverId}/advanced`, advForm.value)
    ElMessage.success('高级配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { savingAdv.value = false }
}

const savingSsl = ref(false)
const renewing = ref(false)
const sslStateLabel = (s: string) => ({ pending_dns: '未解析', pending_issuing: '签发中', issued: '已签发', failed: '签发失败' } as any)[s] || s
const sslStateTag = (s: string) => ({ pending_dns: 'warning', pending_issuing: 'primary', issued: 'success', failed: 'danger' } as any)[s] || 'info'
const sslRenewing = computed(() => server.value?.sslState === 'pending_issuing')

async function renewCert() {
  renewing.value = true
  try {
    await cdnApi.post(`/admin/servers/${serverId}/ssl/renew`)
    ElMessage.success('续期请求已下发，节点将重新签发证书')
    await load()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '发起续期失败') }
  finally { renewing.value = false }
}
const savingCache = ref(false)
const savingSec = ref(false)

// ---- 模板 ----
const templates = ref<any[]>([])
const tplPicked = ref<number | null>(null)
const applyingTpl = ref(false)

const load = async () => {
  loading.value = true
  try {
    const s = await cdnApi.get(`/admin/servers/${serverId}`)
    server.value = s
    sslForm.value = {
      sslMode: s.sslMode || 'auto',
      forceHttps: !!s.forceHttps,
      httpsPorts: (s.httpsPorts || []).join(','),
    }
    cacheForm.value = JSON.parse(JSON.stringify(s.cache || { enabled: false, rules: [] }))
    cacheForm.value.rules = cacheForm.value.rules || []
    const sec = JSON.parse(JSON.stringify(s.security || {}))
    sec.cc = sec.cc || { enabled: false, threshold: 300, windowSeconds: 60, banSeconds: 60, excludePaths: [] }
    sec.ipWhitelist = sec.ipWhitelist || []
    sec.ipBlacklist = sec.ipBlacklist || []
    sec.uaRules = sec.uaRules || []
    sec.deniedCidrs = sec.deniedCidrs || []
    sec.deniedCountries = sec.deniedCountries || []
    sec.jsShield = sec.jsShield || { enabled: false, mode: 'js', cookieSeconds: 3600, maxFails: 5, banSeconds: 600, excludePaths: [] }
    sec.jsShield.excludePaths = sec.jsShield.excludePaths || []
    sec.urlAuth = sec.urlAuth || { enabled: false, type: 'a', key: '', expirySeconds: 1800 }
    sec.cc.excludePaths = sec.cc.excludePaths || []
    secForm.value = sec
    const adv = s.advanced || {}
    advForm.value = {
      redirects: adv.redirects || [],
      rewrites: adv.rewrites || [],
      headers: adv.headers || [],
      basicAuth: { enabled: adv.basicAuth?.enabled || false, users: adv.basicAuth?.users || [] },
      compression: adv.compression !== false,
      webpOptimization: !!adv.webpOptimization,
      charset: adv.charset || '',
      requestLimitPerMinute: adv.requestLimitPerMinute || 0,
      maxBodySizeMB: adv.maxBodySizeMB || 0,
      websocket: adv.websocket !== false,
    }
    templates.value = await cdnApi.get('/admin/site-templates')
  } finally {
    loading.value = false
  }
}
onMounted(load)

// ---- HTTPS/SSL ----
async function saveSsl() {
  const ports = sslForm.value.httpsPorts.split(',').map((x: string) => parseInt(x.trim())).filter((n: number) => n > 0)
  savingSsl.value = true
  try {
    await cdnApi.put(`/admin/servers/${serverId}/ssl`, {
      sslMode: sslForm.value.sslMode,
      forceHttps: sslForm.value.forceHttps,
      httpsPorts: sslForm.value.sslMode === 'off' ? [] : (ports.length ? ports : [443]),
    })
    ElMessage.success('HTTPS/SSL 配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { savingSsl.value = false }
}

// ---- 缓存 ----
function addCacheRule() { cacheForm.value.rules.push({ matchType: 'extension', value: '', ttlSeconds: 3600, clientTtlSeconds: 0, ignoreQueryString: false }) }
function removeCacheRule(i: number) { cacheForm.value.rules.splice(i, 1) }
async function saveCache() {
  savingCache.value = true
  try {
    await cdnApi.put(`/admin/servers/${serverId}/cache`, cacheForm.value)
    ElMessage.success('缓存配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { savingCache.value = false }
}

// ---- 安全 ----
function parseIpRules(v: string) {
  return v.split(/\n|,/).map((x: string) => x.trim()).filter(Boolean).map((line: string) => {
    const m = line.match(/^(\S+)\s*#\s*(.*)$/)
    return m ? { cidr: m[1], note: m[2] } : { cidr: line, note: '' }
  })
}
const ipWhitelistText = computed({
  get: () => (secForm.value.ipWhitelist || []).map((r: any) => r.note ? `${r.cidr} # ${r.note}` : r.cidr).join('\n'),
  set: (v: string) => { secForm.value.ipWhitelist = parseIpRules(v) },
})
const ipBlacklistText = computed({
  get: () => (secForm.value.ipBlacklist || []).map((r: any) => r.note ? `${r.cidr} # ${r.note}` : r.cidr).join('\n'),
  set: (v: string) => { secForm.value.ipBlacklist = parseIpRules(v) },
})
const deniedCidrsText = computed({
  get: () => (secForm.value.deniedCidrs || []).join('\n'),
  set: (v: string) => { secForm.value.deniedCidrs = v.split(/\n|,/).map((x: string) => x.trim()).filter(Boolean) },
})
function addUaRule() { secForm.value.uaRules.push({ keyword: '', allow: false }) }
function removeUaRule(i: number) { secForm.value.uaRules.splice(i, 1) }
async function saveSecurity() {
  savingSec.value = true
  try {
    await cdnApi.put(`/admin/servers/${serverId}/security`, secForm.value)
    ElMessage.success('安全防护配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { savingSec.value = false }
}

// ---- 一键模板 ----
async function applyTpl() {
  if (!tplPicked.value) return ElMessage.warning('请选择模板')
  const t = templates.value.find((x: any) => x.id === tplPicked.value)
  await ElMessageBox.confirm(
    `将把模板「${t?.name}」（${t?.description || ''}）覆盖应用到本站点的 HTTPS/缓存/安全配置，继续？`,
    '应用配置模板', { type: 'warning' })
  applyingTpl.value = true
  try {
    const r = await cdnApi.post(`/admin/site-templates/${tplPicked.value}/apply`, { serverId })
    ElMessage.success(`已应用：${(r.applied || []).join('、') || '无变更'}`)
    await load()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '应用失败') }
  finally { applyingTpl.value = false }
}

const domainsLabel = computed(() => (server.value?.domains || []).join(', '))
</script>

<template>
  <div v-loading="loading">
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <el-button size="small" text @click="router.push('/cdn/servers')">← 返回网站列表</el-button>
          <h2 class="page-title d-inline-block ms-2">站点配置</h2>
          <div class="text-muted">{{ domainsLabel }}</div>
        </div>
        <div class="col-auto ms-auto d-flex gap-2 align-items-center">
          <el-select v-model="tplPicked" placeholder="一键配置模板" style="width:220px" size="small">
            <el-option v-for="t in templates" :key="t.id" :value="t.id" :label="t.name + (t.builtIn ? '（内置）' : '')">
              <div><span>{{ t.name }}</span><span class="text-muted small ms-2">{{ t.description }}</span></div>
            </el-option>
          </el-select>
          <el-button type="success" size="small" :loading="applyingTpl" @click="applyTpl">一键应用</el-button>
        </div>
      </div>
    </div>

    <el-card>
      <el-tabs v-model="activeTab">
        <!-- ============ HTTPS / SSL ============ -->
        <el-tab-pane label="HTTPS / SSL 证书" name="ssl">
          <el-form label-width="130px" style="max-width:680px" class="mt-3">
            <el-form-item label="证书模式">
              <el-radio-group v-model="sslForm.sslMode">
                <el-radio-button value="auto">自动签发（ACME/Let's Encrypt）</el-radio-button>
                <el-radio-button value="manual">手动上传</el-radio-button>
                <el-radio-button value="off">关闭 HTTPS</el-radio-button>
              </el-radio-group>
              <div class="text-muted small mt-1" v-if="sslForm.sslMode === 'auto'">
                边缘节点自动向 Let's Encrypt 申请并续期证书（HTTP-01 验证，需域名已解析到节点）。切换后约 10 秒内节点生效。
              </div>
              <div class="text-muted small mt-1" v-else-if="sslForm.sslMode === 'manual'">
                请先在「证书管理」页上传覆盖该域名的证书（PEM），节点按 SNI 匹配下发。
              </div>
            </el-form-item>
            <el-form-item v-if="sslForm.sslMode !== 'off'" label="HTTPS端口">
              <el-input v-model="sslForm.httpsPorts" placeholder="443" style="width:220px" />
              <span class="ms-2 text-muted small">多个用英文逗号分隔</span>
            </el-form-item>
            <el-form-item label="强制HTTPS">
              <el-switch v-model="sslForm.forceHttps" :disabled="sslForm.sslMode === 'off'" />
              <span class="ms-2 text-muted small">HTTP 访问 301 跳转到 HTTPS</span>
            </el-form-item>
            <el-form-item v-if="sslForm.sslMode === 'auto'" label="证书状态">
              <el-tag v-if="server?.sslState" size="small" :type="sslStateTag(server.sslState)">{{ sslStateLabel(server.sslState) }}</el-tag>
              <span v-else class="text-muted small">待节点首次尝试签发</span>
              <span v-if="server?.sslExpiresAt" class="ms-2 text-muted small">到期：{{ new Date(server.sslExpiresAt).toLocaleDateString() }}</span>
              <span v-if="server?.sslLastError && server.sslState !== 'issued'" class="ms-2 text-danger small">{{ server.sslLastError }}</span>
            </el-form-item>
            <el-form-item v-if="sslForm.sslMode === 'auto'" label="证书续期">
              <el-tooltip content="证书正常自动续期；此按钮用于强制立即重新签发（更新中不可重复点击，超时失败后自动解锁）">
                <el-button type="warning" plain :loading="renewing || sslRenewing" :disabled="sslRenewing" @click="renewCert">
                  {{ sslRenewing ? '证书更新中…' : '立即更新证书' }}
                </el-button>
              </el-tooltip>
              <span class="ms-2 text-muted small">自动续期由节点在到期前完成，通常无需手动操作</span>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="savingSsl" @click="saveSsl">保存 HTTPS 配置</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <!-- ============ 缓存 ============ -->
        <el-tab-pane label="缓存规则" name="cache">
          <el-form label-width="130px" style="max-width:860px" class="mt-3">
            <el-form-item label="页面缓存">
              <el-switch v-model="cacheForm.enabled" />
              <span class="ms-2 text-muted small">开启后命中规则的静态内容在边缘缓存加速</span>
            </el-form-item>
            <el-form-item v-if="cacheForm.enabled" label="缓存规则">
              <div style="width:100%">
                <div v-for="(r, i) in cacheForm.rules" :key="i" class="d-flex gap-1 mb-1">
                  <el-select v-model="r.matchType" style="width:130px" size="small">
                    <el-option value="extension" label="扩展名" /><el-option value="path-prefix" label="路径前缀" />
                    <el-option value="path-exact" label="精确路径" /><el-option value="homepage" label="首页" /><el-option value="all" label="全部" />
                  </el-select>
                  <el-input v-model="r.value" size="small" placeholder="png,jpg 或 /static/" style="width:200px" />
                  <span class="small" style="align-self:center">缓存</span>
                  <el-input-number v-model="r.ttlSeconds" size="small" :min="1" controls-position="right" style="width:130px" />
                  <span class="small" style="align-self:center">秒</span>
                  <el-button size="small" type="danger" text @click="removeCacheRule(i)">删</el-button>
                </div>
                <el-button size="small" @click="addCacheRule">+ 添加规则</el-button>
                <div class="text-muted small mt-1">未命中任何规则默认回源不缓存</div>
              </div>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="savingCache" @click="saveCache">保存缓存配置</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <!-- ============ 安全防护 ============ -->
        <el-tab-pane label="安全防护" name="security">
          <el-form label-width="130px" style="max-width:860px" class="mt-3">
            <el-divider content-position="left">IP 名单</el-divider>
            <el-form-item label="IP 白名单">
              <el-input :model-value="ipWhitelistText" type="textarea" :rows="2"
                placeholder="每行一个 IP 或 CIDR，可加备注：1.2.3.4 # 办公网"
                @update:model-value="(v: string) => ipWhitelistText = v" />
              <div class="text-muted small mt-1" style="width:100%">白名单 IP 跳过全部安全检查（CC、WAF、5秒盾等）</div>
            </el-form-item>
            <el-form-item label="IP 黑名单">
              <el-input :model-value="ipBlacklistText" type="textarea" :rows="2"
                placeholder="每行一个 IP 或 CIDR，可加备注：5.6.7.0/24 # 攻击源"
                @update:model-value="(v: string) => ipBlacklistText = v" />
            </el-form-item>
            <el-form-item label="区域封禁">
              <el-input :model-value="deniedCidrsText" type="textarea" :rows="3"
                placeholder="每行一个 CIDR，如 1.0.1.0/24（可粘贴公开的国家/地区 CIDR 列表）"
                @update:model-value="(v: string) => deniedCidrsText = v" />
            </el-form-item>

            <el-divider content-position="left">UA 黑白名单</el-divider>
            <el-form-item label="UA 规则">
              <div style="width:100%">
                <div v-for="(r, i) in secForm.uaRules || []" :key="i" class="d-flex gap-1 mb-1">
                  <el-radio-group v-model="r.allow" size="small">
                    <el-radio-button :value="true">允许</el-radio-button>
                    <el-radio-button :value="false">拒绝</el-radio-button>
                  </el-radio-group>
                  <el-input v-model="r.keyword" size="small" placeholder="关键字，支持 * 通配，如 curl* / *bot*" style="width:260px" />
                  <el-button size="small" type="danger" text @click="removeUaRule(i)">删</el-button>
                </div>
                <el-button size="small" @click="addUaRule">+ 添加 UA 规则</el-button>
                <span class="text-muted small ms-2">先匹配先生效，可拦截恶意爬虫/采集器</span>
              </div>
            </el-form-item>

            <el-divider content-position="left">CC 限流</el-divider>
            <el-form-item label="CC限流">
              <el-switch v-model="secForm.cc.enabled" />
              <span class="ms-2 text-muted small">阈值</span>
              <el-input-number v-model="secForm.cc.threshold" :min="10" size="small" class="ms-1" style="width:110px" />
              <span class="ms-1 text-muted small">req/{{ secForm.cc.windowSeconds }}s，超限封禁 {{ secForm.cc.banSeconds }}s</span>
            </el-form-item>
            <el-form-item v-if="secForm.cc.enabled" label="例外路径">
              <el-input :model-value="(secForm.cc.excludePaths || []).join('\n')" type="textarea" :rows="2"
                placeholder="每行一个 URL 前缀，如 /api/webhook（这些路径不计入 CC 限流）"
                @update:model-value="(v: string) => secForm.cc.excludePaths = v.split(/\n|,/).map((x: string) => x.trim()).filter(Boolean)" />
            </el-form-item>

            <el-divider content-position="left">5秒盾（浏览器验证）</el-divider>
            <el-form-item label="5秒盾">
              <div style="width:100%">
                <div class="d-flex align-items-center gap-2">
                  <el-switch v-model="secForm.jsShield.enabled" />
                  <el-radio-group v-if="secForm.jsShield.enabled" v-model="secForm.jsShield.mode" size="small">
                    <el-radio-button value="js">JS 挑战</el-radio-button>
                    <el-radio-button value="meta">Meta 刷新</el-radio-button>
                  </el-radio-group>
                </div>
                <div v-if="secForm.jsShield.enabled" class="mt-2 text-muted small">
                  首次访问返回浏览器验证页，通过后种 Cookie 放行；可拦截 CC 攻击与恶意脚本流量。Cookie 有效
                  <el-input-number v-model="secForm.jsShield.cookieSeconds" size="small" :min="60" controls-position="right" style="width:110px" /> 秒，
                  连续失败 <el-input-number v-model="secForm.jsShield.maxFails" size="small" :min="1" controls-position="right" style="width:90px" /> 次封禁
                  <el-input-number v-model="secForm.jsShield.banSeconds" size="small" :min="60" controls-position="right" style="width:110px" /> 秒。
                </div>
                <el-input v-if="secForm.jsShield.enabled" class="mt-2" :model-value="(secForm.jsShield.excludePaths || []).join('\n')"
                  type="textarea" :rows="2" placeholder="例外路径（每行一个，如 /api/callback 不做验证）"
                  @update:model-value="(v: string) => secForm.jsShield.excludePaths = v.split(/\n|,/).map((x: string) => x.trim()).filter(Boolean)" />
              </div>
            </el-form-item>

            <el-divider content-position="left">URL 防盗链</el-divider>
            <el-form-item label="URL防盗链">
              <div style="width:100%">
                <div class="d-flex align-items-center gap-2">
                  <el-switch v-model="secForm.urlAuth.enabled" />
                  <el-select v-if="secForm.urlAuth.enabled" v-model="secForm.urlAuth.type" size="small" style="width:220px">
                    <el-option value="a" label="A 型：md5(key + uri + ts)" />
                    <el-option value="b" label="B 型：md5(key + ts + uri)" />
                    <el-option value="c" label="C 型：md5(uri + key + ts)" />
                    <el-option value="d" label="D 型：md5(ts + uri + key)" />
                  </el-select>
                </div>
                <div v-if="secForm.urlAuth.enabled" class="mt-2 d-flex align-items-center gap-2">
                  <el-input v-model="secForm.urlAuth.key" size="small" placeholder="签名密钥" style="width:240px" show-password />
                  <span class="text-muted small">有效期</span>
                  <el-input-number v-model="secForm.urlAuth.expirySeconds" size="small" :min="60" controls-position="right" style="width:120px" /> 秒
                </div>
                <div v-if="secForm.urlAuth.enabled" class="text-muted small mt-1">
                  开启后所有资源 URL 需带时间戳签名参数，盗链与篡改链接将返回 403
                </div>
              </div>
            </el-form-item>

            <el-form-item>
              <el-button type="primary" :loading="savingSec" @click="saveSecurity">保存安全防护配置</el-button>
            </el-form-item>
          </el-form>

        <!-- ============ 高级设置 ============ -->
        <el-tab-pane label="高级设置" name="advanced">
          <el-form label-width="130px" style="max-width:900px" class="mt-3">
            <el-divider content-position="left">URL 跳转（301/302）</el-divider>
            <div v-for="(r, i) in advForm.redirects" :key="'rd'+i" class="d-flex gap-1 mb-1">
              <el-input v-model="r.pattern" size="small" placeholder="匹配路径（正则/前缀）如 ^/old/(.*)$" style="width:240px" />
              <el-input v-model="r.target" size="small" placeholder="目标，如 /new/$1" style="width:220px" />
              <el-select v-model="r.code" size="small" style="width:90px"><el-option :value="301" label="301" /><el-option :value="302" label="302" /></el-select>
              <el-switch v-model="r.enabled" />
              <el-button size="small" type="danger" text @click="advForm.redirects.splice(i,1)">删</el-button>
            </div>
            <el-button size="small" @click="addRedirect">+ 添加跳转规则</el-button>

            <el-divider content-position="left">重写规则（转发前改写路径）</el-divider>
            <div v-for="(r, i) in advForm.rewrites" :key="'rw'+i" class="d-flex gap-1 mb-1">
              <el-input v-model="r.pattern" size="small" placeholder="正则，如 ^/a/(.*)$" style="width:240px" />
              <el-input v-model="r.replacement" size="small" placeholder="替换为，如 /b/$1" style="width:240px" />
              <el-switch v-model="r.enabled" />
              <el-button size="small" type="danger" text @click="advForm.rewrites.splice(i,1)">删</el-button>
            </div>
            <el-button size="small" @click="addRewrite">+ 添加重写规则</el-button>

            <el-divider content-position="left">HTTP 报头（增/删/改）</el-divider>
            <div v-for="(h, i) in advForm.headers" :key="'hd'+i" class="d-flex gap-1 mb-1">
              <el-select v-model="h.direction" size="small" style="width:100px"><el-option value="request" label="请求" /><el-option value="response" label="响应" /></el-select>
              <el-select v-model="h.action" size="small" style="width:90px"><el-option value="set" label="设置" /><el-option value="add" label="追加" /><el-option value="del" label="删除" /></el-select>
              <el-input v-model="h.name" size="small" placeholder="Header 名" style="width:160px" />
              <el-input v-model="h.value" size="small" placeholder="值（删除时留空）" style="width:180px" />
              <el-switch v-model="h.enabled" />
              <el-button size="small" type="danger" text @click="advForm.headers.splice(i,1)">删</el-button>
            </div>
            <el-button size="small" @click="addHeader('response')">+ 响应报头</el-button>
            <el-button size="small" @click="addHeader('request')">+ 请求报头</el-button>

            <el-divider content-position="left">访问鉴权（Basic Auth）</el-divider>
            <el-form-item label="启用鉴权"><el-switch v-model="advForm.basicAuth.enabled" /></el-form-item>
            <div v-if="advForm.basicAuth.enabled">
              <div v-for="(u, i) in advForm.basicAuth.users" :key="'bu'+i" class="d-flex gap-1 mb-1">
                <el-input v-model="u.username" size="small" placeholder="用户名" style="width:160px" />
                <el-input v-model="u.plain" size="small" placeholder="密码（留空=不修改）" style="width:200px" />
                <el-button size="small" type="danger" text @click="advForm.basicAuth.users.splice(i,1)">删</el-button>
              </div>
              <el-button size="small" @click="addBasicUser">+ 添加用户</el-button>
            </div>

            <el-divider content-position="left">其它</el-divider>
            <el-form-item label="内容压缩"><el-switch v-model="advForm.compression" /><span class="ms-2 text-muted small">Gzip/Brotli 自适应压缩</span></el-form-item>
            <el-form-item label="WebP 自适应"><el-switch v-model="advForm.webpOptimization" /><span class="ms-2 text-muted small">支持 webp 的浏览器自动请求 .webp（404 自动回退原图）</span></el-form-item>
            <el-form-item label="字符编码"><el-input v-model="advForm.charset" placeholder="如 utf-8（留空=跟随源站）" style="width:200px" /></el-form-item>
            <el-form-item label="请求限制">
              <el-input-number v-model="advForm.requestLimitPerMinute" :min="0" controls-position="right" style="width:160px" />
              <span class="ms-2 text-muted small">次/分钟/IP（0=不限）</span>
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="savingAdv" @click="saveAdvanced">保存高级配置</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>
