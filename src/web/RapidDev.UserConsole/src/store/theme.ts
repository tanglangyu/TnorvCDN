import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export type ThemeMode = 'light' | 'dark'

export const useThemeStore = defineStore(
  'theme',
  () => {
    const mode = ref<ThemeMode>('light')

    function setMode(m: ThemeMode) {
      mode.value = m
      applyMode(m)
    }

    function toggle() {
      setMode(mode.value === 'light' ? 'dark' : 'light')
    }

    function applyMode(m: ThemeMode) {
      // Element Plus 暗色模式通过 html.dark 类启用
      document.documentElement.classList.toggle('dark', m === 'dark')
    }

    function init() {
      applyMode(mode.value)
    }

    watch(mode, (m) => applyMode(m))

    return { mode, setMode, toggle, applyMode, init }
  },
  {
    persist: {
      key: 'rapiddev:theme',
    },
  },
)
