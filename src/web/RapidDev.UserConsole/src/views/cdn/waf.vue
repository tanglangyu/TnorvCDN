<script setup lang="ts">
/** WAF 概览 —— 近24h 我的站点拦截统计（/api/user/waf/summary） */
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'
const loadData = async () => (await cdnApi.get('/user/waf/summary')) as any
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">概览</h2><div class="text-muted">WAF安全 · 近 24 小时拦截分析</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card class="mb-3">
          <el-statistic title="24h 拦截总数" :value="data?.blocked24h ?? 0" />
        </el-card>
        <el-card>
          <el-table :data="data?.byReason || []" stripe>
            <el-table-column prop="reason" label="拦截原因" min-width="220" />
            <el-table-column prop="count" label="次数" width="110" />
          </el-table>
          <el-empty v-if="!(data?.byReason || []).length" description="24 小时内无拦截记录" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
