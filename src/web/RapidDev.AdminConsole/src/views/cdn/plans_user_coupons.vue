<script setup lang="ts">
/** 已领优惠券 —— 优惠券发放与领取情况（对接 /admin/coupons） */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = rows.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})
const rows = ref<any[]>([])

const loadData = async () => {
  const data = await cdnApi.get('/admin/coupons')
  rows.value = Array.isArray(data) ? data : (data?.data || [])
  return rows.value
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">已领优惠券</h2><div class="text-muted">套餐管理 · 优惠券使用情况</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default>
        <el-card>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="code" label="兑换码" min-width="160" />
            <el-table-column label="折扣" width="90">
              <template #default="{ row }">{{ row.discountPercent }}%</template>
            </el-table-column>
            <el-table-column label="限用/已用" width="110">
              <template #default="{ row }">{{ row.maxUses }} / {{ row.used }}</template>
            </el-table-column>
            <el-table-column label="到期时间" width="165">
              <template #default="{ row }">{{ row.expireAt ? new Date(row.expireAt).toLocaleString() : '—' }}</template>
            </el-table-column>
            <el-table-column label="启用" width="80">
              <template #default="{ row }">
                <el-tag size="small" :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '是' : '否' }}</el-tag>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
