<script setup lang="ts">
/** 节点SSH —— 对接 TnorvCDN 主控 /ssh-grants */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/ssh-grants'

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
  form.value = { nodeName: '', host: '', port: '', username: '', authType: '' }
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

const cols: any[] = [{k:"id",t:"ID",w:70},{k:"nodeName",t:"节点名",w:150},{k:"host",t:"主机",w:160},{k:"port",t:"端口",w:90},{k:"username",t:"用户",w:110},{k:"authType",t:"认证",w:100}]

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
        <div class="col-auto"><h2 class="page-title">节点SSH</h2><div class="text-muted">TnorvCDN · 节点SSH</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openAdd()">+ 新增授权</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
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
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

        <el-dialog v-model="dialog" title="节点SSH" width="540px">
          <el-form label-width="110px">
            <el-form-item label="节点名"><el-input v-model="form.nodeName" /></el-form-item>
            <el-form-item label="主机"><el-input v-model="form.host" /></el-form-item>
            <el-form-item label="端口"><el-input v-model="form.port" /></el-form-item>
            <el-form-item label="用户名"><el-input v-model="form.username" /></el-form-item>
            <el-form-item label="认证方式"><el-select v-model="form.authType" style="width:200px"><el-option value="key" /><el-option value="password" /></el-select></el-form-item>
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
