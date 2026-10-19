<script setup lang="ts">
/**
 * 站点设置容器 —— 通用设计
 * 左侧分组菜单 + 右侧子页动态组件；每页独立保存，保存后刷新站点数据。
 * 路由锚点 /cdn/servers/config/:id?s=cache 直达子页。
 */
import { ref, onMounted, watch, computed, markRaw, type Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

import Basic from './basic.vue'
import Domains from './domains.vue'
import Dns from './dns.vue'
import Http from './http.vue'
import Https from './https.vue'
import Origin from './origin.vue'
import Cache from './cache.vue'
import Waf from './waf.vue'
import Uam from './uam.vue'
import Cc from './cc.vue'
import Referers from './referers.vue'
import UserAgent from './userAgent.vue'
import Access from './access.vue'
import Locations from './locations.vue'
import IpLists from './iplists.vue'
import Advanced from './advanced.vue'
import Scripts from './scripts.vue'
import Links from './links.vue'
import Common from './common.vue'

const route = useRoute()
const router = useRouter()
const serverId = Number(route.params.id)

interface MenuDef { key: string; title: string; group: string; comp: Component; props?: any }

const menus: MenuDef[] = [
  { key: 'basic', title: '基本信息', group: '基本设置', comp: Basic },
  { key: 'domain', title: '域名', group: '基本设置', comp: Domains },
  { key: 'dns', title: 'DNS', group: '基本设置', comp: Dns },
  { key: 'http', title: 'HTTP', group: '基本设置', comp: Http },
  { key: 'https', title: 'HTTPS', group: '基本设置', comp: Https },
  { key: 'origin', title: '源站', group: '基本设置', comp: Origin },
  { key: 'cache', title: '缓存', group: '缓存', comp: Cache },
  { key: 'waf', title: 'WAF', group: '安全防护', comp: Waf },
  { key: 'uam', title: '5秒盾', group: '安全防护', comp: Uam },
  { key: 'cc', title: 'CC防护', group: '安全防护', comp: Cc },
  { key: 'referers', title: '防盗链', group: '安全防护', comp: Referers },
  { key: 'userAgent', title: 'UA名单', group: '安全防护', comp: UserAgent },
  { key: 'access', title: '访问鉴权', group: '安全防护', comp: Access },
  { key: 'locations', title: '路由规则', group: '安全防护', comp: Locations },
  { key: 'ipLists', title: 'IP名单', group: '安全防护', comp: IpLists },
  { key: 'redirects', title: 'URL跳转', group: '高级功能', comp: Advanced, props: { field: 'redirects' } },
  { key: 'rewrite', title: '重写规则', group: '高级功能', comp: Advanced, props: { field: 'rewrites' } },
  { key: 'headers', title: 'HTTP报头', group: '高级功能', comp: Advanced, props: { field: 'headers' } },
  { key: 'compression', title: '内容压缩', group: '高级功能', comp: Advanced, props: { field: 'compression' } },
  { key: 'optimization', title: '页面优化', group: '高级功能', comp: Advanced, props: { field: 'optimization' } },
  { key: 'webp', title: 'WebP', group: '高级功能', comp: Advanced, props: { field: 'webp' } },
  { key: 'charset', title: '字符编码', group: '高级功能', comp: Advanced, props: { field: 'charset' } },
  { key: 'requestLimit', title: '请求限制', group: '高级功能', comp: Advanced, props: { field: 'requestLimit' } },
  { key: 'websocket', title: 'Websocket', group: '高级功能', comp: Advanced, props: { field: 'websocket' } },
  { key: 'grpc', title: 'gRPC', group: '高级功能', comp: Advanced, props: { field: 'grpc' } },
  { key: 'multimedia', title: '音视频设置', group: '高级功能', comp: Advanced, props: { field: 'multimedia' } },
  { key: 'remoteAddr', title: '访客IP地址', group: '高级功能', comp: Advanced, props: { field: 'remoteAddr' } },
  { key: 'pages', title: '自定义页面', group: '其他', comp: Advanced, props: { field: 'pages' } },
  { key: 'traffic', title: '流量限制', group: '其他', comp: Advanced, props: { field: 'traffic' } },
  { key: 'accessLog', title: '访问日志', group: '其他', comp: Links, props: { to: '/cdn/servers/logs', title: '访问日志' } },
  { key: 'stat', title: '统计', group: '其他', comp: Links, props: { to: '/cdn/servers/traffic-stats', title: '用量统计' } },
  { key: 'requestScripts', title: '边缘脚本', group: '其他', comp: Scripts },
  { key: 'common', title: '其他设置', group: '其他', comp: Common },
]

const groups = computed(() => [...new Set(menus.map((m) => m.group))])
const active = ref((route.query.s as string) || 'basic')
const current = computed(() => menus.find((m) => m.key === active.value) || menus[0])
const server = ref<any>(null)
const loading = ref(true)

const load = async () => {
  loading.value = true
  try {
    server.value = await cdnApi.get('/admin/servers/' + serverId + '/settings')
  } finally {
    loading.value = false
  }
}
onMounted(load)

watch(active, (k) => router.replace({ query: { ...route.query, s: k } }))

const reload = load

// ---- 复制配置通用设计----
const copyVisible = ref(false)
const copyLoading = ref(false)
const copyParts = ref<string[]>(['cache', 'security', 'advanced'])
const copyFromId = ref<number | null>(null)
const serverOptions = ref<{ id: number; label: string }[]>([])

const openCopy = async () => {
  copyVisible.value = true
  if (serverOptions.value.length === 0) {
    const list = await cdnApi.get('/admin/servers?page=1&pageSize=200')
    serverOptions.value = (list.items || [])
      .filter((s: any) => s.id !== serverId)
      .map((s: any) => ({ id: s.id, label: (s.domains && s.domains[0]) || ('站点#' + s.id) }))
  }
}

const partOptions = [
  { key: 'cache', label: '缓存设置' },
  { key: 'security', label: '安全防护（WAF/CC/防盗链等）' },
  { key: 'advanced', label: '高级功能（跳转/重写/报头/压缩等）' },
  { key: 'https', label: 'HTTPS（证书模式/强制跳转/端口）' },
  { key: 'origin', label: '源站' },
]

const doCopy = async () => {
  if (!copyFromId.value) { ElMessage.warning('请选择来源站点'); return }
  if (copyParts.value.length === 0) { ElMessage.warning('请选择要复制的设置'); return }
  copyLoading.value = true
  try {
    await cdnApi.post('/admin/servers/' + serverId + '/copy-from', { fromId: copyFromId.value, parts: copyParts.value })
    ElMessage.success('配置已复制并下发到节点')
    copyVisible.value = false
    await load()
  } finally {
    copyLoading.value = false
  }
}


// PUT /admin/servers/{id} 是整体替换语义：合并 patch 与当前配置后整体提交，避免清空其他块
const mergeSave = async (patch: any) => {
  const body = {
    domains: server.value.domains || [],
    serverType: server.value.serverType || 'http',
    l4Ports: server.value.l4Ports || [],
    httpPorts: server.value.httpPorts || [80],
    httpsPorts: server.value.httpsPorts || [],
    sslMode: server.value.sslMode || 'auto',
    forceHttps: !!server.value.forceHttps,
    enabled: server.value.enabled !== false,
    origin: server.value.origin || {},
    cache: server.value.cache || {},
    security: server.value.security || {},
    ...patch,
  }
  await cdnApi.put('/admin/servers/' + serverId, body)
  ElMessage.success('已保存并下发到节点')
  await load()
}
</script>

<template>
  <div v-loading="loading">
    <div class="page-header mb-3">
      <div class="row align-items-center">
        <div class="col-auto">
          <el-button size="small" text @click="router.push('/cdn/servers')">← 网站列表</el-button>
          <h2 class="page-title d-inline-block ms-2">站点设置</h2>
          <div class="text-muted">{{ (server?.domains || []).join(', ') }}</div>
        </div>
        <div class="col-auto ms-auto">
          <el-button size="small" @click="openCopy">复制配置</el-button>
        </div>
      </div>
    </div>
    <el-dialog align-center v-model="copyVisible" title="从其他站点复制配置" width="480px">
      <el-form label-width="90px">
        <el-form-item label="来源站点">
          <el-select v-model="copyFromId" filterable placeholder="选择要复制设置的站点" style="width: 100%">
            <el-option v-for="o in serverOptions" :key="o.id" :value="o.id" :label="o.label" />
          </el-select>
        </el-form-item>
        <el-form-item label="复制内容">
          <el-checkbox-group v-model="copyParts">
            <div v-for="p in partOptions" :key="p.key" class="py-1">
              <el-checkbox :value="p.key">{{ p.label }}</el-checkbox>
            </div>
          </el-checkbox-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="copyVisible = false">取消</el-button>
        <el-button type="primary" :loading="copyLoading" @click="doCopy">开始复制</el-button>
      </template>
    </el-dialog>
    <div class="row">
      <div class="col-auto" style="width: 200px">
        <el-card body-style="padding: 8px 4px">
          <div v-for="g in groups" :key="g">
            <div class="text-muted small px-2 pt-2 pb-1">{{ g }}</div>
            <div
              v-for="m in menus.filter((x) => x.group === g)" :key="m.key"
              class="px-3 py-2 rounded" style="cursor: pointer; font-size: 13.5px"
              :class="active === m.key ? 'bg-primary-lt text-primary font-weight-medium' : ''"
              @click="active = m.key"
            >
              {{ m.title }}
            </div>
          </div>
        </el-card>
      </div>
      <div class="col">
        <el-card v-if="server">
          <component
            :is="markRaw(current.comp)"
            :key="current.key"
            :server-id="serverId"
            :server="server"
            v-bind="current.props || {}"
            @saved="reload"
            @save="mergeSave"
          />
        </el-card>
      </div>
    </div>
  </div>
</template>
