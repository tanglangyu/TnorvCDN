import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export type CryptoMode = 'off' | 'aes'

// 全局单例引用(避免动态 import 拿到不同模块实例)
let _service: any = null
let _installCrypto: any = null
let _uninstall: (() => void) | null = null

export const useCryptoStore = defineStore(
  'crypto',
  () => {
    const mode = ref<CryptoMode>('off')
    const locked = ref(false)
    const lastError = ref<string>('')

    const enabled = computed(() => mode.value !== 'off')

    async function _ensureSDK() {
      if (!_service) {
        const reqMod: any = await import('@/utils/request')
        _service = reqMod.default
      }
      if (!_installCrypto) {
        const sdk: any = await import('@/utils/rapiddev-crypto-sdk.js')
        _installCrypto = sdk.installCryptoInterceptor
      }
    }

    async function switchMode(newMode: CryptoMode) {
      if (locked.value) return
      if (newMode === mode.value) return
      locked.value = true
      lastError.value = ''
      try {
        await _ensureSDK()
        // 卸载旧 adapter
        if (_uninstall) { _uninstall(); _uninstall = null }

        if (newMode === 'off') {
          mode.value = 'off'
        } else {
          const fn = _installCrypto as (inst: any, opts: any) => (() => void)
          _uninstall = fn(_service, {
            baseUrl: window.location.origin,
            mode: newMode,
            debug: false,
          })
          mode.value = newMode
        }
        try { localStorage.setItem('rapiddev:crypto', JSON.stringify({ mode: mode.value })) } catch {}
      } catch (e: any) {
        lastError.value = e?.message || String(e)
        console.error('[CryptoStore] switch failed:', e)
      } finally {
        locked.value = false
      }
    }

    return { mode, enabled, locked, lastError, switchMode }
  },
  {
    persist: { key: 'rapiddev:crypto', pick: ['mode'] },
  },
)
