<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { configApi } from '@/api/system'
import { mqttApi } from '@/api/monitor'
import { TCard, TButton, TInput, TSwitch, TSelect, TTextarea, TModal } from '@/components/t'
import type { SysConfig } from '@/types'

type FieldType = 'input' | 'number' | 'switch' | 'select' | 'textarea' | 'password'

interface FieldDef {
  key: string
  label: string
  type: FieldType
  options?: { label: string; value: any }[]
  help?: string
  unit?: string
  span?: number
  placeholder?: string
}

interface GroupDef {
  id: string
  title: string
  icon: string
  fields: FieldDef[]
}

// 10 个分组,覆盖全部 sys_config
const groups: GroupDef[] = [
  {
    id: 'site', title: '站点信息', icon: 'building-arch',
    fields: [
      { key: 'sys.site.name', label: '系统名称', type: 'input', span: 6, help: '登录页展示的系统名称' },
      { key: 'sys.site.subtitle', label: '系统副标题', type: 'input', span: 6, help: '登录页副标题/简介' },
      { key: 'sys.site.icp', label: 'ICP 备案号', type: 'input', span: 6, help: '如 京ICP备XXXXXXXX号-X,留空不显示' },
      { key: 'sys.site.psb', label: '公安备案号', type: 'input', span: 6, help: '如 京公网安备 XXXXXXXXXXXX号,留空不显示' },
      { key: 'sys.site.copyright', label: '版权信息', type: 'input', span: 12, help: '页脚版权文字' },
    ],
  },
  {
    id: 'skin', title: '界面皮肤', icon: 'color-swatch',
    fields: [
      {
        key: 'sys.index.skinTheme', label: '默认皮肤', type: 'select', span: 6,
        options: [
          { label: '蓝色 skin-blue', value: 'skin-blue' },
          { label: '绿色 skin-green', value: 'skin-green' },
          { label: '紫色 skin-purple', value: 'skin-purple' },
        ],
      },
    ],
  },
  {
    id: 'crypto', title: '加密网关', icon: 'lock-password',
    fields: [
      { key: 'Crypto.Enabled', label: '总开关', type: 'switch', span: 4 },
      { key: 'Crypto.RequireNonce', label: '强制 nonce 防重放', type: 'switch', span: 4, help: '关闭后不校验重放' },
      { key: 'Crypto.Aes.SessionTtlSec', label: 'AES 会话有效期', type: 'number', span: 4, unit: '秒', help: '默认 1800(30 分钟)' },
      { key: 'Crypto.Aes.EarlyRefreshSec', label: 'AES 提前刷新阈值', type: 'number', span: 4, unit: '秒', help: '剩余 <= 值时提示客户端刷新' },
      { key: 'Crypto.NonceTtlSec', label: 'Nonce 有效期', type: 'number', span: 4, unit: '秒' },
      { key: 'Crypto.ClockSkewSec', label: '时钟偏差容差', type: 'number', span: 4, unit: '秒' },
      { key: 'Crypto.MaxBodyBytes', label: '加密请求最大 body', type: 'number', span: 4, unit: '字节', help: '默认 1MB' },
    ],
  },
  {
    id: 'gateway', title: 'API 网关聚合(BFF)', icon: 'topology',
    fields: [
      { key: 'gateway.loopback.baseUrl', label: '回环 BaseUrl', type: 'input', span: 8, help: '聚合转发目标;strict=true 时强制 loopback(127.0.0.1/localhost/::1),改端口用' },
      { key: 'gateway.loopback.strict', label: '强制 loopback 校验', type: 'switch', span: 4, help: 'true=仅允许 loopback(SSRF 防护);false=允许聚合外部 API(自担风险)' },
    ],
  },
  {
    id: 'geetest', title: '人机验证 GeeTest', icon: 'shield-check',
    fields: [
      { key: 'GeeTest.Enabled', label: '是否启用', type: 'switch', span: 4 },
      { key: 'GeeTest.CaptchaId', label: 'CaptchaId', type: 'input', span: 8, help: '从 GeeTest 后台获取' },
      { key: 'GeeTest.CaptchaKey', label: 'CaptchaKey', type: 'password', span: 12, help: '机密,只在服务端使用' },
      { key: 'GeeTest.Scenes', label: '启用场景', type: 'input', span: 12, help: '逗号分隔:login/phone-login;* 表示全部。手机号登录需含 phone-login' },
    ],
  },
  {
    id: 'httplog', title: 'HTTP 访问日志', icon: 'http',
    fields: [
      { key: 'HttpLog.Enabled', label: '总开关', type: 'switch', span: 3 },
      { key: 'HttpLog.SkipInterceptedRequests', label: '跳过拦截请求', type: 'switch', span: 3, help: '429/400/405/415 不记录' },
      { key: 'HttpLog.SkipAnonymousErrors', label: '跳过匿名错误', type: 'switch', span: 3, help: '减少爬虫扫描器日志' },
      { key: 'HttpLog.MaxBodyBytes', label: '最大 body 字节', type: 'number', span: 3, unit: '字节', help: '超出截断' },
    ],
  },
  {
    id: 'file', title: '文件存储', icon: 'file',
    fields: [
      { key: 'FileStorage.RootPath', label: '根目录', type: 'input', span: 6, help: '服务端本地路径' },
      { key: 'FileStorage.MaxFileSizeMb', label: '单文件最大', type: 'number', span: 6, unit: 'MB' },
      { key: 'FileStorage.AllowedExtensions', label: '允许的扩展名', type: 'textarea', span: 12, help: '以逗号分隔,含点' },
      { key: 'FileStorage.SignatureSecret', label: '签名密钥(HMAC)', type: 'password', span: 12, help: '至少 32 字符' },
    ],
  },
  {
    id: 'sms', title: '短信服务(tnorv)', icon: 'mail',
    fields: [
      { key: 'Sms.Enabled', label: '总开关', type: 'switch', span: 4 },
      { key: 'Sms.LogEnabled', label: '记录日志', type: 'switch', span: 4, help: '记录到 sys_sms_log' },
      { key: 'Sms.TimeoutSec', label: 'HTTP 超时', type: 'number', span: 4, unit: '秒' },
      { key: 'Sms.ApiBaseUrl', label: 'API 地址', type: 'input', span: 12 },
      { key: 'Sms.AppKey', label: 'AppKey', type: 'password', span: 6, help: 'tnorv 后台获取' },
      { key: 'Sms.SecretKey', label: 'SecretKey', type: 'password', span: 6, help: '机密,仅服务端使用' },
      { key: 'Sms.SignId', label: '签名 ID', type: 'input', span: 12, help: '审核通过的签名 ID' },
    ],
  },
  {
    id: 'smscode', title: '短信验证码', icon: 'message-2',
    fields: [
      { key: 'SmsCode.Enabled', label: '总开关', type: 'switch', span: 3 },
      { key: 'SmsCode.DebugReturnCode', label: '开发环境返回明文', type: 'switch', span: 6, help: 'true 时接口返回验证码明文,生产务必 false' },
      { key: 'SmsCode.Length', label: '位数', type: 'number', span: 3, help: '4-8' },
      { key: 'SmsCode.TtlSec', label: '有效期', type: 'number', span: 4, unit: '秒' },
      { key: 'SmsCode.RatePerMin', label: '每分钟上限', type: 'number', span: 4, help: '按手机号' },
      { key: 'SmsCode.RatePerHour', label: '每小时上限', type: 'number', span: 4, help: '按手机号' },
      { key: 'SmsCode.VerifyFailMax', label: '失败次数上限', type: 'number', span: 4, help: '超限作废' },
      { key: 'SmsCode.Subport', label: '扩展端口', type: 'number', span: 4 },
      { key: 'SmsCode.TemplateId', label: '验证码模板 ID', type: 'input', span: 4, help: 'tnorv 验证码模板' },
    ],
  },
  {
    id: 'auth', title: '登录会话', icon: 'login',
    fields: [
      { key: 'Session.MaxDevices', label: '最大登录设备数', type: 'number', span: 6, unit: '个', help: '单用户同时在线设备上限,超过自动踢出最老会话;0=不限' },
      { key: 'Auth.PhoneLoginEnabled', label: '手机号登录', type: 'switch', span: 4, help: '启用后登录页可用手机号+短信验证码登录;未注册手机号自动注册为普通用户' },
      { key: 'Auth.IdCardVerifyPageEnabled', label: '实名认证页面', type: 'switch', span: 4, help: '关闭后用户详情页身份证验证区块隐藏(实名状态仍可在后台校验)' },
      { key: 'IdCardAuth.VerifiedCacheTtlDays', label: '实名缓存 TTL', type: 'number', span: 4, unit: '天', help: '实名状态 Redis 缓存天数,高并发实名校验走缓存;换绑/重置时主动失效' },
    ],
  },
  {
    id: 'idcardauth', title: '身份证二要素(创蓝)', icon: 'id-badge',
    fields: [
      { key: 'IdCardAuth.Enabled', label: '总开关', type: 'switch', span: 4 },
      { key: 'IdCardAuth.AppId', label: 'AppId', type: 'input', span: 6, help: '创蓝应用 appId' },
      { key: 'IdCardAuth.AppKey', label: 'AppKey', type: 'password', span: 6, help: '创蓝应用 appKey,机密仅服务端使用' },
      { key: 'IdCardAuth.ApiBaseUrl', label: 'API 地址', type: 'input', span: 8 },
      { key: 'IdCardAuth.TimeoutSec', label: 'HTTP 超时', type: 'number', span: 4, unit: '秒' },
      { key: 'IdCardAuth.LogEnabled', label: '记录日志', type: 'switch', span: 4, help: '记录到 sys_idcard_auth_log' },
    ],
  },
  {
    id: 'iplocation', title: 'IP 归属地服务', icon: 'world',
    fields: [
      { key: 'IpLocation.Enabled', label: '总开关', type: 'switch', span: 4 },
      { key: 'IpLocation.CacheTtlMin', label: '内存缓存 TTL', type: 'number', span: 4, unit: '分钟', help: '按 IP 命中缓存(非全量),默认 30' },
      { key: 'IpLocation.CacheMaxSize', label: '缓存上限', type: 'number', span: 4, unit: '条', help: 'LRU 淘汰,默认 50000' },
      { key: 'IpLocation.BilibiliEnabled', label: 'Bilibili 在线兜底', type: 'switch', span: 4, help: '仅独立工具页 includeOnline=true 时调用' },
      { key: 'IpLocation.BilibiliConcurrency', label: 'Bilibili 并发', type: 'number', span: 4, help: '限流,接口有并发限制' },
      { key: 'IpLocation.BilibiliTimeoutSec', label: 'Bilibili 超时', type: 'number', span: 4, unit: '秒' },
      { key: 'IpLocation.Ip2RegionDataPath', label: 'ip2region 路径', type: 'input', span: 6, help: '相对运行目录,默认 ipdata/ip2region.xdb' },
      { key: 'IpLocation.QqwryDataPath', label: '纯真库路径', type: 'input', span: 6, help: '相对运行目录,默认 ipdata/qqwry.dat' },
    ],
  },
  {
    id: 'backup', title: '数据库定时备份', icon: 'database-export',
    fields: [
      { key: 'backup.enabled', label: '启用定时备份', type: 'switch', span: 4 },
      { key: 'backup.retentionDays', label: '保留天数', type: 'number', span: 4, unit: '天', help: '超期自动删除,0=永久保留' },
      { key: 'backup.cron', label: 'Cron 表达式', type: 'input', span: 4, help: '默认每天凌晨 2 点' },
    ],
  },
  {
    id: 'mqtt', title: 'MQTT 消息总线', icon: 'broker',
    fields: [
      { key: 'Mqtt.Enabled', label: '总开关', type: 'switch', span: 3, help: 'false 时 broker 不启动,IMqttPublisher 发布 no-op;改后需重启' },
      { key: 'Mqtt.RequireClientId', label: '要求 ClientId', type: 'switch', span: 3, help: '空 ClientId 拒连(便于审计)' },
      { key: 'Mqtt.EnableTestPanel', label: '测试面板', type: 'switch', span: 3, help: '监控页测试发布/最近消息端点开关;生产可关' },
      { key: 'Mqtt.EnableTopicAcl', label: 'Topic ACL', type: 'switch', span: 3, help: '默认放行所有 topic;开启后按保留前缀禁令/全通配禁令拦截' },
      { key: 'Mqtt.EnableTls', label: '启用 TLS', type: 'switch', span: 3, help: '独立端口,需提供 PFX 证书;生产强烈建议开启;改后需重启' },
      { key: 'Mqtt.DenySubscribeWildcards', label: '禁订阅全通配', type: 'switch', span: 3, help: 'true=禁客户端订阅 # 防窃听;ACL 开启时生效' },
      { key: 'Mqtt.DefaultRetained', label: '默认 retained', type: 'switch', span: 3 },
      { key: 'Mqtt.EnableWebSocket', label: '启用 WebSocket', type: 'switch', span: 3, help: '预留(当前未接入)' },
      { key: 'Mqtt.TcpPort', label: 'TCP 端口', type: 'number', span: 4, unit: '端口', help: '独立于 Kestrel 5170;改后需重启' },
      { key: 'Mqtt.TlsPort', label: 'TLS 端口', type: 'number', span: 4, unit: '端口', help: '启用 TLS 时生效;改后需重启' },
      { key: 'Mqtt.MaxConnectionRatePerMin', label: '单IP连接速率', type: 'number', span: 4, unit: '/分', help: '超出拒连;0=不限' },
      { key: 'Mqtt.Username', label: 'TCP 用户名', type: 'input', span: 6, help: '设备/外部客户端连接账号;热刷新下次连接起生效' },
      { key: 'Mqtt.Password', label: 'TCP 密码', type: 'password', span: 6, help: '机密,仅服务端使用;生产务必改为强密码;热刷新下次连接起生效' },
      { key: 'Mqtt.DenyPrefixes', label: '保留前缀禁令', type: 'input', span: 12, help: '逗号分隔,如 system/,tenant/;对真实客户端禁发禁订,后端注入豁免;ACL 开启时生效' },
      { key: 'Mqtt.TlsCertPath', label: 'TLS 证书路径', type: 'input', span: 6, help: 'PFX 文件路径,相对运行目录或绝对;EnableTls=true 时必填' },
      { key: 'Mqtt.TlsCertPassword', label: 'TLS 证书密码', type: 'password', span: 6, help: 'PFX 密码,机密仅服务端使用' },
      { key: 'Mqtt.MaxMessageSizeBytes', label: '单消息最大字节', type: 'number', span: 4, unit: '字节', help: '超限拒绝并断开;默认 256KB' },
      { key: 'Mqtt.MaxPendingMessagesPerClient', label: '每客户端挂起上限', type: 'number', span: 4, help: '防慢消费者撑爆内存;改后需重启' },
      { key: 'Mqtt.ConnectionBacklog', label: '连接积压上限', type: 'number', span: 4, help: '防 SYN 洪泛;改后需重启' },
      { key: 'Mqtt.MaxConnectionsPerIp', label: '单IP连接上限', type: 'number', span: 4, help: '超出拒新连接;0=不限' },
      { key: 'Mqtt.MaxTotalConnections', label: '总连接上限', type: 'number', span: 4, help: '超出拒新连接;0=不限' },
      { key: 'Mqtt.WebSocketPath', label: 'WebSocket 路径', type: 'input', span: 4, help: '预留' },
    ],
  },
]

