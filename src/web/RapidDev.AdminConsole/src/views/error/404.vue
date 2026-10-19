<script setup lang="ts">
import { useRouter } from 'vue-router'
const router = useRouter()
</script>
<template>
  <div class="page page-center">
    <div class="container-tight py-4">
      <div class="empty">
        <div class="empty-header">404</div>
        <p class="empty-title">页面未找到</p>
        <p class="empty-subtitle text-muted">抱歉,你访问的页面不存在或已被移动。</p>
        <div class="empty-action">
          <button class="btn btn-primary" @click="router.push('/dashboard')"><i class="ti ti-home me-1"></i>返回首页</button>
        </div>
      </div>
    </div>
  </div>
</template>
