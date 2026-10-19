<script setup lang="ts">
/** 购买实例 —— 高防套餐列表 + 余额购买（对接 POST /user/anti-ddos/purchase） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const pg = ref({ page: 1, size: 20 })
const filterQ = ref('')
const paying = ref(false)
const viewRows = (data: any[]) => {
  const start = (pg.value.page - 1) * pg.value.size
  const kw = (filterQ.value || '').trim().toLowerCase()
  const list = kw ? (data || []).filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw)) : (data || [])
  return list.slice(start, start + pg.value.size)
}
const loadData = async () => (await cdnApi.get('/user/anti-ddos/packages')) as any

const fmtPrice = (v: any) => (v == null ? '-' : (Number(v) / 100).toFixed(2) + ' 元')
const fmtBytes = (v: any) => {
  const b = Number(v) || 0
  return b >= 1099511627776 ? (b / 1099511627776).toFixed(2) + ' TB' : b >= 1073741824 ? (b / 1073741824).toFixed(0) + ' GB' : (b / 1048576).toFixed(0) + ' MB'
}

const buy = async (row: any) => {
  const { value } = await ElMessageBox.prompt('将使用余额购买该高防方案（购买后立即生效，可在「实例列表」查看）。可选填要防护的域名，多个用英文逗号分隔：', `购买 ${row.name}`, {
    confirmButtonText: '确认购买', cancelButtonText: '取消', inputValue: '',
    inputValidator: () => true,
  }).catch(() => ({ value: null }))
  if (value == null) return
  paying.value = true
  try {
    const res: any = await cdnApi.post('/user/anti-ddos/purchase', { packageId: row.id, targetDomains: (value || '').trim() || null })
    if (res?.code === 200 || res?.code === undefined) ElMessage.success('购买成功，可在「实例列表」查看')
    else ElMessage.warning(res?.message || '购买结果未知')
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.message || '购买失败')
  } finally {
    paying.value = false
  }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">购买实例</h2><div class="text-muted">DDoS高防 · 选择防护方案（大带宽定制请联系客服）</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
          <el-table :data="viewRows(data)" stripe>
            <el-table-column prop="name" label="方案" min-width="160" />
            <el-table-column label="流量包" width="120">
              <template #default="{ row }">{{ fmtBytes(row.bytes) }}</template>
            </el-table-column>
            <el-table-column label="价格" width="120">
              <template #default="{ row }">{{ fmtPrice(row.priceCents) }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }"><el-tag v-if="row.enabled" type="success" size="small">可购</el-tag><el-tag v-else type="info" size="small">下架</el-tag></template>
            </el-table-column>
            <el-table-column label="操作" width="120">
              <template #default="{ row }">
                <el-button size="small" type="primary" :disabled="paying || !row.enabled" @click="buy(row)">立即购买</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(data || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
          <el-empty v-if="!(data || []).length" description="暂无可选方案，请联系客服定制" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
