<script setup lang="ts">
/** 消息提醒设置 —— 绑定手机号（验证码校验）+ 订阅提醒项（与全局启用项取交集生效） */
import { ref, onMounted } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const smsEnabled = ref(false)
const hasPhone = ref(false)
const phone = ref('')            // 绑定时输入的新手机号（裸号码，不带国别码）
const countryCode = ref('0086')  // 国别码（暂只开放 0086）
const maskedPhone = ref('')
const code = ref('')
const codeSending = ref(false)
const countdown = ref(0)
const subscribed = ref<string[]>([])
const allEvents = ref<{ key: string; label: string }[]>([])
const globalEvents = ref<string[]>([])
const saving = ref(false)

const load = async () => {
  const r: any = await cdnApi.get('/user/notify/settings')
  const d = r?.data ?? r
  smsEnabled.value = !!d?.smsEnabled
  hasPhone.value = !!d?.hasPhone
  maskedPhone.value = d?.phone || ''
  subscribed.value = d?.subscribed || []
  allEvents.value = d?.allEvents || []
  globalEvents.value = d?.globalEvents || []
}
onMounted(load)

const sendCode = async () => {
  if (countryCode.value === '0086' && !/^1[3-9]\d{9}$/.test(phone.value)) return ElMessage.warning('请输入正确的手机号')
  if (!phone.value) return ElMessage.warning('请输入手机号')
  codeSending.value = true
  try {
    const r: any = await cdnApi.post('/user/notify/phone/send-code', { phone: countryCode.value + phone.value })
    r?.code === 200 ? ElMessage.success('验证码已发送') : ElMessage.error(r?.message || '发送失败')
    if (r?.code === 200) {
      countdown.value = 60
      const t = setInterval(() => { countdown.value--; if (countdown.value <= 0) clearInterval(t) }, 1000)
    }
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || e?.response?.data || '发送失败') } finally { codeSending.value = false }
}

const save = async () => {
  saving.value = true
  try {
    const payload: any = { events: subscribed.value }
    if (phone.value && phone.value !== maskedPhone.value.replace(/\*+/g, '')) {
      if (!hasPhone.value || phone.value !== '') {
        payload.phone = phone.value
        payload.code = code.value
      }
    }
    const r: any = await cdnApi.put('/user/notify/settings', payload)
    r?.code === 200 ? ElMessage.success('已保存') : ElMessage.error(r?.message || '保存失败')
    phone.value = ''; code.value = ''
    load()
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || e?.response?.data || '保存失败') } finally { saving.value = false }
}

// 事件展示：仅显示全局已启用的项（全局未启用订阅了也不生效）
const visibleEvents = () => allEvents.value.filter((e) => globalEvents.value.includes(e.key))
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="col-auto"><h2 class="page-title">消息提醒设置</h2><div class="text-muted">短信提醒 · 绑定手机号后可订阅</div></div>
    </div>

    <el-alert v-if="!smsEnabled" type="info" :closable="false" class="mb-4" title="短信提醒功能暂未开放，开通后可在本页绑定手机号并订阅提醒。" />
    <el-alert type="warning" :closable="false" class="mb-4">
      <template #title>
        提醒短信由 Tnorv SMS 发送。如未收到，请确认手机号正确；短信内容不包含链接，一切操作请登录控制台完成。
      </template>
    </el-alert>

    <el-card class="mb-4">
      <template #header><b>绑定手机号</b></template>
      <template v-if="hasPhone">
        <div class="mb-3">当前绑定：<b>{{ maskedPhone }}</b>
          <el-button size="small" class="ms-3" @click="hasPhone = false; phone = ''">更换手机号</el-button>
        </div>
      </template>
      <template v-else>
        <div class="d-flex align-items-center gap-2 mb-3" style="flex-wrap: wrap">
          <el-input v-model="phone" placeholder="手机号" style="width: 200px" maxlength="11">
            <template #prepend>
              <el-select v-model="countryCode" style="width: 128px">
                <el-option value="0086" label="+86 中国大陆" />
                <el-option value="00852" label="+852 中国香港" disabled />
                <el-option value="00853" label="+853 中国澳门" disabled />
                <el-option value="00886" label="+886 中国台湾" disabled />
              </el-select>
            </template>
          </el-input>
          <el-input v-model="code" placeholder="验证码" style="width: 120px" maxlength="6" />
          <el-button :loading="codeSending" :disabled="countdown > 0" @click="sendCode">
            {{ countdown > 0 ? countdown + 's 后重发' : '获取验证码' }}
          </el-button>
        </div>
        <div class="text-muted small mb-3">绑定后可用手机号 + 短信验证码免密登录。</div>
      </template>
    </el-card>

    <el-card>
      <template #header><b>订阅提醒项</b><span class="text-muted ms-2 small">仅以下全局开放的项目可订阅</span></template>
      <el-empty v-if="!visibleEvents().length" description="管理员暂未开放任何提醒项" />
      <div v-else class="d-flex flex-wrap" style="gap: 12px 24px">
        <el-checkbox v-for="e in visibleEvents()" :key="e.key" v-model="subscribed" :value="e.key">{{ e.label }}</el-checkbox>
      </div>
      <div class="mt-3"><el-button type="primary" :loading="saving" @click="save">保存</el-button></div>
    </el-card>
  </div>
</template>
