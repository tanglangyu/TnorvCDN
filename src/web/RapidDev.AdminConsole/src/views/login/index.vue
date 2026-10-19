<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUserStore } from '@/store/user'
import { useThemeStore } from '@/store/theme'
import { useSiteStore } from '@/store/site'
import TGeeTest from '@/components/TGeeTest.vue'
import { authApi, type GeeTestResult } from '@/api/auth'
import { smsApi } from '@/api/sms'
import { startNotificationHub } from '@/composables/useNotification'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()
const themeStore = useThemeStore()
const siteStore = useSiteStore()

type LoginType = 'password' | 'phone'
const loginType = ref<LoginType>('password')

const pwdForm = reactive({ userName: 'admin', password: 'admin123' })
const phoneForm = reactive({ phone: '', smsCode: '', countryCode: '0086' })
// 国别选项：暂只开放 0086，其余置灰占位
const countryOptions = [
  { code: '0086', label: '+86 中国大陆', enabled: true },
  { code: '00852', label: '+852 中国香港', enabled: false },
  { code: '00853', label: '+853 中国澳门', enabled: false },
  { code: '00886', label: '+886 中国台湾', enabled: false },
]
const loading = ref(false)
const error = ref('')

// 站点信息(系统名/副标题/备案号)统一走 siteStore,与后台 layout 共享
const siteInfo = siteStore

// GeeTest(两种登录方式共用,切 Tab 时重置)
const captchaId = ref('')
const captchaEnabled = ref(false)
const geeRef = ref<any>(null)
const geeResult = ref<GeeTestResult | null>(null)

// 短信验证码倒计时
const smsCountdown = ref(0)
let smsTimer: ReturnType<typeof setInterval> | null = null

async function loadSiteInfo() { siteStore.load() }

async function loadCaptcha() {
  try {
    const res: any = await authApi.getCaptcha()
    captchaId.value = res.captchaId
    captchaEnabled.value = res.enabled !== false
  } catch {}
}

function onVerified(payload: GeeTestResult) {
  geeResult.value = payload
  error.value = ''
}

function switchTab(t: LoginType) {
  if (loginType.value === t) return
  loginType.value = t
  error.value = ''
  geeResult.value = null
  geeRef.value?.reset?.()
}

async function sendSms() {
  if (phoneForm.countryCode === '0086' && !/^1[3-9]\d{9}$/.test(phoneForm.phone)) { error.value = '请输入正确的手机号'; return }
  if (!phoneForm.phone) { error.value = '请输入手机号'; return }
  error.value = ''
  try {
    const res: any = await smsApi.send(phoneForm.phone, 'phone-login', phoneForm.countryCode)
    smsCountdown.value = 60
    smsTimer = setInterval(() => {
      smsCountdown.value--
      if (smsCountdown.value <= 0 && smsTimer) { clearInterval(smsTimer); smsTimer = null }
    }, 1000)
    // 开发环境后端可能返回验证码明文(DebugReturnCode=true),前端永不读取,避免暴露
    error.value = res?.message || '验证码已发送'
  } catch (e: any) {
    error.value = e?.message || '发送失败'
  }
}

async function handleLogin() {
  if (!pwdForm.userName || !pwdForm.password) { error.value = '请输入用户名和密码'; return }
  if (captchaEnabled.value && !geeResult.value) { error.value = '请先完成人机验证'; return }
  loading.value = true
  error.value = ''
  try {
    await userStore.login({
      userName: pwdForm.userName,
      password: pwdForm.password,
      ...(geeResult.value || {}),
    })
    await startNotificationHub()
    const redirect = (route.query.redirect as string) || '/dashboard'
    router.push(redirect)
  } catch (e: any) {
    error.value = e?.message || '登录失败'
    geeResult.value = null
    geeRef.value?.reset?.()
  } finally {
    loading.value = false
  }
}

async function handlePhoneLogin() {
  if (phoneForm.countryCode === '0086' && !/^1[3-9]\d{9}$/.test(phoneForm.phone)) { error.value = '请输入正确的手机号'; return }
  if (!phoneForm.smsCode) { error.value = '请输入短信验证码'; return }
  if (captchaEnabled.value && !geeResult.value) { error.value = '请先完成人机验证'; return }
  loading.value = true
  error.value = ''
  try {
    await userStore.login({
      userName: '',
      password: '',
      loginType: 1,
      phone: phoneForm.countryCode + phoneForm.phone,
      smsCode: phoneForm.smsCode,
      ...(geeResult.value || {}),
    } as any)
    await startNotificationHub()
    const redirect = (route.query.redirect as string) || '/dashboard'
    router.push(redirect)
  } catch (e: any) {
    error.value = e?.message || '登录失败'
    geeResult.value = null
    geeRef.value?.reset?.()
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  themeStore.init()
  loadSiteInfo()
  loadCaptcha()
})

onUnmounted(() => { if (smsTimer) clearInterval(smsTimer) })
</script>

