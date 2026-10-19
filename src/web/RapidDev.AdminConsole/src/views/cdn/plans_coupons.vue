<script setup lang="ts">
/** 优惠券 —— 对接 TnorvCDN 主控 /coupons */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/coupons'

// 分页 + 关键词筛选（后端 /admin/coupons 支持 page/pageSize/q）
const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const data = await cdnApi.get(apiUrl, {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    q: filterQ.value || undefined,
  })
  rows.value = normalize(data)
  if (data && typeof data === 'object' && !Array.isArray(data) && 'total' in data) {
    pager.value.total = data.total ?? rows.value.length
  } else {
    pager.value.total = rows.value.length
  }
  return rows.value
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

function normalize(data: any): any[] {
  if (Array.isArray(data)) return data
  if (data?.items) return data.items
  if (data?.probes) return data.probes
  if (data?.byReason) return data.byReason
  if (data?.topCcIps) return data.topCcIps
  if (typeof data === 'object' && data !== null) return [data]
  return []
}

function openAdd() {
  form.value = { code: '', discountPercent: 0, maxUses: 0 }
  dialog.value = true
}

async function save() {
  try {
    await cdnApi.post(apiUrl, form.value)
    ElMessage.success('已保存')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || e?.message || '保存失败')
  }
}

async function del(row: any) {
  await cdnApi.delete(`${apiUrl}/${row.id}`)
  ElMessage.success('已删除')
  gate.value?.refresh?.()
}

const cols: any[] = [{k:"code",t:"兑换码",w:170},{k:"discountPercent",t:"折扣%",w:100},{k:"maxUses",t:"限用",w:90},{k:"used",t:"已用",w:90},{k:"expireAt",t:"到期",w:170,fmt:"time"},{k:"enabled",t:"启用",w:90,fmt:"bool"}]

function fmtVal(row: any, col: any) {
  const v = row[col.k]
  if (col.fmt === 'time') return v ? new Date(v).toLocaleString() : '-'
  if (col.fmt === 'bytes') return v >= 1073741824 ? (v / 1073741824).toFixed(2) + ' GB' : v >= 1048576 ? (v / 1048576).toFixed(1) + ' MB' : (v || 0) + ' B'
  if (col.fmt === 'bool') return v ? '是' : '否'
  if (col.fmt === 'join') return Array.isArray(v) ? v.join(', ') : v
  return v ?? '-'
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">优惠券</h2><div class="text-muted">TnorvCDN · 优惠券</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openAdd()">+ 生成优惠券</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="券码关键词" size="small" clearable style="width: 220px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column v-for="col in cols" :key="col.k" :label="col.t" :width="col.w" :min-width="col.min">
              <template #default="{ row }">{{ fmtVal(row, col) }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" type="danger" @click="del(row)">删除</el-button>
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

        <el-dialog v-model="dialog" title="优惠券" width="540px">
          <el-form label-width="110px">
            <el-form-item label="兑换码"><el-input v-model="form.code" /></el-form-item>
            <el-form-item label="折扣%"><el-input-number v-model="form.discountPercent" :min="0" style="width:200px" /></el-form-item>
            <el-form-item label="限用次数"><el-input-number v-model="form.maxUses" :min="0" style="width:200px" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" @click="save">保存</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
