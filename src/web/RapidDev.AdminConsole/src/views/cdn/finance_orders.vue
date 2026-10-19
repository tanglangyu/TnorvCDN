<script setup lang="ts">
/** 订单管理 —— 对接 TnorvCDN 主控 /admin/orders（充值/套餐/流量包订单） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const statusFilter = ref('')
const detail = ref<any>(null)
const drawer = ref(false)
const openDetail = (row: any) => { detail.value = row; drawer.value = true }
const detailItems = (d: any) => Object.entries({
  订单号: d?.orderNo, 用户ID: d?.userId, 类型: kindLabel(d?.kind || ''),
  金额: '¥' + ((d?.amountCents || 0) / 100).toFixed(2), 支付方式: methodLabel(d?.payMethod || ''),
  状态: statusLabel(d?.status || ''), 优惠券: d?.couponCode || '—',
  创建时间: d?.createdAt ? new Date(d.createdAt).toLocaleString() : '—',
  支付时间: d?.paidAt ? new Date(d.paidAt).toLocaleString() : '—',
  备注: d?.note || '—',
}).map(([k, v]) => ({ k, v }))

const kindLabel = (k: string) =>
  ({ recharge: '余额充值', plan: '购买套餐', flow_package: '流量包' } as any)[k] || k
const statusLabel = (s: string) =>
  ({ pending: '待支付', paid: '已支付', cancelled: '已取消' } as any)[s] || s
const statusTag = (s: string) =>
  ({ pending: 'warning', paid: 'success', cancelled: 'info' } as any)[s] || 'info'
const methodLabel = (m: string) =>
  ({ balance: '余额', alipay: '支付宝', wechat: '微信' } as any)[m] || m

// 分页 + 关键词筛选（后端 /admin/orders 支持 page/pageSize/q/status）
const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const params: any = {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
  }
  if (statusFilter.value) params.status = statusFilter.value
  if (filterQ.value) params.q = filterQ.value
  const data = await cdnApi.get('/admin/orders', params)
  const items = Array.isArray(data) ? data : (data?.items || data?.data || [])
  pager.value.total = Array.isArray(data) ? items.length : (data?.total ?? items.length)
  rows.value = items
  return rows.value
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

async function cancel(row: any) {
  await ElMessageBox.confirm(`确认取消订单 ${row.orderNo}？`, '取消订单', {
    confirmButtonText: '确认取消', cancelButtonText: '返回',
  })
  try {
    await cdnApi.post(`/admin/orders/${row.orderNo}/cancel`)
    ElMessage.success('已取消')
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || e?.message || '取消失败')
  }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">订单管理</h2><div class="text-muted">财务管理 · 充值/套餐/流量包订单</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default>
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="单号/券码关键词" size="small" clearable style="width: 220px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
            <el-radio-group v-model="statusFilter" size="small" @change="() => { pager.page = 1; gate?.refresh?.() }">
              <el-radio-button value="">全部</el-radio-button>
              <el-radio-button value="pending">待支付</el-radio-button>
              <el-radio-button value="paid">已支付</el-radio-button>
              <el-radio-button value="cancelled">已取消</el-radio-button>
            </el-radio-group>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column prop="orderNo" label="订单号" min-width="190" />
            <el-table-column prop="userId" label="用户" width="70" />
            <el-table-column label="类型" width="100">
              <template #default="{ row }">{{ kindLabel(row.kind) }}</template>
            </el-table-column>
            <el-table-column label="金额" width="100">
              <template #default="{ row }">¥{{ ((row.amountCents || 0) / 100).toFixed(2) }}</template>
            </el-table-column>
            <el-table-column label="支付方式" width="90">
              <template #default="{ row }">{{ methodLabel(row.payMethod) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="优惠券" width="90">
              <template #default="{ row }">{{ row.couponCode || '—' }}</template>
            </el-table-column>
            <el-table-column label="创建时间" width="165">
              <template #default="{ row }">{{ row.createdAt ? new Date(row.createdAt).toLocaleString() : '—' }}</template>
            </el-table-column>
            <el-table-column label="支付时间" width="165">
              <template #default="{ row }">{{ row.paidAt ? new Date(row.paidAt).toLocaleString() : '—' }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
                  <el-button size="small" @click="openDetail(row)">详情</el-button>
                  <el-button v-if="row.status === 'pending'" size="small" type="danger" plain @click="cancel(row)">取消</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

        <el-drawer v-model="drawer" title="订单详情" size="380px">
          <el-descriptions v-if="detail" :column="1" border>
            <el-descriptions-item v-for="it in detailItems(detail)" :key="it.k" :label="it.k">{{ it.v }}</el-descriptions-item>
          </el-descriptions>
        </el-drawer>
      </template>
    </CdnGate>
  </div>
</template>
