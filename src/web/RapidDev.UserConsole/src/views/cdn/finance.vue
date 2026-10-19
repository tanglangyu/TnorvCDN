<script setup lang="ts">
/** 财务概览（U5）—— 余额大数字卡 + 本月消费/充值 + 本月用量进度环 + 近 6 笔订单（全部走 /user/finance/summary） */
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const loadData = async () => {
  const r = (await cdnApi.get('/user/finance/summary')) as any
  return r?.data ?? r
}

const money = (v: any) => `¥${Number(v ?? 0).toFixed(2)}`
const fmtTime = (t: string) => (t ? new Date(t).toLocaleString() : '-')
const statusTag = (s: string) => ({ paid: 'success', pending: 'warning', cancelled: 'info' } as any)[s] || 'info'
const statusLabel = (s: string) => ({ paid: '已支付', pending: '待支付', cancelled: '已取消' } as any)[s] || s
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">概览</h2><div class="text-muted">财务管理 · 余额 · 本月消费 · 用量</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data, refresh }">
        <el-row :gutter="16">
          <!-- 余额大数字卡 -->
          <el-col :span="8">
            <el-card class="h-100">
              <div class="subheader">账户余额</div>
              <div class="d-flex align-items-center mt-2">
                <div class="h1 mb-0 text-primary">{{ money(data?.balance) }}</div>
                <el-button type="primary" size="small" class="ms-3" @click="$router.push('/finance/charge')">充值</el-button>
              </div>
              <div class="text-muted small mt-2">充值后可直接余额支付购买套餐与流量包</div>
            </el-card>
          </el-col>
          <!-- 本月消费 / 充值 -->
          <el-col :span="8">
            <el-card class="h-100">
              <div class="subheader">本月消费 <el-tag v-if="data?.spendGrowth != null" size="small" class="ms-1" :type="data.spendGrowth >= 0 ? 'danger' : 'success'">{{ data.spendGrowth >= 0 ? '+' : '' }}{{ data.spendGrowth }}%</el-tag></div>
              <div class="h2 mb-0 mt-2">{{ money(data?.monthSpend) }}</div>
              <el-divider class="my-2" />
              <div class="d-flex justify-content-between small">
                <span class="text-muted">本月充值</span><span>{{ money(data?.monthRecharge) }}</span>
              </div>
            </el-card>
          </el-col>
          <!-- 本月用量进度环 -->
          <el-col :span="8">
            <el-card class="h-100">
              <div class="d-flex align-items-center">
                <el-progress type="dashboard" :percentage="Math.min(100, Number(data?.usage?.trafficPercent ?? 0))"
                  :color="Number(data?.usage?.trafficPercent ?? 0) > 90 ? '#F56C6C' : Number(data?.usage?.trafficPercent ?? 0) > 70 ? '#E6A23C' : '#409EFF'"
                  :width="92" :stroke-width="9">
                  <template #default>
                    <div class="text-center">
                      <div class="h3 mb-0">{{ data?.usage?.trafficPercent ?? 0 }}%</div>
                      <div class="text-muted" style="font-size: 11px">月流量</div>
                    </div>
                  </template>
                </el-progress>
                <div class="flex-fill ms-3">
                  <div class="font-weight-medium" v-if="data?.usage?.planName">{{ data.usage.planName }}</div>
                  <div class="text-muted small">{{ data?.usage?.trafficMB ?? 0 }} MB / {{ data?.usage?.planTrafficMB || '不限' }} MB</div>
                  <div class="text-muted small mt-1">本月请求 {{ (data?.usage?.requests ?? 0).toLocaleString() }} 次</div>
                  <div v-if="Number(data?.usage?.trafficPercent ?? 0) > 90" class="text-danger small mt-1">
                    <i class="ti ti-alert-triangle me-1"></i>流量即将超出套餐，请及时升级或购买流量包
                  </div>
                </div>
              </div>
            </el-card>
          </el-col>
        </el-row>

        <!-- 近 6 笔订单 -->
        <el-card class="mt-3">
          <template #header>
            <div class="d-flex justify-content-between align-items-center">
              <span>最近订单</span>
              <el-button size="small" text type="primary" @click="$router.push('/finance/bills')">查看全部</el-button>
            </div>
          </template>
          <el-table v-if="(data?.recentOrders || []).length" :data="data.recentOrders" size="small" stripe>
            <el-table-column prop="orderNo" label="订单号" min-width="180" />
            <el-table-column prop="kindLabel" label="类型" width="110" />
            <el-table-column label="金额" width="110"><template #default="{ row }">{{ money(row.amount) }}</template></el-table-column>
            <el-table-column label="状态" width="90"><template #default="{ row }"><el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag></template></el-table-column>
            <el-table-column label="创建时间" min-width="160"><template #default="{ row }">{{ fmtTime(row.createdAt) }}</template></el-table-column>
            <el-table-column label="支付时间" min-width="160"><template #default="{ row }">{{ fmtTime(row.paidAt) }}</template></el-table-column>
          </el-table>
          <el-empty v-else description="暂无订单" :image-size="60" />
          <div class="text-muted small mt-2">数据每次进入页面自动刷新 <el-button size="small" text type="primary" @click="refresh">手动刷新</el-button></div>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
