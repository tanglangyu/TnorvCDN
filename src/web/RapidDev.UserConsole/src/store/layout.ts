import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

/**
 * 布局状态 store。
 * 跨所有 layout 实例共享侧边栏收起状态(解决 dashboard 和 system 页分属不同 layout 实例的问题)。
 * 状态持久化到 localStorage,刷新后保持。
 */
export const useLayoutStore = defineStore(
  'layout',
  () => {
    /** 侧边栏是否收起 */
    const sidebarCollapsed = ref(false)

    function toggle() {
      sidebarCollapsed.value = !sidebarCollapsed.value
    }

    function setCollapsed(v: boolean) {
      sidebarCollapsed.value = v
    }

    watch(sidebarCollapsed, (v) => {
      localStorage.setItem('rapiddev:sidebar-collapsed', JSON.stringify(v))
    })

    return { sidebarCollapsed, toggle, setCollapsed }
  },
  {
    persist: {
      key: 'rapiddev:layout',
    },
  },
)
