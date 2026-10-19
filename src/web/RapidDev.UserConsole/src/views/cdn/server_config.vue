<script setup lang="ts">
/** 站点分层配置页（用户端）—— HTTPS/SSL、缓存、HTTP、源站 独立 Tab 管理，
 *  与管理端配置页体验一致。用户 PUT 接口仅允许源站/sslMode/forceHttps/cache。 */
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const route = useRoute()
const router = useRouter()
const serverId = Number(route.params.id)
const gate = ref()
const server = ref<any>(null)
const loading = ref(true)
const activeTab = ref('https')

const httpsForm = ref({ sslMode: 'auto', forceHttps: false })
const cacheForm = ref<any>({ enabled: false, rules: [] })
const originForm = ref({ originScheme: 'http', originHost: '', originPort: 80 })

const load = async () => {
  loading.value = true
  try {
    const r = await cdnApi.get('/user/servers') as any
    const list = r?.data || (Array.isArray(r) ? r : [])
    const s = list.find((x: any) => x.id === serverId)
    if (!s) { ElMessage.error('站点不存在'); return }
    server.value = s
    httpsForm.value = { sslMode: s.sslMode || 'auto', forceHttps: !!s.forceHttps }
    cacheForm.value = { enabled: !!(s.cache?.enabled ?? false), rules: [] }
    originForm.value = {
      originScheme: s.originFull?.[0]?.scheme || 'http',
      originHost: s.originFull?.[0]?.host || '',
      originPort: s.originFull?.[0]?.port || 80,
    }
  } finally { loading.value = false }
}
onMounted(load)

const savingHttps = ref(false)
async function saveHttps() {
  savingHttps.value = true
  try {
    await cdnApi.put(`/user/servers/${serverId}`, {
      sslMode: httpsForm.value.sslMode,
      forceHttps: httpsForm.value.forceHttps,
    })
    ElMessage.success('HTTPS 配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || e?.message || '保存失败') }
  finally { savingHttps.value = false }
}

const savingCache = ref(false)
async function saveCache() {
  savingCache.value = true
  try {
    await cdnApi.put(`/user/servers/${serverId}`, { cacheEnabled: cacheForm.value.enabled })
    ElMessage.success('缓存配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || e?.message || '保存失败') }
  finally { savingCache.value = false }
}

const savingOrigin = ref(false)
async function saveOrigin() {
  if (!originForm.value.originHost) return ElMessage.warning('请填写源站地址')
  savingOrigin.value = true
  try {
    await cdnApi.put(`/user/servers/${serverId}`, {
      originHost: originForm.value.originHost,
      originPort: Number(originForm.value.originPort) || 80,
      originScheme: originForm.value.originScheme,
    })
    ElMessage.success('源站配置已保存并下发到节点')
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || e?.message || '保存失败') }
  finally { savingOrigin.value = false }
}

const domainsLabel = computed(() => (server.value?.domains || []).join(', '))
const sslLabel = (m: string) => ({ auto: '自动签发', manual: '手动证书', off: '关闭' } as any)[m] || m
</script>

<template>
  <div v-loading="loading">
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <el-button size="small" text @click="router.push('/servers')">← 返回我的网站</el-button>
          <h2 class="page-title d-inline-block ms-2">站点配置</h2>
          <div class="text-muted">{{ domainsLabel }}</div>
        </div>
      </div>
    </div>
    <el-card>
      <el-tabs v-model="activeTab">
        <el-tab-pane label="HTTPS / SSL" name="https">
          <el-form label-width="120px" style="max-width:640px" class="mt-3">
            <el-form-item label="证书模式">
              <el-radio-group v-model="httpsForm.sslMode">
                <el-radio-button value="auto">自动签发（免费）</el-radio-button>
                <el-radio-button value="manual">手动上传</el-radio-button>
                <el-radio-button value="off">关闭 HTTPS</el-radio-button>
              </el-radio-group>
              <div class="text-muted small mt-1" v-if="httpsForm.sslMode === 'auto'">系统自动申请并续期 Let's Encrypt 证书</div>
              <div class="text-muted small mt-1" v-else-if="httpsForm.sslMode === 'manual'">请在「证书管理」页上传覆盖该域名的证书</div>
            </el-form-item>
            <el-form-item label="强制HTTPS">
              <el-switch v-model="httpsForm.forceHttps" :disabled="httpsForm.sslMode === 'off'" />
              <span class="ms-2 text-muted small">HTTP 访问 301 跳转到 HTTPS</span>
            </el-form-item>
            <el-form-item><el-button type="primary" :loading="savingHttps" @click="saveHttps">保存 HTTPS 配置</el-button></el-form-item>
          </el-form>
        </el-tab-pane>
        <el-tab-pane label="缓存" name="cache">
          <el-form label-width="120px" style="max-width:640px" class="mt-3">
            <el-form-item label="页面缓存">
              <el-switch v-model="cacheForm.enabled" />
              <span class="ms-2 text-muted small">开启后静态内容边缘缓存加速</span>
            </el-form-item>
            <el-form-item><el-button type="primary" :loading="savingCache" @click="saveCache">保存缓存配置</el-button></el-form-item>
          </el-form>
        </el-tab-pane>
        <el-tab-pane label="源站" name="origin">
          <el-form label-width="120px" style="max-width:640px" class="mt-3">
            <el-form-item label="源站协议">
              <el-select v-model="originForm.originScheme" style="width:120px">
                <el-option value="http" label="http" /><el-option value="https" label="https" /><el-option value="s3" label="s3" />
              </el-select>
            </el-form-item>
            <el-form-item label="源站地址">
              <el-input v-model="originForm.originHost" placeholder="源站地址" style="width:240px" />
              <span class="mx-1">:</span>
              <el-input-number v-model="originForm.originPort" :min="1" :max="65535" />
            </el-form-item>
            <el-form-item><el-button type="primary" :loading="savingOrigin" @click="saveOrigin">保存源站配置</el-button></el-form-item>
          </el-form>
        </el-tab-pane>
        <el-tab-pane label="DNS / CNAME" name="dns">
          <el-alert type="info" :closable="false" class="mt-3"
            :title="`请将域名 CNAME 解析到节点提供的接入地址后即可生效`" />
          <el-form label-width="120px" style="max-width:640px" class="mt-3">
            <el-form-item label="当前状态">
              <el-tag size="small">{{ server?.nodesOnline ? '节点正常' : '节点离线' }}</el-tag>
            </el-form-item>
          </el-form>
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>
