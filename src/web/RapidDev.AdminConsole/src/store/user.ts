import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi } from '@/api/auth'
import type { LoginResult, UserInfo, MenuNode } from '@/types'

export const useUserStore = defineStore(
  'user',
  () => {
    const token = ref<string>('')
    const refreshToken = ref<string>('')
    const expiresAt = ref<number>(0)
    const sessionId = ref<string>('')
    const userInfo = ref<UserInfo | null>(null)
    const menus = ref<MenuNode[]>([])
    const roles = ref<string[]>([])
    const permissions = ref<string[]>([])

    const isLogin = computed(() => !!token.value)
    const isSuperAdmin = computed(() => roles.value.includes('admin') || permissions.value.includes('*:*:*'))

    async function login(input: import('@/api/auth').LoginInput) {
      const res = (await authApi.login(input)) as LoginResult
      token.value = res.accessToken
      refreshToken.value = res.refreshToken
      expiresAt.value = res.expiresAt
      sessionId.value = res.sessionId || ''
      await fetchUserInfo()
      await fetchMenus()
      return res
    }

    async function fetchUserInfo() {
      const info = (await authApi.getUserInfo()) as UserInfo
      userInfo.value = info
      roles.value = info.roles || []
      permissions.value = info.permissions || []
      return info
    }

    async function fetchMenus() {
      const list = (await authApi.getMenus()) as MenuNode[]
      menus.value = list
      return list
    }

    function logout() {
      token.value = ''
      refreshToken.value = ''
      expiresAt.value = 0
      sessionId.value = ''
      userInfo.value = null
      menus.value = []
      roles.value = []
      permissions.value = []
    }

    function hasPermission(perm: string): boolean {
      if (isSuperAdmin.value) return true
      return permissions.value.includes(perm) || permissions.value.includes('*:*:*')
    }

    function hasAnyPermission(perms: string[]): boolean {
      if (isSuperAdmin.value) return true
      return perms.some((p) => permissions.value.includes(p))
    }

    return {
      token,
      refreshToken,
      expiresAt,
      sessionId,
      userInfo,
      menus,
      roles,
      permissions,
      isLogin,
      isSuperAdmin,
      login,
      fetchUserInfo,
      fetchMenus,
      logout,
      hasPermission,
      hasAnyPermission,
    }
  },
  {
    persist: {
      key: 'rapiddev:user',
      pick: ['token', 'refreshToken', 'expiresAt', 'sessionId', 'userInfo', 'roles', 'permissions'],
    },
  },
)
