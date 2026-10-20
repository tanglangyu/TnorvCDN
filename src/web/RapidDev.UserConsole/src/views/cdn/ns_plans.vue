<script setup lang="ts">
/** 套餐 —— 域名解析套餐（含购买入口：跳转充值页购买套餐，NS 权益随套餐附带） */
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const router = useRouter()
const pg = ref({ page: 1, size: 20 })
const filterQ = ref('')
const viewRows = (data: any[]) => {
  const start = (pg.value.page - 1) * pg.value.size
  const kw = (filterQ.value || '').trim().toLowerCase()
  const list = kw ? (data || []).filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw)) : (data || [])
  return list.slice(start, start + pg.value.size)
}
const loadData = async () => (await cdnApi.get('/user/ns/plans')) as any

// 当前我的套餐
const myPlan = ref<any>(null)
const loadMyPlan = async () => {
  try {
    const r = (await cdnApi.get('/user/my-plan')) as any
    const d = r?.data ?? r
    myPlan.value = d?.plans?.[0] ?? null
  } catch { /* 静默 */ }
}
loadMyPlan()

// 购买：跳转充值页套餐区（余额或扫码），带确认提示
const buy = async (row: any) => {
  await ElMessageBox.confirm(
    `购买套餐「${row.name}」（¥${(row.priceCents / 100).toFixed(2)}/月）？将跳转到充值页完成支付（余额或扫码），NS 解析权益随套餐附带。`,
    '购买套餐', { confirmButtonText: '去购买', cancelButtonText: '取消', type: 'info' })
  router.push({ path: '/finance/charge', query: { planId: String(row.id) } })
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">套餐</h2><div class="text-muted">域名解析 · NS 托管权益随套餐附带</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-alert v-if="myPlan?.planName" type="success" show-icon :closable="false" class="mb-3"
          :title="`当前套餐：${myPlan.planName}（到期 ${myPlan.expiresAt ? new Date(myPlan.expiresAt).toLocaleDateString() : '-'}）`" />
        <el-card>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
          <el-table :data="viewRows(data)" stripe>
            <el-table-column prop="name" label="套餐" min-width="140" />
            <el-table-column label="价格" width="120">
              <template #default="{ row }">¥{{ (row.priceCents / 100).toFixed(2) }}/月</template>
            </el-table-column>
            <el-table-column label="月流量" width="120">
              <template #default="{ row }">{{ row.monthlyTrafficMB >= 1024 ? (row.monthlyTrafficMB / 1024).toFixed(0) + ' GB' : (row.monthlyTrafficMB || 0) + ' MB' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag v-if="row.name === myPlan?.planName" type="success" size="small">当前</el-tag>
                <el-tag v-else type="info" size="small">可购</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="110">
              <template #default="{ row }">
                <el-button v-if="row.name !== myPlan?.planName" size="small" type="primary" @click="buy(row)">立即购买</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(data || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
          <el-empty v-if="!(data || []).length" description="暂无可购套餐" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