// 配置项原始行(含 id 等字段,保存时用)
const rawMap = ref<Map<string, SysConfig>>(new Map())
// 表单值:每个 key 的当前值(string,switch 也用 'true'/'false' 字符串存,渲染时转 boolean)
// 预初始化所有 key 为空串,确保 reactive 对动态 key 建立响应式追踪(否则首次渲染读 undefined 不收集依赖,后续赋值不触发更新)
const form = reactive<Record<string, string>>(Object.fromEntries(groups.flatMap(g => g.fields.map(f => [f.key, '']))))
// 未保存的 key
const dirty = ref<Set<string>>(new Set())
// 敏感项是否明文显示
const showSecret = ref<Set<string>>(new Set())
const loading = ref(false)
const saving = ref(false)
const msg = ref('')
const msgType = ref<'success' | 'danger'>('success')

const dirtyCount = computed(() => dirty.value.size)

function parseVal(v: string | undefined, field: FieldDef): any {
  if (field.type === 'switch') return v === 'true'
  if (field.type === 'number') return v === '' || v === undefined ? undefined : Number(v)
  return v ?? ''
}

function displayVal(v: string | undefined, field: FieldDef): any {
  if (field.type === 'switch') return v === 'true'
  return v ?? ''
}

function onChange(key: string) {
  dirty.value.add(key)
}

