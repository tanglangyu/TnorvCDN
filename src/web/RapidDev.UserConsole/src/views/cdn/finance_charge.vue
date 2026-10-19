<script setup lang="ts">
/** 充值 —— 余额充值 / 购买套餐 / 购买流量包（余额直付或扫码模拟网关） */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const account = ref<any>(null)
const plans = ref<any[]>([])
const packages = ref<any[]>([])

const loadData = async () => {
  const acc = await cdnApi.get('/user/account')
  account.value = acc && acc.data ? acc.data : acc
  try { const pl = await cdnApi.get('/user/plans'); plans.value = Array.isArray(pl) ? pl : (Array.isArray(pl?.data) ? pl.data : (Array.isArray(pl?.data?.data) ? pl.data.data : [])) } catch { plans.value = [] }
  try { const pk = await cdnApi.get('/user/flow-packages'); packages.value = Array.isArray(pk) ? pk : (pk?.data || []) } catch { packages.value = [] }
  return { account, plans, packages }
}

const amount = ref<number>(100)
const coupon = ref('')
const paying = ref(false)

const payWithGateway = async (kind: string, payload: any) => {
  const res: any = await cdnApi.post('/user/orders', { ...payload, couponCode: coupon.value || null })
  if (!res || !res.orderNo) { ElMessage.success('下单成功'); await loadData(); return }
  if (res.status === 'paid') {
    ElMessage.success('支付成功')
  } else if (res.payUrl) {
    await ElMessageBox.confirm(
      `订单 ${res.orderNo}，金额 ¥${(res.amountCents / 100).toFixed(2)}，已生成${payload.payMethod === 'alipay' ? '支付宝' : '微信'}扫码订单（演示网关）。是否立即完成支付？`,
      '扫码支付', { confirmButtonText: '完成支付', cancelButtonText: '稍后支付' })
    await cdnApi.post(`/user/orders/${res.orderNo}/pay-callback`)
    ElMessage.success('支付成功')
  }
  await loadData()
}

const doRecharge = async (method: string) => {
  paying.value = true
  try { await payWithGateway('recharge', { kind: 'recharge', amountCents: Math.round(amount.value * 100), payMethod: method }) }
  catch (e: any) { ElMessage.error(e?.message || '下单失败') }
  paying.value = false
}

const buyPlan = async (row: any, method: string) => {
  paying.value = true
  try { await payWithGateway('plan', { kind: 'plan', planId: row.id, months: 1, payMethod: method }) }
  catch (e: any) { ElMessage.error(e?.message || '购买失败') }
  paying.value = false
}

const buyPackage = async (row: any, method: string) => {
  paying.value = true
  try { await payWithGateway('flow_package', { kind: 'flow_package', flowPackageId: row.id, payMethod: method }) }
  catch (e: any) { ElMessage.error(e?.message || '购买失败') }
  paying.value = false
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">充值</h2><div class="text-muted">财务管理 · 余额/购买套餐/购买流量包</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-row :gutter="16" class="mb-4">
          <el-col :span="8">
            <el-card shadow="never">
              <div class="text-muted" style="font-size:13px">账户余额</div>
              <div style="font-size:28px;font-weight:600">¥{{ ((data.account?.balanceCents ?? 0) as number / 100).toFixed(2) }}</div>
            </el-card>
          </el-col>
          <el-col :span="16">
            <el-card shadow="never">
              <div class="mb-2" style="font-weight:600">余额充值</div>
              <el-space wrap>
                <el-input-number v-model="amount" :min="1" :max="100000" :step="100" />
                <el-input v-model="coupon" placeholder="优惠券码（选填）" style="width:150px" clearable />
                <el-button type="primary" :loading="paying" @click="doRecharge('alipay')">支付宝充值</el-button>
                <el-button type="success" :loading="paying" @click="doRecharge('wechat')">微信充值</el-button>
              </el-space>
            </el-card>
          </el-col>
        </el-row>
        <el-card class="mb-4" shadow="never">
          <div class="mb-2" style="font-weight:600">购买套餐（余额或扫码支付）</div>
          <el-table :data="data.plans || []" stripe>
            <el-table-column prop="name" label="套餐" min-width="120" />
            <el-table-column label="价格" width="110">
              <template #default="{ row }">¥{{ (row.priceCents / 100).toFixed(2) }}/月</template>
            </el-table-column>
            <el-table-column label="流量" width="110">
              <template #default="{ row }">{{ row.monthlyTrafficMB >= 1024 ? (row.monthlyTrafficMB / 1024).toFixed(0) + ' GB' : row.monthlyTrafficMB + ' MB' }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" type="primary" plain :disabled="paying" @click="buyPlan(row, 'balance')">余额购买</el-button>
              <el-button size="small" plain :disabled="paying" @click="buyPlan(row, 'alipay')">扫码</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
        <el-card v-if="(data.packages || []).length" shadow="never">
          <div class="mb-2" style="font-weight:600">购买流量包</div>
          <el-table :data="data.packages" stripe>
            <el-table-column prop="name" label="流量包" min-width="120" />
            <el-table-column label="流量" width="140">
              <template #default="{ row }">{{ (row.bytes / 1073741824).toFixed(0) }} GB</template>
            </el-table-column>
            <el-table-column label="价格" width="110">
              <template #default="{ row }">¥{{ (row.priceCents / 100).toFixed(2) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="220">
              <template #default="{ row }">
                <el-button size="small" type="primary" plain :disabled="paying" @click="buyPackage(row, 'balance')">余额购买</el-button>
                <el-button size="small" plain :disabled="paying" @click="buyPackage(row, 'alipay')">扫码</el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
