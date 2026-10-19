<script setup lang="ts">
/**
 * 套餐管理（U7）—— 套餐卡对比 + 购买流程弹窗化（选月数→用券→确认金额→余额支付）+ 费用计算器
 */
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const router = useRouter()
const gate = ref()
const rows = ref<any[]>([])
const currentPlanId = ref(0)

const loadData = async () => {
  const r = (await cdnApi.get('/user/plans')) as any
  rows.value = r?.data ?? r ?? []
  // 当前订阅：从 /user/subscriptions 读（含自动续费状态）
  try {
    const sr = (await cdnApi.get('/user/subscriptions')) as any
    const subs = sr?.data ?? sr ?? []
    const active = subs.filter((x: any) => x.enabled && new Date(x.expiresAt) > new Date())
    if (active.length) {
      currentPlanId.value = active[0].planId
      subs_.value = active
      currentSub.value = active[0]
    } else {
      subs_.value = []
      currentSub.value = null
    }
  } catch { /* 无订阅时忽略 */ }
  return rows.value
}

const subs_ = ref<any[]>([])
const currentSub = ref<any>(null)
const renewing = ref(false)

// 开/关自动续费（关闭 = 退订：到期自然失效）
async function toggleAutoRenew() {
  const sub = currentSub.value
  if (!sub) return
  const target = !sub.autoRenew
  const action = target ? '开启' : '关闭'
  if (!target) {
    try { await ElMessageBox.confirm('关闭自动续费后，套餐到期将自然失效且网站会停服。确认退订？', '退订确认', { type: 'warning' }) } catch { return }
  }
  renewing.value = true
  try {
    const r = (await cdnApi.post(`/user/subscriptions/${sub.id}/auto-renew`, { autoRenew: target })) as any
    ElMessage.success(r?.message || `已${action}自动续费`)
    await loadData()
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '操作失败') } finally { renewing.value = false }
}

// ---- 升级套餐 ----
const upDialog = ref(false)
const upTarget = ref<any>(null)
const upDiffCents = ref(0)
const upExtDays = ref(0)
const upgrading = ref(false)

function openUpgrade(p: any) {
  const cur = rows.value.find((x: any) => x.id === currentPlanId.value)
  if (!currentSub.value) { ElMessage.warning('您还没有生效中的套餐，请先购买'); return }
  const now = Date.now()
  const remainDays = Math.max(0, (new Date(currentSub.value.expiresAt).getTime() - now) / 86400000)
  const diff = Math.round((Number(p.priceCents) - Number(cur?.priceCents || 0)) * remainDays / 30)
  upTarget.value = p
  upDiffCents.value = Math.max(0, diff)
  upExtDays.value = diff < 0 ? Math.round(-diff / (Number(p.priceCents) || 1) * 30) : 0
  upDialog.value = true
}

async function doUpgrade() {
  if (!upTarget.value || !currentSub.value) return
  upgrading.value = true
  try {
    const r = (await cdnApi.post('/user/subscriptions/upgrade', {
      subscriptionId: currentSub.value.id, planId: upTarget.value.id,
    })) as any
    const d = r?.data ?? r
    ElMessage.success(d?.message || r?.message || '升级成功')
    upDialog.value = false
    await loadData()
  } catch (e: any) {
    const msg = e?.response?.data?.message || '升级失败'
    if (String(msg).includes('余额不足')) {
      ElMessageBox.confirm('余额不足，是否前往充值？', '余额不足', { type: 'warning', confirmButtonText: '去充值', cancelButtonText: '取消' })
        .then(() => router.push('/finance/charge')).catch(() => {})
    } else ElMessage.error(msg)
  } finally { upgrading.value = false }
}

const money = (cents: any) => `¥${(Number(cents || 0) / 100).toFixed(2)}`

// ---- 购买流程弹窗 ----
const buyDialog = ref(false)
const buyPlan = ref<any>(null)
const buyStep = ref(1) // 1 选月数+券 → 2 确认支付
const buyMonths = ref(1)
const buyCoupon = ref('')
const buyCouponInfo = ref<{ code: string; discountPercent: number } | null>(null)
const buyPayMethod = ref('balance')
const buying = ref(false)

const baseAmountCents = computed(() => (buyPlan.value ? Math.round(Number(buyPlan.value.priceCents || 0)) * buyMonths.value : 0))
const finalAmountCents = computed(() =>
  buyCouponInfo.value ? Math.max(1, Math.round(baseAmountCents.value * buyCouponInfo.value.discountPercent / 100)) : baseAmountCents.value)
const discountRows = computed(() => {
  const list: { label: string; value: string }[] = []
  if (buyCouponInfo.value) list.push({ label: `优惠券 ${buyCouponInfo.value.code}（${buyCouponInfo.value.discountPercent}%）`, value: `-¥${((baseAmountCents.value - finalAmountCents.value) / 100).toFixed(2)}` })
  return list
})