<template>
  <div class="login-page">
    <div class="login-box">
      <div class="login-head">
        <span class="login-logo"><i class="ti ti-bolt"></i></span>
        <h1 class="login-title">{{ siteInfo.name || 'RapidDev 快速开发框架' }}</h1>
        <div class="login-subtitle" v-if="siteInfo.subtitle">{{ siteInfo.subtitle }}</div>
      </div>

      <el-card shadow="always" class="login-card">
        <el-tabs v-model="loginType" stretch>
          <el-tab-pane label="账号密码" name="password" />
          <el-tab-pane label="手机号" name="phone" />
        </el-tabs>

        <!-- 账号密码登录 -->
        <el-form v-if="loginType === 'password'" label-position="top" size="large" @submit.prevent="handleLogin">
          <el-form-item label="用户名">
            <el-input v-model="pwdForm.userName" placeholder="请输入用户名" clearable>
              <template #prefix><i class="ti ti-user"></i></template>
            </el-input>
          </el-form-item>
          <el-form-item label="密码">
            <el-input v-model="pwdForm.password" type="password" placeholder="请输入密码" show-password @keyup.enter="handleLogin">
              <template #prefix><i class="ti ti-lock"></i></template>
            </el-input>
          </el-form-item>
          <el-form-item v-if="captchaEnabled && captchaId" label="人机验证">
            <TGeeTest ref="geeRef" :captcha-id="captchaId" :enabled="captchaEnabled" @verified="onVerified" />
          </el-form-item>
          <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="mb-3" />
          <el-button type="primary" native-type="submit" :loading="loading" size="large" style="width: 100%">
            <i class="ti ti-login" style="margin-right: 4px"></i>登 录
          </el-button>
        </el-form>

        <!-- 手机号登录 -->
        <el-form v-else label-position="top" size="large" @submit.prevent="handlePhoneLogin">
          <el-form-item label="手机号">
            <el-input v-model="phoneForm.phone" placeholder="请输入手机号" clearable>
              <template #prepend>
                <el-select v-model="phoneForm.countryCode" style="width: 132px">
                  <el-option v-for="c in countryOptions" :key="c.code" :value="c.code" :label="c.label" :disabled="!c.enabled" />
                </el-select>
              </template>
            </el-input>
          </el-form-item>
          <el-form-item label="短信验证码">
            <div class="d-flex gap-2" style="width: 100%">
              <el-input v-model="phoneForm.smsCode" placeholder="请输入验证码" style="flex: 1" @keyup.enter="handlePhoneLogin">
                <template #prefix><i class="ti ti-message"></i></template>
              </el-input>
              <el-button :disabled="smsCountdown > 0" @click="sendSms" style="white-space: nowrap">
                {{ smsCountdown > 0 ? `${smsCountdown}s` : '发送验证码' }}
              </el-button>
            </div>
          </el-form-item>
          <el-form-item v-if="captchaEnabled && captchaId" label="人机验证">
            <TGeeTest ref="geeRef" :captcha-id="captchaId" :enabled="captchaEnabled" @verified="onVerified" />
          </el-form-item>
          <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="mb-3" />
          <el-button type="primary" native-type="submit" :loading="loading" size="large" style="width: 100%">
            <i class="ti ti-login" style="margin-right: 4px"></i>登 录
          </el-button>
        </el-form>
      </el-card>

      <div class="login-foot text-muted">默认账号 <strong>admin</strong> / 密码 <strong>admin123</strong></div>
      <div class="login-foot text-muted login-beian">
        <template v-if="siteInfo.icp || siteInfo.psb">
          <a v-if="siteInfo.icp" href="https://beian.miit.gov.cn" target="_blank" rel="noopener" class="text-muted text-decoration-none">{{ siteInfo.icp }}</a>
          <span v-if="siteInfo.icp && siteInfo.psb" style="margin: 0 8px">|</span>
          <span v-if="siteInfo.psb">{{ siteInfo.psb }}</span>
        </template>
        <div v-if="siteInfo.copyright">{{ siteInfo.copyright }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px 16px;
  background: linear-gradient(135deg, var(--el-color-primary-light-9) 0%, var(--el-bg-color) 60%);
}
.login-box { width: 100%; max-width: 420px; }
.login-head { text-align: center; margin-bottom: 20px; }
.login-logo {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 12px;
  background: linear-gradient(135deg, var(--el-color-primary), var(--el-color-primary-dark-2));
  color: #fff;
  font-size: 26px;
  margin-bottom: 10px;
  box-shadow: 0 4px 14px var(--el-color-primary-light-5);
}
.login-title { font-size: 22px; font-weight: 600; color: var(--el-text-color-primary); margin: 0; }
.login-subtitle { font-size: 13px; color: var(--el-text-color-secondary); margin-top: 6px; }
.login-card { border-radius: 10px; }
.login-card :deep(.el-card__body) { padding: 8px 24px 24px; }
.login-foot { text-align: center; font-size: 13px; margin-top: 16px; }
.login-beian { font-size: 12px; margin-top: 8px; }
.text-decoration-none { text-decoration: none; }
</style>
