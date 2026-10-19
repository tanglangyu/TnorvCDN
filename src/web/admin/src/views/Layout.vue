<template>
  <el-container class="layout">
    <el-aside width="220px" class="aside">
      <div class="logo">TnorvCDN 管理平台</div>
      <el-menu :default-active="$route.path" router background-color="#001529" text-color="#a6adb4" active-text-color="#fff">
        <el-menu-item index="/dashboard"><el-icon><Odometer /></el-icon>仪表盘</el-menu-item>
        <el-menu-item index="/servers"><el-icon><Globe /></el-icon>网站管理</el-menu-item>
        <el-menu-item index="/nodes"><el-icon><Cpu /></el-icon>边缘节点</el-menu-item>
        <el-menu-item index="/clusters"><el-icon><OfficeBuilding /></el-icon>集群管理</el-menu-item>
        <el-menu-item index="/users"><el-icon><User /></el-icon>用户管理</el-menu-item>
        <el-menu-item index="/certs"><el-icon><Postcard /></el-icon>证书管理</el-menu-item>
        <el-menu-item index="/logs"><el-icon><Document /></el-icon>访问日志</el-menu-item>
        <el-menu-item index="/billing"><el-icon><Wallet /></el-icon>套餐计费</el-menu-item>
        <el-menu-item index="/protection"><el-icon><Umbrella /></el-icon>高防 IP</el-menu-item>
      </el-menu>
    </el-aside>
    <el-container>
      <el-header class="header">
        <span></span>
        <el-dropdown @command="onCmd">
          <span class="user">admin <el-icon><ArrowDown /></el-icon></span>
          <template #dropdown>
            <el-dropdown-menu><el-dropdown-item command="logout">退出登录</el-dropdown-item></el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-header>
      <el-main><router-view /></el-main>
    </el-container>
  </el-container>
</template>

<script setup>
const onCmd = (cmd) => {
  if (cmd === 'logout') {
    localStorage.removeItem('token')
    location.href = '/login'
  }
}
</script>

<style scoped>
.layout { height: 100vh; }
.aside { background: #001529; }
.logo { color: #fff; font-size: 16px; font-weight: 600; padding: 20px 16px; }
.aside :deep(.el-menu) { border-right: none; }
.header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #eee; }
.user { cursor: pointer; display: inline-flex; align-items: center; gap: 4px; }
</style>
