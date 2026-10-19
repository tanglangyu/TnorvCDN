<script setup lang="ts">
/** 收支明细 —— 已支付订单流水（/api/user/finance/logs） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const r = await cdnApi.get('/user/finance/logs', { page: pager.value.page, pageSize: pager.value.pageSize }) as any
  pager.value.total = r?.data?.total ?? 0
  return r?.data?.items || []
}
const fmt = (t: string) => (t ? new Date(t).toLocaleString() : '-')
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">收支明细</h2><div class="text-muted">财务管理 · 已支付订单流水</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-table v-if="(data || []).length" :data="data" stripe>
            <el-table-column label="方向" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="row.direction === 'income' ? 'success' : 'danger'">
                  {{ row.direction === 'income' ? '收入' : '支出' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="kindLabel" label="类型" width="120" />
            <el-table-column prop="orderNo" label="订单号" min-width="180" />
            <el-table-column label="金额" width="110">
              <template #default="{ row }">¥{{ Number(row.amount).toFixed(2) }}</template>
            </el-table-column>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ fmt(row.paidAt || row.createdAt) }}</template>
            </el-table-column>
          </el-table>
<el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
          <el-empty v-else description="暂无收支记录" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
