import { defineStore } from 'pinia'
import { ref } from 'vue'
import { authApi } from '@/api/auth'

/** 站点信息(系统名/副标题/备案号等)。登录页与后台 layout 共用,避免重复请求 */
export const useSiteStore = defineStore('site', () => {
  const name = ref('TnorvCDN')
  const subtitle = ref('')
  const icp = ref('')
  const psb = ref('')
  const copyright = ref('')
  let loaded = false

  async function load() {
    if (loaded) return
    try {
      const res: any = await authApi.getSiteInfo()
      if (res) {
        name.value = res.name || name.value
        subtitle.value = res.subtitle || ''
        icp.value = res.icp || ''
        psb.value = res.psb || ''
        copyright.value = res.copyright || ''
      }
    } catch {}
    loaded = true
  }

  return { name, subtitle, icp, psb, copyright, load }
})
