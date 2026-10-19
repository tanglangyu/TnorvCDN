<script setup lang="ts">
/** 网站分组 —— 对接 TnorvCDN 主控 /ns-domains */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = rows.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/ns-domains'

const loadData = async () => {
  const data = await cdnApi.get(apiUrl)
  rows.value = normalize(data)
  return rows.value
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
  form.value = { name: '', group: '', line: '', status: '' }
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

const cols: any[] = [{k:"name",t:"名称/域名",w:220},{k:"group",t:"分组",w:140},{k:"line",t:"线路",w:110},{k:"status",t:"状态",w:90},{k:"createdAt",t:"创建时间",w:170,fmt:"time"}]

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
        <div class="col-auto"><h2 class="page-title">网站分组</h2><div class="text-muted">TnorvCDN · 网站分组</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
            <el-button type="primary" size="small" @click="openAdd()">+ 新增</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="viewRows" stripe>
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
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

        <el-dialog v-model="dialog" title="网站分组" width="540px">
          <el-form label-width="110px">
            <el-form-item label="名称/域名"><el-input v-model="form.name" /></el-form-item>
            <el-form-item label="分组"><el-input v-model="form.group" /></el-form-item>
            <el-form-item label="线路"><el-select v-model="form.line" style="width:200px"><el-option value="default" /><el-option value="telecom" /><el-option value="unicom" /><el-option value="mobile" /></el-select></el-form-item>
            <el-form-item label="状态"><el-select v-model="form.status" style="width:200px"><el-option value="active" /><el-option value="paused" /></el-select></el-form-item>
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
