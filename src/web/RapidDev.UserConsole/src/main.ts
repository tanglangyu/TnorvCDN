import { createApp } from 'vue'
import { createPinia } from 'pinia'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'

// Element Plus 样式(必须在 App 之前导入,以便覆盖)
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
// Tabler 图标字体(ti ti-home 等,仅图标)
import '@tabler/icons-webfont/dist/tabler-icons.min.css'

import App from './App.vue'
import router from './router'
import './styles/main.scss'

// 权限指令 + 组件:无权限的元素自动隐藏
import { perm as permDirective } from '@/directives/perm'
import PermContainer from '@/components/PermContainer.vue'

const app = createApp(App)

const pinia = createPinia()
pinia.use(piniaPluginPersistedstate)

app.use(pinia)
app.use(router)
app.use(ElementPlus, { locale: zhCn })

// 全局注册: v-perm 指令 + PermContainer 组件
app.directive('perm', permDirective)
app.component('PermContainer', PermContainer)

app.mount('#app')
