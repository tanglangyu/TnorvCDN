import { useThemeStore } from '@/store/theme'

/**
 * 主题切换 composable
 * 用法:const { mode, toggle, setMode } = useTheme()
 */
export function useTheme() {
  const store = useThemeStore()
  return {
    mode: store.mode,
    toggle: store.toggle,
    setMode: store.setMode,
  }
}
