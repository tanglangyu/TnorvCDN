<script setup lang="ts">
/** 线路管理 —— 节点按线路聚合 */
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
const nodeRows = ref<any[]>([])
const editDialog = ref(false)
const editForm = ref<any>({})
const LINES = [
  ['default', '默认（全网）'], ['电信', '中国电信'], ['联通', '中国联通'], ['移动', '中国移动'],
  ['教育网', '中国教育网'], ['广电', '中国广电'], ['境外', '海外（全部境外）'], ['港澳台', '港澳台地区'],
  ['美国', '美国'], ['日本', '日本'], ['新加坡', '新加坡'], ['德国', '德国'],
] as [string, string][]
const lineLabel = (l: string) => LINES.find(x => x[0] === l)?.[1] || l

const loadData = async () => {
  const list = await cdnApi.get('/admin/nodes')
  nodeRows.value = list as any[]
  const m = new Map<string, number>()
  for (const n of nodeRows.value) { const l = n.line || 'default'; m.set(l, (m.get(l) || 0) + 1) }
  rows.value = [...m.entries()].map(([line, count]) => ({ line, count }))
  return rows.value
}

function openEdit(row: any) {
  editForm.value = { from: row.line, line: row.line }
  editDialog.value = true
}
// 批量调整某线路下全部节点的线路归属
async function saveEdit() {
  try {
    const targets = nodeRows.value.filter((n: any) => (n.line || 'default') === editForm.value.from)
    for (const n of targets) {
      await cdnApi.put(`/admin/nodes/${n.id}`, { line: editForm.value.line })
    }
    ElMessage.success(`已将 ${targets.length} 个节点从「${lineLabel(editForm.value.from)}」调整到「${lineLabel(editForm.value.line)}」，发布后 DNS 分流生效`)
    editDialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '调整失败') }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">线路管理</h2><div class="text-muted">智能DNS · 解析线路分布</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="viewRows" stripe>
            <el-table-column label="线路" width="150">
              <template #default="{ row }">{{ lineLabel(row.line) }}<span class="text-muted ms-1 small">({{ row.line }})</span></template>
            </el-table-column>
            <el-table-column prop="count" label="节点数" width="110" />
            <el-table-column label="操作" width="120">
              <template #default="{ row }">
                <el-button size="small" @click="openEdit(row)">调整节点线路</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-divider content-position="left">节点线路明细</el-divider>
          <el-table :data="nodeRows" stripe size="small">
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column prop="name" label="节点名" width="140" />
            <el-table-column label="线路" width="120">
              <template #default="{ row }">{{ lineLabel(row.line || 'default') }}</template>
            </el-table-column>
            <el-table-column label="在线" width="80">
              <template #default="{ row }"><el-tag size="small" :type="row.isOnline ? 'success' : 'danger'">{{ row.isOnline ? '在线' : '离线' }}</el-tag></template>
            </el-table-column>
            <el-table-column label="公网 IP" min-width="160">
              <template #default="{ row }">{{ (row.publicIps || []).join(', ') || '-' }}</template>
            </el-table-column>
          </el-table>

        <el-dialog v-model="editDialog" title="批量调整节点线路" width="460px">
          <el-form label-width="110px">
            <el-form-item label="源线路"><el-input :model-value="lineLabel(editForm.from || '')" disabled /></el-form-item>
            <el-form-item label="目标线路">
              <el-select v-model="editForm.line" style="width: 220px">
                <el-option v-for="[v, label] in LINES" :key="v" :value="v" :label="label + ' (' + v + ')'" />
              </el-select>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="editDialog = false">取消</el-button>
            <el-button type="primary" @click="saveEdit">确认调整</el-button>
          </template>
        </el-dialog>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