function openBuy(p: any) {
  buyPlan.value = p
  buyStep.value = 1
  buyMonths.value = 1
  buyCoupon.value = ''
  buyCouponInfo.value = null
  buyPayMethod.value = 'balance'
  buyDialog.value = true
}

async function checkCoupon() {
  if (!buyCoupon.value.trim()) { buyCouponInfo.value = null; return }
  // 用券校验放到订单创建时（后端为准），这里仅预览标记
  buyCouponInfo.value = { code: buyCoupon.value.trim(), discountPercent: 0 }
  ElMessage.info('优惠券将在提交订单时由服务端校验')
}

async function submitOrder() {
  if (!buyPlan.value) return
  buying.value = true
  try {
    const r = (await cdnApi.post('/user/orders', {
      kind: 'plan',
      planId: buyPlan.value.id,
      months: buyMonths.value,
      amountCents: 0,
      payMethod: buyPayMethod.value,
      couponCode: buyCoupon.value.trim() || null,
    })) as any
    const d = r?.data ?? r
    if (d?.status === 'paid') {
      ElMessage.success(`支付成功！已开通/续期 ${buyPlan.value.name} ${buyMonths.value} 个月`)
      buyDialog.value = false
      gate.value?.refresh?.()
    } else if (d?.payUrl) {
      ElMessage.success('订单已创建，即将跳转支付页面')
      buyDialog.value = false
      router.push(d.payUrl.replace(/^\/user/, '/user'))
    } else {
      ElMessage.warning(d?.message || '订单状态未知，请到「费用账单」查看')
      buyDialog.value = false
    }
  } catch (e: any) {
    const msg = e?.response?.data?.message || e?.message || '下单失败'
    if (String(msg).includes('余额不足')) {
      ElMessageBox.confirm('余额不足，是否前往充值？', '余额不足', { type: 'warning', confirmButtonText: '去充值', cancelButtonText: '取消' })
        .then(() => router.push('/finance/charge'))
        .catch(() => {})
    } else {
      ElMessage.error(msg)
    }
  } finally { buying.value = false }
}

