<script setup lang="ts">
/** 工单 —— 对接 TnorvCDN 主控 /tickets */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/tickets'

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
  form.value = { title: '', content: '', priority: '', status: '' }
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

async function reply(row: any) {
  try {
    const { value } = await ElMessageBox.prompt('回复内容', `回复工单：${row.title}`, {
      confirmButtonText: '发送回复', cancelButtonText: '取消', inputType: 'textarea',
    })
    await cdnApi.put(`${apiUrl}/${row.id}`, { ...row, reply: value || '', status: 'replied' })
    ElMessage.success('已回复')
    gate.value?.refresh?.()
  } catch { /* 取消 */ }
}

async function close(row: any) {
  try {
    await ElMessageBox.confirm(`确认关闭工单「${row.title}」？`, '关闭工单', { type: 'warning' })
    await cdnApi.put(`${apiUrl}/${row.id}`, { ...row, status: 'closed' })
    ElMessage.success('已关闭')
    gate.value?.refresh?.()
  } catch { /* 取消 */ }
}

const cols: any[] = [{k:"id",t:"ID",w:60},{k:"title",t:"标题",min:180},{k:"status",t:"状态",w:90},{k:"priority",t:"优先级",w:90},{k:"reply",t:"回复",min:140},{k:"createdAt",t:"时间",w:170,fmt:"time"}]

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
        <div class="col-auto"><h2 class="page-title">工单</h2><div class="text-muted">TnorvCDN · 工单</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openAdd()">+ 新增工单</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="标题/内容关键词" size="small" clearable style="width: 220px"
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
              <el-button v-if="row.status !== 'closed'" size="small" type="primary" plain @click="reply(row)">回复</el-button>
              <el-button v-if="row.status !== 'closed'" size="small" @click="close(row)">关闭</el-button>
              <el-button size="small" type="danger" plain @click="del(row)">删除</el-button>
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

        <el-dialog v-model="dialog" title="工单" width="540px">
          <el-form label-width="110px">
            <el-form-item label="标题"><el-input v-model="form.title" /></el-form-item>
            <el-form-item label="内容"><el-input v-model="form.content" type="textarea" :rows="3" /></el-form-item>
            <el-form-item label="优先级"><el-select v-model="form.priority" style="width:200px"><el-option value="low" /><el-option value="medium" /><el-option value="high" /></el-select></el-form-item>
            <el-form-item label="状态"><el-select v-model="form.status" style="width:200px"><el-option value="open" /><el-option value="replied" /><el-option value="closed" /></el-select></el-form-item>
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
