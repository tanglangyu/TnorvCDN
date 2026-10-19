import { http } from '@/utils/request'

export interface SmsCodeSendResult {
  success: boolean
  message?: string
}

export const smsApi = {
  /** 发送短信验证码(匿名,受 IP+手机号流控)。前端永不读取验证码明文,即使后端误开 DebugReturnCode 也不暴露 */
  send: (mobile: string, scene = 'phone-login', countryCode = '0086') =>
    http.post<SmsCodeSendResult>('/sms-code/send', { mobile, scene, countryCode }),
}