// ---- 费用计算器 ----
const calcGb = ref(100)
const calcRequests = ref(1000000)
const calcLoading = ref(false)
const calcResult = ref<any>(null)
const doCalc = async () => {
  calcLoading.value = true
  try {
    const r = (await cdnApi.get('/user/fee/calculator', { gb: calcGb.value, requests: calcRequests.value })) as any
    calcResult.value = r?.data ?? r
  } catch {
    ElMessage.error('计算失败')
  } finally {
    calcLoading.value = false
  }
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">套餐管理</h2><div class="text-muted">可购套餐对比与在线购买（余额支付，全程不跳页）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card v-if="currentSub" class="mb-3" style="max-width: 900px">
          <div class="d-flex align-items-center flex-wrap" style="gap: 12px">
            <el-tag type="success">当前订阅：{{ currentSub.planName }}</el-tag>
            <span class="small">到期：<b>{{ new Date(currentSub.expiresAt).toLocaleDateString() }}</b>
              <span v-if="new Date(currentSub.expiresAt) > new Date()" class="text-muted ms-1">（剩余 {{ Math.max(0, Math.ceil((new Date(currentSub.expiresAt).getTime() - Date.now()) / 86400000)) }} 天）</span></span>
            <el-switch v-model="currentSub.autoRenew" :loading="renewing"
              inline-prompt active-text="自动续费" inactive-text="到期失效"
              @change="toggleAutoRenew" />
            <el-button size="small" plain @click="refresh()">刷新</el-button>
          </div>
          <div class="text-muted small mt-1">开启自动续费：到期时从余额自动扣一个月月价续期；关闭即退订（到期自然失效）。</div>
        </el-card>
        <el-row :gutter="16">
          <el-col :xs="24" :sm="12" :lg="8" v-for="p in rows" :key="p.id" class="mb-3">
            <el-card shadow="hover" :class="{ 'border-primary': p.id === currentPlanId }" style="position: relative">
              <el-tag v-if="p.id === currentPlanId" type="success" size="small" style="position: absolute; top: 12px; right: 12px">当前套餐</el-tag>
              <div class="text-center py-2">
                <h3 class="mb-1">{{ p.name }}</h3>
                <div><span style="font-size: 28px; color: #409EFF; font-weight: 600">{{ money(p.priceCents) }}</span>
                  <span class="text-muted">/月</span></div>
                <el-button type="primary" class="mt-3" :disabled="!p.enabled" @click="openBuy(p)">
                  {{ p.enabled ? (p.id === currentPlanId ? '续费' : (currentPlanId ? '升级到此套餐' : '立即购买')) : '已下架' }}
                </el-button>
                <el-button v-if="p.id !== currentPlanId && currentPlanId" class="mt-3" plain @click="openUpgrade(p)">按剩余天数升级</el-button>
              </div>
              <el-divider class="my-3" />
              <div class="small" style="line-height: 2">
                <div><i class="ti ti-check text-success me-2"></i>网站数上限：<b>{{ p.maxServers }}</b> 个</div>
                <div><i class="ti ti-check text-success me-2"></i>域名数上限：<b>{{ p.maxDomains }}</b> 个</div>
                <div><i class="ti ti-check text-success me-2"></i>月流量：<b>{{ p.monthlyTrafficMB >= 1024 ? (p.monthlyTrafficMB / 1024).toFixed(0) + ' GB' : p.monthlyTrafficMB + ' MB' }}</b></div>
              </div>
            </el-card>
          </el-col>
        </el-row>
        <el-empty v-if="!rows.length" description="暂无可购套餐" />

        <!-- 购买流程弹窗 -->
        <el-dialog v-model="buyDialog" :title="buyPlan ? `购买 ${buyPlan.name}` : '购买套餐'" width="480px">
          <div v-if="buyStep === 1">
            <el-form label-width="90px">
              <el-form-item label="购买时长">
                <el-radio-group v-model="buyMonths">
                  <el-radio-button :value="1">1 个月</el-radio-button>
                  <el-radio-button :value="3">3 个月</el-radio-button>
                  <el-radio-button :value="6">6 个月</el-radio-button>
                  <el-radio-button :value="12">12 个月</el-radio-button>
                </el-radio-group>
              </el-form-item>
              <el-form-item label="优惠券">
                <div class="d-flex gap-2" style="width: 100%">
                  <el-input v-model="buyCoupon" placeholder="输入优惠码（可选）" clearable @change="checkCoupon" />
                </div>
              </el-form-item>
              <el-form-item label="应付金额">
                <div style="font-size: 22px; color: #F56C6C; font-weight: 600">{{ money(baseAmountCents) }}</div>
                <div class="text-muted small">{{ money(buyPlan?.priceCents) }} × {{ buyMonths }} 个月</div>
              </el-form-item>
            </el-form>
          </div>
          <div v-else>
            <el-descriptions :column="1" border>
              <el-descriptions-item label="套餐">{{ buyPlan?.name }}</el-descriptions-item>
              <el-descriptions-item label="时长">{{ buyMonths }} 个月</el-descriptions-item>
              <el-descriptions-item v-for="(d, i) in discountRows" :key="i" :label="d.label">
                <span class="text-success">{{ d.value }}</span>
              </el-descriptions-item>
              <el-descriptions-item label="支付方式">
                <el-radio-group v-model="buyPayMethod">
                  <el-radio value="balance">余额支付</el-radio>
                  <el-radio value="alipay">支付宝</el-radio>
                  <el-radio value="wechat">微信</el-radio>
                </el-radio-group>
              </el-descriptions-item>
              <el-descriptions-item label="实付金额">
                <b style="font-size: 20px; color: #F56C6C">{{ money(finalAmountCents) }}</b>
              </el-descriptions-item>
            </el-descriptions>
          </div>
          <template #footer>
            <el-button v-if="buyStep === 1" @click="buyDialog = false">取消</el-button>
            <el-button v-if="buyStep === 1" type="primary" @click="buyStep = 2">下一步</el-button>
            <el-button v-if="buyStep === 2" @click="buyStep = 1">上一步</el-button>
            <el-button v-if="buyStep === 2" type="primary" :loading="buying" @click="submitOrder">确认支付</el-button>
          </template>
        </el-dialog>

        <!-- 费用计算器 -->
        <el-card class="mt-3" style="max-width: 720px">
          <template #header><b>费用计算器</b><span class="text-muted ms-2 small">按流量与请求数估算月度费用</span></template>
          <el-form label-width="130px">
            <el-form-item label="预计月流量（GB）">
              <el-input-number v-model="calcGb" :min="1" :max="1000000" controls-position="right" />
            </el-form-item>
            <el-form-item label="预计月请求（次）">
              <el-input-number v-model="calcRequests" :min="1000" :max="10000000000" :step="10000" controls-position="right" />
            </el-form-item>
            <el-form-item>
              <el-button type="primary" :loading="calcLoading" @click="doCalc">估算</el-button>
            </el-form-item>
          </el-form>
          <el-descriptions v-if="calcResult" :column="2" border>
            <el-descriptions-item label="流量单价">¥{{ calcResult.pricePerGB }}/GB</el-descriptions-item>
            <el-descriptions-item label="请求单价">¥{{ calcResult.pricePer10kReq }}/万次</el-descriptions-item>
            <el-descriptions-item label="流量费用">¥{{ calcResult.estimate.trafficCost }}</el-descriptions-item>
            <el-descriptions-item label="请求费用">¥{{ calcResult.estimate.requestCost }}</el-descriptions-item>
            <el-descriptions-item label="预估月费用" :span="2">
              <b class="text-primary" style="font-size: 18px">¥{{ calcResult.estimate.total }}</b>
              <el-tag v-if="calcResult.hasPlan" type="success" size="small" class="ms-2">按当前套餐折算单价</el-tag>
              <el-tag v-else type="info" size="small" class="ms-2">平台默认单价</el-tag>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
