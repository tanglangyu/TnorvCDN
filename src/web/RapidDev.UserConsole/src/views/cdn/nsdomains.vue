<script setup lang="ts">
/** 我的域名 —— 用户侧智能 DNS 记录 CRUD（/api/user/ns/domains，含分组过滤/批量操作） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const groupFilter = ref('')
const selected = ref<number[]>([])
const dialog = ref(false)
const saving = ref(false)
const form = ref<any>({})
// U8 行内编辑：editingId 非空时该行进入编辑态；editForm 为行内草稿
const editingId = ref(0)
const editForm = ref<any>({})
const syncing = ref(0)
const dnsChecking = ref(0)
const dnsResult = ref<any>(null)
const dnsDialog = ref(false)

const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const loadData = async () => {
  const params: any = {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    q: filterQ.value || undefined,
  }
  if (groupFilter.value) params.group = groupFilter.value
  const r = await cdnApi.get('/user/ns/domains', params) as any
  // request() 已拆包一次：返回值即 ApiResult.data = { total, page, pageSize, items }
  const d = r && 'total' in r ? r : r?.data
  if (d && !Array.isArray(d) && 'total' in d) {
    pager.value.total = d.total
    rows.value = d.items || []
  } else {
    rows.value = Array.isArray(d) ? d : []
    pager.value.total = rows.value.length
  }
  return rows.value
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

function startEdit(row: any) {
  editingId.value = row.id
  editForm.value = { type: row.type || 'A', value: row.value || '', ttl: row.ttl || 600, line: row.line || 'default' }
}
const cancelEdit = () => { editingId.value = 0 }

async function saveEdit(row: any) {
  if (!editForm.value.value?.trim()) return ElMessage.warning('记录值不能为空')
  try {
    await cdnApi.put(`/user/ns/domains/${row.id}`, { ...row, ...editForm.value })
    ElMessage.success('已保存')
    editingId.value = 0
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
}

// U8：重新同步（失败重试入口）
async function resync(row: any) {
  syncing.value = row.id
  try {
    const r = (await cdnApi.post(`/user/ns/domains/${row.id}/sync`)) as any
    const ok = r?.data?.ok ?? r?.ok
    if (ok) ElMessage.success('已重新同步并生效')
    else ElMessage.warning('同步未完成，请稍后重试')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || '同步失败') }
  finally { syncing.value = 0 }
}

// U8：解析检测（A 记录查询）
async function dnsCheck(row: any) {
  dnsChecking.value = row.id
  dnsResult.value = null
  try {
    const ips = await (window as any).fetch(`https://dns.alidns.com/resolve?name=${encodeURIComponent(row.name)}&type=A`).then((r2: any) => r2.json())
    dnsResult.value = { name: row.name, answers: (ips.Answer || []).map((a: any) => a.data) }
  } catch {
    dnsResult.value = { name: row.name, answers: [], error: '查询失败' }
  } finally { dnsChecking.value = 0; dnsDialog.value = true }
}

function openAdd() {
  form.value = { name: '', type: 'A', value: '', ttl: 600, line: 'default', group: '' }
  dialog.value = true
}
function openEdit(row: any) {
  form.value = { ...row }
  dialog.value = true
}

async function save() {
  if (!form.value.name || !form.value.value) return ElMessage.warning('域名与记录值必填')
  saving.value = true
  try {
    if (form.value.id) await cdnApi.put(`/user/ns/domains/${form.value.id}`, form.value)
    else await cdnApi.post('/user/ns/domains', form.value)
    ElMessage.success('已保存')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}

async function del(row: any) {
  await ElMessageBox.confirm(`确认删除记录 ${row.name}？`, '删除', { type: 'warning' })
  await cdnApi.delete(`/user/ns/domains/${row.id}`)
  ElMessage.success('已删除')
  gate.value?.refresh?.()
}

async function batch(action: string) {
  if (!selected.value.length) return ElMessage.warning('请先勾选记录')
  const names: any = { delete: '删除', enable: '启用', disable: '停用' }
  await ElMessageBox.confirm(`确认${names[action]}选中的 ${selected.value.length} 条记录？`, '批量操作', { type: 'warning' })
  await cdnApi.post('/user/ns/domains/batch', { ids: selected.value, action })
  ElMessage.success(`已${names[action]} ${selected.value.length} 条`)
  gate.value?.refresh?.()
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">我的域名</h2><div class="text-muted">域名解析 · DNS 记录管理</div></div>
        <div class="col-auto ms-auto d-flex gap-2">
          <el-input v-model="filterQ" placeholder="域名/记录值关键词" size="small" clearable style="width: 200px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
            <el-input v-model="groupFilter" placeholder="按分组过滤" clearable style="width:160px" size="small" @change="gate?.refresh?.()" />
          <el-button type="primary" size="small" @click="openAdd()">+ 添加记录</el-button>
        </div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button size="small" @click="batch('enable')" :disabled="!selected.length">批量启用</el-button>
            <el-button size="small" @click="batch('disable')" :disabled="!selected.length">批量停用</el-button>
            <el-button size="small" type="danger" @click="batch('delete')" :disabled="!selected.length">批量删除</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="rows" stripe @selection-change="(sel: any[]) => selected = sel.map(x => x.id)">
            <el-table-column type="selection" width="45" />
            <el-table-column prop="name" label="域名" min-width="220" />
            <el-table-column prop="type" label="类型" width="90" />
            <el-table-column label="类型" width="90">
              <template #default="{ row }">
                <el-select v-if="editingId === row.id" v-model="editForm.type" size="small" style="width: 80px">
                  <el-option value="A" /><el-option value="AAAA" /><el-option value="CNAME" /><el-option value="TXT" />
                </el-select>
                <span v-else>{{ row.type }}</span>
              </template>
            </el-table-column>
            <el-table-column label="记录值" min-width="200">
              <template #default="{ row }">
                <el-input v-if="editingId === row.id" v-model="editForm.value" size="small" placeholder="IP 或目标域名"
                  @keyup.enter="saveEdit(row)" @keyup.esc="cancelEdit" />
                <span v-else class="text-truncate d-inline-block" style="max-width: 240px; vertical-align: middle; cursor: pointer" :title="row.value + '（点击编辑）'"
                  @click="startEdit(row)">{{ row.value }}</span>
              </template>
            </el-table-column>
            <el-table-column label="线路" width="130">
              <template #default="{ row }">
                <el-select v-if="editingId === row.id" v-model="editForm.line" size="small" style="width: 110px">
                  <el-option value="default" label="默认" /><el-option value="telecom" label="电信" />
                  <el-option value="unicom" label="联通" /><el-option value="mobile" label="移动" />
                </el-select>
                <span v-else>{{ row.line }}</span>
              </template>
            </el-table-column>
            <el-table-column label="TTL" width="110">
              <template #default="{ row }">
                <el-input-number v-if="editingId === row.id" v-model="editForm.ttl" size="small" :min="60" :max="86400" :controls="false" style="width: 80px" />
                <span v-else>{{ row.ttl }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="group" label="分组" width="120" />
            <el-table-column label="生效状态" width="110">
              <template #default="{ row }">
                <el-tag v-if="row.syncStatus === 'synced'" size="small" type="success">已生效</el-tag>
                <el-tag v-else-if="row.syncStatus === 'local'" size="small" type="info">仅本站</el-tag>
                <el-tooltip v-else-if="(row.syncStatus || '').startsWith('failed')" :content="row.syncStatus" placement="top">
                  <el-tag size="small" type="warning">同步失败</el-tag>
                </el-tooltip>
                <el-tag v-else size="small" type="info">—</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="row.status === 'active' ? 'success' : 'info'">{{ row.status === 'active' ? '正常' : '停用' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" fixed="right" width="240">
              <template #default="{ row }">
                <div v-if="editingId === row.id" class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
                  <el-button size="small" type="primary" @click="saveEdit(row)">保存</el-button>
                  <el-button size="small" @click="cancelEdit">取消</el-button>
                </div>
                <div v-else class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
                  <el-button size="small" @click="startEdit(row)">编辑</el-button>
                  <el-tooltip v-if="(row.syncStatus || '').startsWith('failed') || row.syncStatus === 'local'" content="重新同步到权威 DNS">
                    <el-button size="small" type="warning" plain :loading="syncing === row.id" @click="resync(row)">同步</el-button>
                  </el-tooltip>
                  <el-button size="small" text type="primary" :loading="dnsChecking === row.id" @click="dnsCheck(row)">解析检测</el-button>
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
          <el-empty v-if="!rows.length" description="暂无解析记录，点击右上角「添加记录」开始" />
        </el-card>

        <el-dialog v-model="dialog" :title="form.id ? '编辑记录' : '添加记录'" width="560px">
          <el-form label-width="100px">
            <el-form-item label="域名"><el-input v-model="form.name" placeholder="如 www.example.com" /></el-form-item>
            <el-form-item label="类型">
              <el-select v-model="form.type" style="width:130px">
                <el-option value="A" /><el-option value="AAAA" /><el-option value="CNAME" /><el-option value="TXT" />
              </el-select>
            </el-form-item>
            <el-form-item label="记录值"><el-input v-model="form.value" placeholder="IP 或目标域名" /></el-form-item>
            <el-form-item label="线路">
              <el-select v-model="form.line" style="width:160px">
                <el-option value="default" label="默认（全网）" /><el-option value="telecom" label="电信" /><el-option value="unicom" label="联通" /><el-option value="mobile" label="移动" />
              </el-select>
            </el-form-item>
            <el-form-item label="TTL"><el-input-number v-model="form.ttl" :min="60" :max="86400" /></el-form-item>
            <el-form-item label="分组"><el-input v-model="form.group" placeholder="可选，用于筛选" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" :loading="saving" @click="save">保存</el-button>
          </template>
        </el-dialog>

        <!-- U8 解析检测结果 -->
        <el-dialog v-model="dnsDialog" :title="`解析检测：${dnsResult?.name || ''}`" width="420px">
          <div v-if="dnsResult">
            <div v-if="dnsResult.answers?.length">
              <div class="text-muted small mb-2">A 记录查询结果（公共 DNS）：</div>
              <el-tag v-for="(a, i) in dnsResult.answers" :key="i" class="me-2 mb-1">{{ a }}</el-tag>
            </div>
            <el-empty v-else :description="dnsResult.error || '未查询到 A 记录（CNAME/TXT 请用 dig 验证）'" :image-size="60" />
          </div>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
