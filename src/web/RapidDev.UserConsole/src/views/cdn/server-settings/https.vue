<script setup lang="ts">
/** HTTPS —— SSL 模式/强制跳转/端口 + 证书状态与续期 */
import { ref, watch, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const form = ref({ sslMode: 'auto', forceHttps: false, httpsPorts: '443' })
watch(() => props.server, (s) => {
  if (!s) return
  form.value.sslMode = s.sslMode || 'auto'
  form.value.forceHttps = !!s.forceHttps
  form.value.httpsPorts = (s.httpsPorts || [443]).join(',')
}, { immediate: true })

const saving = ref(false)
const renewing = ref(false)
const sslRenewing = computed(() => props.server?.sslState === 'pending_issuing')
const stateLabel = (s: string) => ({ pending_dns: '未解析', pending_issuing: '签发中', issued: '已签发', failed: '签发失败' } as any)[s] || '未签发'
const stateTag = (s: string) => ({ pending_dns: 'warning', pending_issuing: 'primary', issued: 'success', failed: 'danger' } as any)[s] || 'info'

const save = async () => {
  const ports = form.value.httpsPorts.split(',').map((x: string) => parseInt(x.trim())).filter((n: number) => n > 0)
  saving.value = true
  try {
    await cdnApi.put('/user/servers/' + props.serverId + '/ssl', {
      sslMode: form.value.sslMode,
      forceHttps: form.value.forceHttps,
      httpsPorts: form.value.sslMode === 'off' ? [] : (ports.length ? ports : [443]),
    })
    ElMessage.success('HTTPS 配置已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}

const renew = async () => {
  renewing.value = true
  try {
    await cdnApi.post('/user/servers/' + props.serverId + '/ssl/renew')
    ElMessage.success('续期请求已下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '发起续期失败') }
  finally { renewing.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">HTTPS</h5>
    <el-form label-width="140px" style="max-width: 640px">
      <el-form-item label="SSL 模式">
        <el-radio-group v-model="form.sslMode">
          <el-radio value="auto">自动签发（ACME）</el-radio>
          <el-radio value="manual">手动上传</el-radio>
          <el-radio value="off">关闭 HTTPS</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item label="HTTPS 端口">
        <el-input v-model="form.httpsPorts" placeholder="443" :disabled="form.sslMode === 'off'" style="width: 220px" />
      </el-form-item>
      <el-form-item label="强制 HTTPS">
        <el-switch v-model="form.forceHttps" :disabled="form.sslMode === 'off'" />
        <span class="text-muted small ms-2">HTTP 请求 301 跳转到 HTTPS</span>
      </el-form-item>
      <el-form-item v-if="props.server?.sslState" label="证书状态">
        <el-tag :type="stateTag(props.server.sslState)">{{ stateLabel(props.server.sslState) }}</el-tag>
        <span v-if="props.server.sslExpiresAt" class="text-muted small ms-2">到期 {{ new Date(props.server.sslExpiresAt).toLocaleDateString() }}</span>
        <span v-if="props.server.sslIssuedDomains" class="text-muted small ms-2">覆盖：{{ props.server.sslIssuedDomains }}</span>
        <div v-if="props.server.sslLastError" class="text-danger small w-100 mt-1">{{ props.server.sslLastError }}</div>
      </el-form-item>
    </el-form>
    <div class="d-flex gap-2">
      <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      <el-button v-if="form.sslMode === 'auto'" type="warning" plain :loading="renewing || sslRenewing" @click="renew">
        {{ sslRenewing ? '签发中…' : '立即更新证书' }}
      </el-button>
    </div>
  </div>
</template>