function toggleShow(key: string) {
  if (showSecret.value.has(key)) showSecret.value.delete(key)
  else showSecret.value.add(key)
}

async function load() {
  loading.value = true
  msg.value = ''
  try {
    const r: any = await configApi.list({ pageIndex: 1, pageSize: 500 })
    // request() 已取 .data,返回 { list, total, ... }
    const list: SysConfig[] = r?.list || r?.data?.list || []
    rawMap.value = new Map(list.map(c => [c.configKey, c]))
    const findByKey = (key: string) => list.find(c => c.configKey === key)
    for (const g of groups) for (const f of g.fields) {
      form[f.key] = findByKey(f.key)?.configValue ?? ''
    }
    dirty.value.clear()
  } catch (e: any) {
    msg.value = '加载失败: ' + (e?.message || '')
    msgType.value = 'danger'
  } finally { loading.value = false }
}

async function save() {
  if (dirty.value.size === 0) return
  saving.value = true
  msg.value = ''
  let ok = 0, fail = 0
  const failed: string[] = []
  try {
    await Promise.all(Array.from(dirty.value).map(async (key) => {
      const raw = rawMap.value.get(key)
      if (!raw) { fail++; failed.push(key); return }
      const payload: SysConfig = { ...raw, configValue: form[key] }
      try { await configApi.edit(raw.id, payload); ok++ }
      catch { fail++; failed.push(key) }
    }))
    dirty.value.clear()
    if (fail === 0) {
      msg.value = `保存成功,共 ${ok} 项`
      msgType.value = 'success'
    } else {
      msg.value = `保存完成:成功 ${ok} 项,失败 ${fail} 项(${failed.join(', ')})`
      msgType.value = 'danger'
      failed.forEach(k => dirty.value.add(k))
    }
  } finally { saving.value = false }
}

