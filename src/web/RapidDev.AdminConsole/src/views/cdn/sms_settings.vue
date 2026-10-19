<script setup lang="ts">
/** 短信设置 —— 网关（Tnorv SMS）配置 + 全局提醒项 + 测试发送 + 发送记录 */
import { ref, onMounted } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const st = ref<any>({ enabled: false, apiBase: '', apiKey: '', apiSecret: '', signature: '', verifySign: true, enabledEvents: [], dailyLimitPerMobile: 10, codeExpireSeconds: 300 })
const EVENTS: [string, string][] = [
  ['sub_expiring', '订阅到期提醒'], ['balance_low', '余额不足提醒'], ['traffic_over', '流量超限提醒'],
  ['cert_failed', '证书签发失败'], ['cert_expiring', '证书到期提醒'], ['icp_result', '备案审核结果'],
  ['ticket_reply', '工单回复提醒'], ['node_offline', '节点离线告警'],
]
const saving = ref(false)
const testMobile = ref('')
const testing = ref(false)
const logs = ref<any[]>([])
const logsTotal = ref(0)
const logsPage = ref(1)
const loadLogs = async () => {
  const d: any = await cdnApi.get('/admin/sms/logs', { page: logsPage.value, pageSize: 10 })
  logs.value = d?.items || []
  logsTotal.value = d?.total || 0
}

onMounted(async () => {
  const d: any = await cdnApi.get('/admin/sms/settings')
  const s = d?.data ?? d
  st.value = { ...st.value, ...s, enabledEvents: s?.enabledEvents || [], apiSecret: '' }
  loadLogs()
})

const save = async () => {
  saving.value = true
  try {
    const payload: any = { ...st.value }
    if (!payload.apiSecret) delete payload.apiSecret // 空值=不修改
    await cdnApi.put('/admin/sms/settings', payload)
    ElMessage.success('已保存')
  } catch (e: any) { ElMessage.error(e?.response?.data || '保存失败') } finally { saving.value = false }
}

const testSend = async () => {
  if (!/^1[3-9]\d{9}$/.test(testMobile.value)) return ElMessage.warning('请输入正确的手机号')
  testing.value = true
  try {
    const r: any = await cdnApi.post('/admin/sms/test', { mobile: testMobile.value })
    r?.code === 200 || r?.success ? ElMessage.success('测试短信已提交：' + (r?.message || '已受理')) : ElMessage.error(r?.message || '发送失败')
    loadLogs()
  } catch (e: any) { ElMessage.error(e?.response?.data || '发送失败') } finally { testing.value = false }
}

const fmtTime = (t?: string) => (t ? new Date(t).toLocaleString() : '-')
const statusTag = (s: string) => (s === 'queued' ? 'success' : s === 'blocked' ? 'warning' : s === 'sent' ? 'success' : 'danger')
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">短信设置</h2><div class="text-muted">Tnorv SMS 网关 · 验证码与提醒短信</div></div>
      </div>
    </div>

    <el-alert type="warning" :closable="false" class="mb-4">
      <template #title>
        使用前请到 <a href="https://sms.tnorv.com/" target="_blank" rel="noopener">sms.tnorv.com</a> 完成负责人实名认证 → 添加资质（审核通过）→ 报备签名（等待通过）→ 报备模板。
        短信签名需先在网关侧报备后使用。注意：短信风控严格，系统已禁止在短信内容中携带任何域名或链接。
      </template>
    </el-alert>

    <el-row :gutter="16">
      <el-col :span="12">
        <el-card>
          <template #header><b>网关配置</b></template>
          <el-form label-width="140px">
            <el-form-item label="启用短信">
              <el-switch v-model="st.enabled" /><span class="ms-2 text-muted small">关闭后全站不发送短信（含验证码）</span>
            </el-form-item>
            <el-form-item label="网关地址"><el-input v-model="st.apiBase" /></el-form-item>
            <el-form-item label="ApiKey"><el-input v-model="st.apiKey" placeholder="对接密钥页创建" /></el-form-item>
            <el-form-item label="ApiSecret"><el-input v-model="st.apiSecret" type="password" show-password :placeholder="st.hasSecret ? '已配置（留空不修改）' : '密钥创建时仅可见一次'" /></el-form-item>
            <el-form-item label="短信签名"><el-input v-model="st.signature" style="width:220px" /><span class="ms-2 text-muted small">不带【】，正式使用需报备</span></el-form-item>
            <el-form-item label="密钥签名校验">
              <el-switch v-model="st.verifySign" /><span class="ms-2 text-muted small">与 sms 平台「对接密钥」页的开关一致</span>
            </el-form-item>
            <el-form-item label="单手机日限额"><el-input-number v-model="st.dailyLimitPerMobile" :min="1" :max="100" style="width:180px" /><span class="ms-2 text-muted small">风控：发送过于频繁会影响签名信誉</span></el-form-item>
            <el-form-item label="验证码有效期(秒)"><el-input-number v-model="st.codeExpireSeconds" :min="60" :max="900" :step="60" style="width:180px" /></el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="saving" @click="save">保存设置</el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>
      <el-col :span="12">
        <el-card class="mb-4">
          <template #header><b>全局提醒项</b><span class="text-muted ms-2 small">用户可在「消息提醒设置」中进一步挑选（交集生效）</span></template>
          <div class="d-flex flex-wrap" style="gap: 12px 20px">
            <el-checkbox v-for="[k, label] in EVENTS" :key="k"
              :model-value="(st.enabledEvents || []).includes(k)"
              @change="(v: any) => { const set = new Set(st.enabledEvents || []); v ? set.add(k) : set.delete(k); st.enabledEvents = [...set] }">
              {{ label }}
            </el-checkbox>
          </div>
          <div class="mt-3"><el-button type="primary" size="small" :loading="saving" @click="save">保存提醒项</el-button></div>
        </el-card>
        <el-card>
          <template #header><b>测试发送</b><span class="text-muted ms-2 small">验证签名与密钥是否配置正确</span></template>
          <div class="d-flex gap-2">
            <el-input v-model="testMobile" placeholder="测试手机号，如 17374668880" style="width: 240px" />
            <el-button type="primary" :loading="testing" @click="testSend">发送测试短信</el-button>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card class="mt-4">
      <template #header><b>发送记录</b></template>
      <el-table :data="logs" stripe size="small">
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column prop="mobile" label="手机号" width="130" />
        <el-table-column prop="event" label="事件" width="120" />
        <el-table-column prop="content" label="内容" min-width="280" show-overflow-tooltip />
        <el-table-column label="状态" width="90">
          <template #default="{ row }"><el-tag size="small" :type="statusTag(row.status)">{{ row.status }}</el-tag></template>
        </el-table-column>
        <el-table-column prop="error" label="说明" min-width="160" show-overflow-tooltip />
        <el-table-column label="时间" width="170">
          <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
        </el-table-column>
      </el-table>
      <el-pagination v-if="logsTotal > 10" class="mt-3" background layout="total, prev, pager, next"
        :total="logsTotal" :page-size="10" :current-page="logsPage"
        @current-change="(p: number) => { logsPage = p; loadLogs() }" />
    </el-card>
  </div>
</template>
