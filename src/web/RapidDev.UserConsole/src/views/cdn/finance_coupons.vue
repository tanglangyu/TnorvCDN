<script setup lang="ts">
/** 优惠券 —— 平台可领优惠券列表（/api/user/finance/coupons） */
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'
const loadData = async () => {
  const r = await cdnApi.get('/user/finance/coupons') as any
  return r?.data || r || []
}
const fmt = (t: string) => (t ? new Date(t).toLocaleDateString() : '-')
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">优惠券</h2><div class="text-muted">财务管理 · 平台优惠券</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-table v-if="(data || []).length" :data="data" stripe>
            <el-table-column prop="code" label="券码" min-width="160" />
            <el-table-column prop="name" label="名称" min-width="160" />
            <el-table-column label="面额" width="110">
              <template #default="{ row }">¥{{ ((row.AmountCents ?? row.amountCents ?? 0) / 100).toFixed(2) }}</template>
            </el-table-column>
            <el-table-column label="有效期至" width="130">
              <template #default="{ row }">{{ fmt(row.ExpiresAt ?? row.expiresAt) }}</template>
            </el-table-column>
          </el-table>
          <el-empty v-else description="暂无可领优惠券" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