function reset() {
  load()
}

// ===== MQTT TLS 证书后台生成 =====
const certModal = ref(false)
const certLoading = ref(false)
const certForm = reactive({ commonName: '', sanDns: 'localhost', sanIp: '127.0.0.1', password: '', validityYears: 1 })
function openCertModal() {
  certForm.commonName = ''
  certForm.sanDns = 'localhost'
  certForm.sanIp = '127.0.0.1'
  certForm.password = ''
  certForm.validityYears = 1
  certModal.value = true
}
async function genCert() {
  if (!certForm.commonName) { msg.value = 'CommonName 不能为空'; msgType.value = 'danger'; return }
  if (!certForm.password) { msg.value = '证书密码不能为空'; msgType.value = 'danger'; return }
  certLoading.value = true
  msg.value = ''
  try {
    const r: any = await mqttApi.generateCert({
      commonName: certForm.commonName,
      sanDns: certForm.sanDns,
      sanIp: certForm.sanIp,
      password: certForm.password,
      validityYears: certForm.validityYears || 1,
    })
    // 回填证书路径/密码到表单并标记 dirty,用户保存后生效
    form['Mqtt.TlsCertPath'] = r?.pfxPath ?? ''
    form['Mqtt.TlsCertPassword'] = certForm.password
    dirty.value.add('Mqtt.TlsCertPath')
    dirty.value.add('Mqtt.TlsCertPassword')
    certModal.value = false
    msg.value = `证书已生成并回填(指纹 ${r?.thumbprint?.slice(0, 16)}…),请保存后开启 Mqtt.EnableTls,再到监控页重启 Broker 生效`
    msgType.value = 'success'
    // 刷新 rawMap 中的 Meta/路径实体 id,确保后续保存能命中(后端已 upsert,前端重新拉取)
    await load()
  } catch (e: any) {
    msg.value = '证书生成失败: ' + (e?.message || '')
    msgType.value = 'danger'
  } finally { certLoading.value = false }
}

