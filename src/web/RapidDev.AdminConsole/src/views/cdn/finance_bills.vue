<script setup lang="ts">
/** 费用账单（A2）—— 月结账单（用户×月份×流量/请求/超额金额）+ 月份筛选 + 导出 CSV */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const month = ref(new Date().toISOString().slice(0, 7))
const pager = ref({ page: 1, pageSize: 50, total: 0 })

const loadData = async () => {
  const r = (await cdnApi.get('/admin/finance/bills', { month: month.value, page: pager.value.page, pageSize: pager.value.pageSize })) as any
  const d = r?.data ?? r
  if (d && !Array.isArray(d) && 'total' in d) {
    pager.value.total = d.total
    rows.value = d.items || []
  } else {
    rows.value = Array.isArray(d) ? d : []
    pager.value.total = rows.value.length
  }
  return rows.value
}

// 合计行 (merge: 原 Options API methods 块, 转为 setup 函数)
function summary(param: any) {
  const { columns, data } = param
  return columns.map((col: any, i: number) => {
    if (i === 0) return '合计'
    if (col.property === 'requests') return data.reduce((a: number, r: any) => a + Number(r.requests || 0), 0).toLocaleString()
    if (col.label === '月流量') return (data.reduce((a: number, r: any) => a + Number(r.trafficMB || 0), 0) / 1024).toFixed(2) + ' GB'
    if (col.label === '超额费用') return '¥' + (data.reduce((a: number, r: any) => a + Number(r.overCents || 0), 0) / 100).toFixed(2)
    return ''
  })
}
const money = (cents: any) => `¥${(Number(cents || 0) / 100).toFixed(2)}`
const gb = (mb: any) => (Number(mb || 0) / 1024).toFixed(2) + ' GB'

function exportCsv() {
  const head = ['用户ID', '账号', '月份', '套餐', '月流量(MB)', '月流量(GB)', '请求数', '套餐流量(MB)', '超额(MB)', '超额费用(元)', '余额(元)']
  const lines = rows.value.map((r: any) => [r.userId, r.username, r.month, r.planName || '无套餐', r.trafficMB, (r.trafficMB / 1024).toFixed(2), r.requests, r.planTrafficMB, r.overMB, (r.overCents / 100).toFixed(2), (r.balanceCents / 100).toFixed(2)])
  const csv = '﻿' + [head, ...lines].map((l) => l.join(',')).join('\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `账单_${month.value}.csv`
  a.click()
  URL.revokeObjectURL(a.href)
  ElMessage.success('已导出 CSV')
}

function pickMonth(d: any) {
  month.value = d
  pager.value.page = 1
  gate.value?.refresh?.()
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">费用账单</h2><div class="text-muted">财务管理 · 月结账单（用户 × 月份 × 流量/请求/金额）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center flex-wrap">
            <el-date-picker v-model="month" type="month" format="YYYY-MM" value-format="YYYY-MM" :clearable="false"
              style="width: 140px" @change="pickMonth" />
            <el-button size="small" type="success" @click="exportCsv" :disabled="!rows.length">导出 CSV</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <span class="text-muted small ms-2">共 {{ pager.total }} 个用户账单</span>
          </div>
          <el-table :data="rows" stripe show-summary :summary-method="summary">
            <el-table-column prop="userId" label="ID" width="70" />
            <el-table-column prop="username" label="账号" min-width="130" />
            <el-table-column prop="month" label="月份" width="90" />
            <el-table-column label="套餐" width="110">
              <template #default="{ row }">{{ row.planName || '无套餐' }}</template>
            </el-table-column>
            <el-table-column label="月流量" width="110">
              <template #default="{ row }">{{ gb(row.trafficMB) }}</template>
            </el-table-column>
            <el-table-column prop="requests" label="请求数" width="120">
              <template #default="{ row }">{{ Number(row.requests || 0).toLocaleString() }}</template>
            </el-table-column>
            <el-table-column label="超额" width="100">
              <template #default="{ row }">
                <el-tag v-if="row.overMB > 0" size="small" type="danger">{{ gb(row.overMB) }}</el-tag>
                <span v-else class="text-muted">—</span>
              </template>
            </el-table-column>
            <el-table-column label="超额费用" width="110">
              <template #default="{ row }">{{ money(row.overCents) }}</template>
            </el-table-column>
            <el-table-column label="账户余额" width="110">
              <template #default="{ row }">{{ money(row.balanceCents) }}</template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }" />
          <el-empty v-if="!rows.length" description="该月份暂无账单数据" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>

