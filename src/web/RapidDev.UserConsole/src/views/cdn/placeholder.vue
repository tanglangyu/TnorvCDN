<script setup lang="ts">
/**
 * CdnPlaceholder —— CDN 模块页面占位组件(用户端)。
 * 页面标题取自路由 meta.title,模块名取自 meta.module。
 * 后端 API 就绪前,统一展示模块信息 + 功能说明,风格与框架其他页面一致。
 */
import { useRoute } from 'vue-router'
import { useSiteStore } from '@/store/site'
import { TCard } from '@/components/t'

const route = useRoute()
const siteStore = useSiteStore()

const title = (route.meta?.title as string) || '页面'
const module = (route.meta?.module as string) || ''
const desc = (route.meta?.desc as string) || '该功能页已规划，等待后端 API 就绪后接入。'
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">{{ title }}</h2>
          <div class="text-muted">{{ siteStore.name }}<template v-if="module"> · {{ module }}</template></div>
        </div>
      </div>
    </div>

    <TCard bordered>
      <div class="empty">
        <div class="empty-header"><i class="ti ti-cloud-cog" style="font-size:2.5rem"></i></div>
        <p class="empty-title">{{ title }}</p>
        <p class="empty-subtitle text-muted">{{ desc }}</p>
      </div>
    </TCard>
  </div>
</template>
