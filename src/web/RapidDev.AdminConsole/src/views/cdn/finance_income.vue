<script setup lang="ts">
/** 统计报表（A2）—— 月收入汇总卡 + 近 12 月收入柱状 + 订单类型分布（/admin/finance/income-summary） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const loadData = async () => {
  const r = (await cdnApi.get('/admin/finance/income-summary')) as any
  return r?.data ?? r
}

const money = (cents: any) => `¥${(Number(cents || 0) / 100).toFixed(2)}`
const pieBg = (byKind: any[]) => {
  const colors = ['#409EFF', '#67C23A', '#E6A23C', '#F56C6C', '#909399']
  const total = byKind.reduce((a, b) => a + Number(b.cents || 0), 0) || 1
  let acc = 0
  const segs: string[] = []
  byKind.forEach((k, i) => {
    const from = (acc / total) * 360
    acc += Number(k.cents || 0)
    const to = (acc / total) * 360
    segs.push(`${colors[i % colors.length]} ${from}deg ${to}deg`)
  })
  return segs.length ? `conic-gradient(${segs.join(', ')})` : '#e4e7ed'
}
const kindLabel = (k: string) => ({ recharge: '余额充值', plan: '购买套餐', flow_package: '购买流量包' } as any)[k] || k
const kindColor = [' #409EFF', '#67C23A', '#E6A23C', '#F56C6C', '#909399']

// 近 12 月柱状（CSS 柱，最大值归一）
function barPct(cents: any, data: any) {
  const max = Math.max(1, ...((data?.months || []) as any[]).map((m) => Number(m.cents)))
  return Math.round((Number(cents || 0) / max) * 100)
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">统计报表</h2><div class="text-muted">财务管理 · 月收入汇总与订单分布</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data, refresh }">
        <!-- 汇总卡 -->
        <el-row :gutter="16">
          <el-col :span="8">
            <el-card>
              <div class="subheader">本月收入</div>
              <div class="h1 mb-0 text-primary">{{ money(data?.monthCents) }}</div>
              <div class="small mt-2">待支付订单 {{ data?.pendingOrders ?? 0 }} 笔 · 累计 {{ data?.totalOrders ?? 0 }} 笔</div>
            </el-card>
          </el-col>
          <el-col :span="8">
            <el-card>
              <div class="subheader">上月收入</div>
              <div class="h2 mb-0">{{ money(data?.lastMonthCents) }}</div>
              <el-tag class="mt-2" size="small" :type="(data?.growth ?? 0) >= 0 ? 'success' : 'danger'">
                环比 {{ (data?.growth ?? 0) >= 0 ? '+' : '' }}{{ data?.growth ?? 0 }}%
              </el-tag>
            </el-card>
          </el-col>
          <el-col :span="8">
            <el-card>
              <div class="subheader">订单类型分布</div>
              <div class="d-flex align-items-center mt-2">
                <div class="me-3" style="width: 84px; height: 84px; border-radius: 50%; position: relative; flex: none"
                  :style="{ background: pieBg(data?.byKind || []) }">
                  <div style="position: absolute; inset: 24px; background: var(--el-bg-color); border-radius: 50%"></div>
                </div>
                <div class="small" style="line-height: 1.9">
                  <div v-for="(k, i) in data?.byKind || []" :key="k.kind">
                    <span :style="{ color: kindColor[i % kindColor.length] }">●</span>
                    {{ kindLabel(k.kind) }}：{{ money(k.cents) }}（{{ k.count }} 笔）
                  </div>
                  <div v-if="!(data?.byKind || []).length" class="text-muted">暂无订单</div>
                </div>
              </div>
            </el-card>
          </el-col>
        </el-row>

        <!-- 近 12 月收入柱状 -->
        <el-card class="mt-3">
          <template #header><div class="d-flex justify-content-between align-items-center"><b>近 12 个月收入</b>
            <el-button size="small" @click="refresh()">刷新</el-button></div></template>
          <div class="d-flex align-items-end gap-2" style="height: 180px; padding-top: 8px">
            <div v-for="m in data?.months || []" :key="m.month" class="flex-fill text-center" style="min-width: 0">
              <div class="small mb-1" :title="money(m.cents)">{{ Number(m.cents) > 0 ? money(m.cents) : '' }}</div>
              <div style="background: #409EFF; border-radius: 3px 3px 0 0; margin: 0 auto; width: 60%; min-height: 2px"
                :style="{ height: Math.max(2, barPct(m.cents, data)) + 'px' }"></div>
              <div class="text-muted" style="font-size: 11px; margin-top: 4px">{{ m.month.slice(5) }}</div>
            </div>
          </div>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
