<script setup lang="ts">
/** DNS服务商 —— 三方 DNS 接入配置（存主控数据库，管理员可编辑）
 *  对接 /admin/dns/settings（GET 脱敏回显 / PUT 合并保存，"******" 占位表示保留原值） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const saving = ref(false)
const verifying = ref(false)
const form = ref<any>({ enabled: false, provider: 'aggregated', defaultDomainId: '', providers: {} })
const activeProvider = ref('aggregated')

const providerNames: Record<string, string> = {
  aggregated: '聚合DNS', aliyun: '阿里云', dnspod: '腾讯云 DNSPod', huawei: '华为云', cloudflare: 'Cloudflare',
}
const providerFields: any = {
  aggregated: [{ k: 'apiBase', label: 'API 地址', secret: false }, { k: 'uid', label: 'UID', secret: false }, { k: 'key', label: '密钥 Key', secret: true }],
  aliyun: [{ k: 'accessKeyId', label: 'AccessKeyId', secret: false }, { k: 'accessKeySecret', label: 'AccessKeySecret', secret: true }, { k: 'endpoint', label: 'Endpoint（可选）', secret: false }],
  dnspod: [{ k: 'secretId', label: 'SecretId', secret: false }, { k: 'secretKey', label: 'SecretKey', secret: true }],
  huawei: [{ k: 'accessKeyId', label: 'AccessKeyId', secret: false }, { k: 'secretAccessKey', label: 'SecretAccessKey', secret: true }],
  cloudflare: [{ k: 'apiToken', label: 'API Token', secret: true }],
}

const loadData = async () => {
  const data = await cdnApi.get('/admin/dns/settings') as any
  form.value = { enabled: data.enabled, provider: data.provider, defaultDomainId: data.defaultDomainId, providers: data.providers || {} }
  activeProvider.value = data.provider || 'aggregated'
  return data
}

async function save() {
  saving.value = true
  try {
    await cdnApi.put('/admin/dns/settings', form.value)
    ElMessage.success('已保存，配置立即生效')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}

async function verify() {
  verifying.value = true
  try {
    const r = await cdnApi.post('/admin/dns/verify') as any
    if (r.ok) ElMessage.success(`凭证有效，${providerNames[r.provider] || r.provider} 返回 ${r.domains} 个域名`)
    else ElMessage.error(r.error || '验证失败')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '验证失败') }
  finally { verifying.value = false }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">DNS服务商</h2><div class="text-muted">域名解析 · 三方 DNS 厂商接入（配置存数据库，保存即生效）</div></div>
        <div class="col-auto ms-auto d-flex gap-2">
          <el-button size="small" :loading="verifying" @click="verify">验证凭证</el-button>
          <el-button type="primary" size="small" :loading="saving" @click="save">保存配置</el-button>
        </div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default>
        <el-card class="mb-4">
          <el-form label-width="140px">
            <el-form-item label="启用三方DNS">
              <el-switch v-model="form.enabled" />
              <span class="text-muted small ms-3">关闭后不再自动同步 CNAME/A 记录（不影响已有解析）</span>
            </el-form-item>
            <el-form-item label="当前厂商">
              <el-select v-model="form.provider" style="width: 240px" @change="activeProvider = form.provider">
                <el-option v-for="(name, key) in providerNames" :key="key" :value="key" :label="name" />
              </el-select>
            </el-form-item>
            <el-form-item label="默认主域名ID">
              <el-input v-model="form.defaultDomainId" placeholder="如聚合DNS提供商的主域名 ID" style="width: 300px" />
              <span class="text-muted small ms-3">网站 CNAME 与集群 A 记录都建在这个主域名下</span>
            </el-form-item>
          </el-form>
        </el-card>

        <el-card>
          <template #header><span>厂商凭证</span></template>
          <el-tabs v-model="activeProvider">
            <el-tab-pane v-for="(fields, key) in providerFields" :key="key" :name="key"
              :label="providerNames[key] + (form.provider === key ? '（使用中）' : '')">
              <el-form label-width="180px" class="mt-2" style="max-width: 640px">
                <el-form-item v-for="f in fields" :key="f.k" :label="f.label">
                  <el-input v-model="form.providers[key][f.k]" :placeholder="f.secret ? '已设置（留空或保持 ****** = 不修改）' : ''"
                    :type="f.secret && form.providers[key]?.[f.k] !== '******' ? 'password' : 'text'"
                    :show-password="f.secret" />
                </el-form-item>
                <div class="text-muted small">
                  {{ String(key) === 'aggregated' ? '聚合DNS：sign = md5(uid+timestamp+key)，需具备该主域名的解析管理权限。' : '' }}
                  {{ String(key) === 'cloudflare' ? 'Cloudflare：需 Zone.DNS 编辑权限的 API Token。' : '' }}
                  密钥保存后脱敏显示为 ******；编辑时保持占位符即不修改原值。
                </div>
              </el-form>
            </el-tab-pane>
          </el-tabs>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
