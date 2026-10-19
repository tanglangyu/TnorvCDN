<script setup lang="ts">
/**
 * GeeTest v4 验证组件(可复用)。
 * float 模式:GeeTest SDK 在容器内渲染按钮,点击后弹出验证弹窗。
 * 按钮通过 nativeButton 宽度适配父容器。
 */
import { ref, onMounted, watch, onUnmounted } from 'vue'

const props = defineProps<{ captchaId: string; enabled?: boolean }>()
const emit = defineEmits<{
  (e: 'verified', payload: { lotNumber: string; captchaOutput: string; passToken: string; genTime: string }): void
  (e: 'reset'): void
}>()

const containerId = 'geetest-' + Math.random().toString(36).slice(2, 8)
let captchaObj: any = null
const verified = ref(false)
let initTimer: ReturnType<typeof setTimeout> | null = null

declare global {
  interface Window { initGeetest4?: (opts: any, cb: (captcha: any) => void) => void }
}

function init() {
  if (!props.enabled || !props.captchaId) return
  if (!window.initGeetest4) { initTimer = setTimeout(init, 500); return }
  if (captchaObj) return

  window.initGeetest4(
    {
      captchaId: props.captchaId,
      product: 'float',
      nativeButton: {
        width: '100%',
        height: '46px',
      },
    },
    (captcha: any) => {
      captchaObj = captcha
      captcha.appendTo('#' + containerId)
      captcha.onSuccess(() => {
        const result = captcha.getValidate()
        if (result) {
          verified.value = true
          emit('verified', {
            lotNumber: result.lot_number, captchaOutput: result.captcha_output,
            passToken: result.pass_token, genTime: result.gen_time,
          })
        }
      })
      captcha.onError(() => { verified.value = false })
    },
  )
}

function reset() {
  verified.value = false
  captchaObj?.reset?.()
  emit('reset')
}

defineExpose({ reset })

const canInit = () => props.enabled && !!props.captchaId
watch(canInit, (val) => { if (val) setTimeout(init, 100) })
onMounted(() => { if (canInit()) init() })
onUnmounted(() => { if (initTimer) clearTimeout(initTimer) })
</script>

<template>
  <div>
    <div :id="containerId"></div>
    <div v-if="verified" class="text-success small mt-1"><i class="ti ti-circle-check me-1"></i>验证通过</div>
  </div>
</template>
