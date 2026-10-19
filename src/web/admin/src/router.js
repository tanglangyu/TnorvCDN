import { createRouter, createWebHistory } from 'vue-router'
import { ElMessage } from 'element-plus'

const routes = [
  { path: '/login', component: () => import('./views/Login.vue') },
  {
    path: '/',
    component: () => import('./views/Layout.vue'),
    meta: { requiresAuth: true },
    children: [
      { path: '', redirect: '/dashboard' },
      { path: 'dashboard', component: () => import('./views/Dashboard.vue') },
      { path: 'servers', component: () => import('./views/Servers.vue') },
      { path: 'nodes', component: () => import('./views/Nodes.vue') },
      { path: 'clusters', component: () => import('./views/Clusters.vue') },
      { path: 'users', component: () => import('./views/Users.vue') },
      { path: 'logs', component: () => import('./views/Logs.vue') },
      { path: 'certs', component: () => import('./views/Certs.vue') },
      { path: 'billing', component: () => import('./views/Billing.vue') },
      { path: 'protection', component: () => import('./views/Protection.vue') },
    ],
  },
]

const router = createRouter({ history: createWebHistory(), routes })

router.beforeEach((to) => {
  if (to.meta.requiresAuth && !localStorage.getItem('token')) {
    ElMessage.warning('请先登录')
    return '/login'
  }
})

export default router
