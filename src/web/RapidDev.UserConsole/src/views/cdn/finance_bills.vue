<script setup lang="ts">
/** 费用账单 —— 我的订单/账单（/api/user/finance/bills） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const statusFilter = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const params: any = { page: pager.value.page, pageSize: pager.value.pageSize }
  if (statusFilter.value) params.status = statusFilter.value
  const r = await cdnApi.get('/user/finance/bills', params) as any
  // request() 已拆包一次:返回值即 ApiResult.data = { total, items }
  pager.value.total = r?.total ?? 0
  return r?.items || []
}

const onFilter = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}
const fmt = (t: string) => (t ? new Date(t).toLocaleString() : '-')
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">费用账单</h2><div class="text-muted">财务管理 · 订单与账单</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ data, refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-radio-group v-model="statusFilter" size="small" @change="onFilter">
              <el-radio-button value="">全部</el-radio-button>
              <el-radio-button value="pending">待支付</el-radio-button>
              <el-radio-button value="paid">已支付</el-radio-button>
              <el-radio-button value="cancelled">已取消</el-radio-button>
            </el-radio-group>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table v-if="(data || []).length" :data="data" stripe>
            <el-table-column prop="orderNo" label="订单号" min-width="180" />
            <el-table-column prop="kindLabel" label="类型" width="110" />
            <el-table-column label="金额" width="110">
              <template #default="{ row }">¥{{ Number(row.amount).toFixed(2) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <el-tag size="small" :type="row.status === 'paid' ? 'success' : row.status === 'pending' ? 'warning' : 'info'">
                  {{ ({ paid: '已支付', pending: '待支付', cancelled: '已取消' } as any)[row.status] || row.status }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="创建时间" width="170">
              <template #default="{ row }">{{ fmt(row.createdAt) }}</template>
            </el-table-column>
            <el-table-column label="支付时间" width="170">
              <template #default="{ row }">{{ row.paidAt ? fmt(row.paidAt) : '—' }}</template>
            </el-table-column>
          </el-table>
<el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
          <el-empty v-else description="暂无账单" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
