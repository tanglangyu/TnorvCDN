<script setup lang="ts">
/** 已购资源包 —— 对接 TnorvCDN 主控 /subscriptions */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/subscriptions'

const enabledFilter = ref<boolean | undefined>(undefined)
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const data = await cdnApi.get(apiUrl, {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    enabled: enabledFilter.value,
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

const cols: any[] = [{k:"id",t:"ID",w:70},{k:"userId",t:"用户",w:90},{k:"planId",t:"套餐",w:90},{k:"status",t:"状态",w:110}]

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
        <div class="col-auto"><h2 class="page-title">已购资源包</h2><div class="text-muted">TnorvCDN · 已购资源包</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-radio-group v-model="enabledFilter" size="small" @change="() => { pager.page = 1; gate?.refresh?.() }">
              <el-radio-button :value="undefined">全部</el-radio-button>
              <el-radio-button :value="true">启用中</el-radio-button>
              <el-radio-button :value="false">已停用</el-radio-button>
            </el-radio-group>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column v-for="col in cols" :key="col.k" :label="col.t" :width="col.w" :min-width="col.min">
              <template #default="{ row }">{{ fmtVal(row, col) }}</template>
            </el-table-column>
            
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

      </template>
    </CdnGate>
  </div>
</template>
