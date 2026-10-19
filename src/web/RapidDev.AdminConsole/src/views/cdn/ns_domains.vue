<script setup lang="ts">
/** 域名管理 —— 对接 TnorvCDN 主控 /ns-domains */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/ns-domains'

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
  editingId.value = 0
  form.value = { name: '', group: '', line: 'default', type: 'A', value: '', ttl: 600 }
  dialog.value = true
}

// ---- 编辑（P3 补齐）----
const editingId = ref(0)
function openEdit(row: any) {
  editingId.value = row.id
  form.value = { name: row.name, group: row.group || '', line: row.line || 'default', type: row.type || 'A', value: row.value || '', ttl: row.ttl || 600, status: row.status || 'active' }
  dialog.value = true
}
async function saveEdit() {
  try {
    if (editingId.value) {
      await cdnApi.put(`${apiUrl}/${editingId.value}`, form.value)
      ElMessage.success('已保存并重发布 DNS 记录')
    } else {
      await cdnApi.post(apiUrl, form.value)
      ElMessage.success('已新增')
    }
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data || e?.message || '保存失败')
  }
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

const cols: any[] = [{k:"id",t:"ID",w:70},{k:"name",t:"域名",min:210},{k:"group",t:"分组",w:130},{k:"line",t:"默认线路",w:120},{k:"status",t:"状态",w:100}]

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
        <div class="col-auto"><h2 class="page-title">域名管理</h2><div class="text-muted">TnorvCDN · 域名管理</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openAdd()">+ 新增域名</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="域名关键词" size="small" clearable style="width: 220px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
              </div>
          <el-table :data="rows" stripe>
            <el-table-column v-for="col in cols" :key="col.k" :label="col.t" :width="col.w" :min-width="col.min">
              <template #default="{ row }">{{ fmtVal(row, col) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="160">
              <template #default="{ row }">
                <el-button size="small" @click="openEdit(row)">编辑</el-button>
                <el-button size="small" type="danger" @click="del(row)">删除</el-button>
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

        <el-dialog v-model="dialog" :title="editingId ? '编辑 DNS 记录' : '新增 DNS 记录'" width="540px">
          <el-form label-width="110px">
            <el-form-item label="域名"><el-input v-model="form.name" :disabled="!!editingId" /></el-form-item>
            <el-form-item label="分组"><el-input v-model="form.group" /></el-form-item>
            <el-form-item label="线路">
              <el-select v-model="form.line" style="width:200px">
                <el-option value="default" label="默认（全网）" /><el-option value="电信" /><el-option value="联通" /><el-option value="移动" />
                <el-option value="教育网" /><el-option value="广电" /><el-option value="境外" /><el-option value="港澳台" />
              </el-select>
            </el-form-item>
            <el-form-item label="记录类型">
              <el-select v-model="form.type" style="width:200px">
                <el-option value="A" /><el-option value="AAAA" /><el-option value="CNAME" /><el-option value="TXT" /><el-option value="NS" />
              </el-select>
            </el-form-item>
            <el-form-item label="记录值"><el-input v-model="form.value" placeholder="IP / 目标域名 / 文本" /></el-form-item>
            <el-form-item label="TTL（秒）"><el-input-number v-model="form.ttl" :min="60" :max="86400" :step="60" /></el-form-item>
            <el-form-item label="状态">
              <el-select v-model="form.status" style="width:200px">
                <el-option value="active" label="启用" /><el-option value="paused" label="暂停" />
              </el-select>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" @click="editingId ? saveEdit() : save()">保存</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