function scrollTo(id: string) {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

onMounted(load)
</script>

<template>
  <div>
    <!-- 顶部标题 + 操作 -->
    <div class="d-flex flex-wrap align-items-center justify-content-between mb-3">
      <h3 class="page-title mb-0">
        <i class="ti ti-adjustments-horizontal me-2"></i>配置中心
      </h3>
      <div class="d-flex align-items-center gap-2">
        <span v-if="msg" class="small" :class="msgType === 'danger' ? 'text-danger' : 'text-success'">
          <i :class="msgType === 'danger' ? 'ti ti-alert-circle' : 'ti ti-check'" class="me-1"></i>{{ msg }}
        </span>
        <TButton variant="outline-secondary" size="sm" @click="reset" :disabled="loading">
          <i class="ti ti-refresh me-1"></i>重置
        </TButton>
        <TButton variant="primary" size="sm" @click="save" :disabled="saving || dirtyCount === 0">
          <i class="ti ti-device-floppy me-1"></i>{{ saving ? '保存中...' : `保存改动${dirtyCount > 0 ? `(${dirtyCount})` : ''}` }}
        </TButton>
      </div>
    </div>

    <!-- 锚点导航 -->
    <div class="mb-3 d-flex flex-wrap gap-1">
      <a v-for="g in groups" :key="g.id" :href="`#${g.id}`" class="btn btn-sm btn-ghost-secondary"
         @click.prevent="scrollTo(g.id)">
        <i :class="`ti ti-${g.icon} me-1`"></i>{{ g.title }}
      </a>
    </div>

    <div v-if="loading" class="text-center text-muted py-5">
      <i class="ti ti-loader ti-spin me-1"></i>加载中...
    </div>

    <div v-else class="row g-3">
      <div v-for="g in groups" :key="g.id" class="col-12" :id="g.id">
        <TCard>
          <template #title>
            <span><i :class="`ti ti-${g.icon} me-2`"></i>{{ g.title }}</span>
          </template>
          <template v-if="g.id === 'mqtt'" #actions>
            <TButton size="sm" variant="outline-warning" @click="openCertModal"><i class="ti ti-certificate me-1"></i>生成 TLS 证书</TButton>
          </template>
          <div class="row g-3">
            <div v-for="f in g.fields" :key="f.key" :class="`col-md-${f.span || 6}`">
              <label class="form-label small text-muted d-flex align-items-center justify-content-between">
                <span>{{ f.label }}</span>
                <a v-if="f.type === 'password'" href="#" class="text-muted small" @click.prevent="toggleShow(f.key)">
                  <i :class="showSecret.has(f.key) ? 'ti ti-eye-off' : 'ti ti-eye'" class="me-1"></i>{{ showSecret.has(f.key) ? '隐藏' : '显示' }}
                </a>
              </label>

              <!-- switch -->
              <div v-if="f.type === 'switch'" class="form-check form-switch">
                <TSwitch :model-value="displayVal(form[f.key], f)" @update:model-value="(v: any) => { form[f.key] = String(v); onChange(f.key) }" />
              </div>
              <!-- select -->
              <TSelect v-else-if="f.type === 'select'" :model-value="form[f.key]" :options="f.options"
                       @update:model-value="(v: any) => { form[f.key] = String(v); onChange(f.key) }" />
              <!-- number -->
              <TInput v-else-if="f.type === 'number'" :model-value="form[f.key]" type="number"
                      @update:model-value="(v: any) => { form[f.key] = String(v ?? ''); onChange(f.key) }" />
              <!-- password -->
              <TInput v-else-if="f.type === 'password'" :model-value="form[f.key]"
                      :type="showSecret.has(f.key) ? 'text' : 'password'"
                      @update:model-value="(v: any) => { form[f.key] = String(v ?? ''); onChange(f.key) }" />
              <!-- textarea -->
              <TTextarea v-else-if="f.type === 'textarea'" :model-value="form[f.key]"
                         @update:model-value="(v: any) => { form[f.key] = String(v ?? ''); onChange(f.key) }" />
              <!-- input -->
              <TInput v-else :model-value="form[f.key]" :placeholder="f.placeholder"
                      @update:model-value="(v: any) => { form[f.key] = String(v ?? ''); onChange(f.key) }" />

              <div v-if="f.help" class="form-text small text-muted">
                {{ f.help }}<span v-if="f.unit" class="ms-1">({{ f.unit }})</span>
              </div>
            </div>
          </div>
        </TCard>
      </div>
    </div>

    <!-- 生成自签 TLS 证书(MQTT) -->
    <TModal v-model="certModal" title="生成自签 TLS 证书" size="lg">
      <div class="row g-3">
        <div class="col-md-6">
          <label class="form-label small text-muted">CommonName (CN)</label>
          <TInput v-model="certForm.commonName" placeholder="如 mqtt.example.com 或 localhost" />
        </div>
        <div class="col-md-3">
          <label class="form-label small text-muted">有效期</label>
          <TInput v-model.number="certForm.validityYears" type="number" />
          <div class="form-text small text-muted">年(1~20)</div>
        </div>
        <div class="col-md-3">
          <label class="form-label small text-muted">PFX 密码</label>
          <TInput v-model="certForm.password" type="password" placeholder="设置密码" />
        </div>
        <div class="col-md-6">
          <label class="form-label small text-muted">SAN DNS(逗号分隔)</label>
          <TInput v-model="certForm.sanDns" placeholder="如 localhost,mqtt.example.com" />
        </div>
        <div class="col-md-6">
          <label class="form-label small text-muted">SAN IP(逗号分隔)</label>
          <TInput v-model="certForm.sanIp" placeholder="如 127.0.0.1,192.168.1.10" />
        </div>
        <div class="col-12">
          <div class="alert alert-info small mb-0">
            <i class="ti ti-info-circle me-1"></i>
            生成 RSA2048 自签证书并落盘到 FileStorage.RootPath/mqtt/,自动回填证书路径/密码。保存后需到监控页点「重启 Broker」生效 TLS。
          </div>
        </div>
      </div>
      <template #footer>
        <TButton variant="outline-secondary" size="sm" @click="certModal = false">取消</TButton>
        <TButton variant="primary" size="sm" :loading="certLoading" @click="genCert"><i class="ti ti-certificate me-1"></i>生成</TButton>
      </template>
    </TModal>
  </div>
</template>
