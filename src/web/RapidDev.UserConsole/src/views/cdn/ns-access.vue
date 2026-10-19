<script setup lang="ts">
/**
 * NS托管接入 —— 用户把域名以 NS 委派方式接入本系统智能 DNS：
 * 1. 展示接入信息（NS 主机 + IP，即节点内建权威 DNS）
 * 2. 添加托管域名（生成 NS 委派记录到聚合 DNS + 本系统节点解析表）
 * 3. 委派检测（递归解析验证是否指向本系统节点 IP）
 */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'

const accessInfo = ref<any>(null)
const rows = ref<any[]>([])
const loading = ref(false)
const adding = ref(false)
const addDialog = ref(false)
const form = ref<any>({})
const checking = ref('')
const checkResult = ref<any>(null)
const checkDialog = ref(false)

const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

// 分页 + 关键词过滤（客户端）
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = rows.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})

const load = async () => {
  loading.value = true
  try {
    const [info, domains] = await Promise.all([
      cdnApi.get('/user/ns/access-info') as any,
      cdnApi.get('/user/ns/domains', { page: 1, pageSize: 200 }) as any,
    ])
    accessInfo.value = info?.data ?? info
    const d = domains?.data ?? domains
    const all = (d && !Array.isArray(d) && 'items' in d) ? d.items : (Array.isArray(d) ? d : [])
    // 托管接入页只展示委派型记录 + NS 记录
    rows.value = all.filter((r: any) => r.kind === 'delegation' || r.type === 'NS')
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || '加载失败')
  } finally { loading.value = false }
}
load()

const openAdd = () => {
  form.value = { name: '', serverId: null }
  addDialog.value = true
}

// 添加托管域名：创建委派记录（A→各线路节点 IP，自动在聚合 DNS 生成 NS 委派）
async function add() {
  const name = (form.value.name || '').trim().toLowerCase()
  if (!name || !name.includes('.')) return ElMessage.warning('请输入有效域名，如 cdn.example.com')
  adding.value = true
  try {
    // 查可用节点 IP：default 线路首个节点作为委派指向
    const info = accessInfo.value || (await (cdnApi.get('/user/ns/access-info') as any))?.data
    const nsHost = info?.nsHosts?.[0]
    if (!nsHost?.ip) { ElMessage.error('无可用接入节点'); return }
    // 1. NS 委派记录（同步到聚合 DNS，用户在自己 DNS 服务商处指 NS）
    await cdnApi.post('/user/ns/domains', { name, type: 'NS', value: nsHost.name, ttl: 600, line: 'default', kind: 'delegation' })
    // 2. 本系统节点直接应答的 A 记录（DNS 拉平：不经过 CNAME，直接返回节点 IP）
    await cdnApi.post('/user/ns/domains', { name: `*.${name}`, type: 'A', value: nsHost.ip, ttl: 600, line: 'default', kind: 'delegation' })
    ElMessage.success('托管记录已创建，请在你的 DNS 服务商处将子域 NS 指向 ' + nsHost.name)
    addDialog.value = false
    load()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || '创建失败')
  } finally { adding.value = false }
}

// 委派检测：递归解析 A 记录，命中节点 IP 即生效
async function check(row: any) {
  checking.value = row.id
  checkResult.value = null
  try {
    const r = await cdnApi.post('/user/ns/delegation-check', { name: row.name }) as any
    checkResult.value = r?.data ?? r
  } catch (e: any) {
    checkResult.value = { name: row.name, delegated: false, error: e?.response?.data?.message || '检测失败' }
  } finally { checking.value = ''; checkDialog.value = true }
}

async function del(row: any) {
  await ElMessageBox.confirm(`确认解除托管 ${row.name}？将同时删除其委派与解析记录。`, '解除托管', { type: 'warning' })
  await cdnApi.delete(`/user/ns/domains/${row.id}`)
  ElMessage.success('已解除托管')
  load()
}

const delegatedTag = (r: any) => {
  if (r.type === 'NS') return { text: '委派记录', type: 'primary' }
  if (r.delegated === 'ok') return { text: '已生效', type: 'success' }
  if (r.delegated === 'failed') return { text: '未生效', type: 'danger' }
  return { text: '待检测', type: 'info' }
}
const fmtTime = (t?: string) => (t ? new Date(t).toLocaleString() : '-')
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">NS托管接入</h2><div class="text-muted">域名解析 · 智能DNS 直接应答节点 IP（无需 CNAME 中间层）</div></div>
        <div class="col-auto ms-auto d-flex gap-2">
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 220px" class="me-2" />
          <el-button type="primary" size="small" @click="openAdd">+ 添加托管域名</el-button>
          <el-button size="small" @click="load">刷新</el-button>
        </div>
      </div>
    </div>

    <!-- 接入信息 -->
    <el-card class="mb-4">
      <template #header><b>接入信息</b><span class="text-muted ms-2 small">将域名子域的 NS 记录指向以下主机，解析即由本系统智能 DNS 接管（按线路直接应答节点 IP，DNS 拉平，无 CNAME）</span></template>
      <el-table v-if="accessInfo?.nsHosts?.length" :data="accessInfo.nsHosts" size="small" stripe>
        <el-table-column prop="name" label="NS 主机" min-width="200" />
        <el-table-column prop="ip" label="IP" width="160" />
        <el-table-column prop="node" label="节点" width="140" />
        <el-table-column label="线路" width="110">
          <template #default="{ row }">
            <el-tag size="small" :type="row.line === 'default' ? 'info' : 'warning'">{{ row.line }}</el-tag>
          </template>
        </el-table-column>
      </el-table>
      <el-alert type="info" :closable="false" class="mt-3">
        <template #title>接入步骤：① 在你的 DNS 服务商处，为要托管的子域添加 NS 记录指向上方 NS 主机；② 在本页「添加托管域名」登记；③ 点击「委派检测」验证生效。生效后解析由本系统按线路直接应答节点 IP。</template>
      </el-alert>
    </el-card>

    <!-- 托管域名列表 -->
    <el-card>
      <template #header><b>我的托管域名</b></template>
      <el-table :data="viewRows" stripe v-loading="loading">
        <el-table-column prop="name" label="域名" min-width="220" />
        <el-table-column prop="type" label="记录类型" width="100" />
        <el-table-column prop="value" label="指向" min-width="180" show-overflow-tooltip />
        <el-table-column prop="line" label="线路" width="100" />
        <el-table-column label="委派状态" width="110">
          <template #default="{ row }">
            <el-tag size="small" :type="(delegatedTag(row).type as any)">{{ delegatedTag(row).text }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最近检测" width="170">
          <template #default="{ row }">{{ fmtTime(row.delegatedCheckedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" fixed="right" width="200">
          <template #default="{ row }">
            <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" type="primary" plain :loading="checking === row.id" @click="check(row)">委派检测</el-button>
              <el-button size="small" type="danger" @click="del(row)">解除托管</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
        :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
        @current-change="(p: number) => { pager.page = p }"
        @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
      <el-empty v-if="!viewRows.length" description="暂无托管域名，点击右上角「添加托管域名」开始" />
    </el-card>

    <!-- 添加托管域名 -->
    <el-dialog v-model="addDialog" title="添加托管域名" width="560px">
      <el-alert type="warning" :closable="false" class="mb-3">
        <template #title>前提：你需要对该域名的一级域（DNS 服务商）有管理权限，可以添加 NS 记录。</template>
      </el-alert>
      <el-form label-width="120px">
        <el-form-item label="托管域名" required>
          <el-input v-model="form.name" placeholder="子域名，如 cdn.example.com 或 example.com" />
        </el-form-item>
        <el-form-item label="说明">
          <div class="text-muted small">
            系统将：① 在聚合 DNS 生成 NS 委派记录；② 在本系统节点解析表登记（*.域名 → 节点 IP，DNS 拉平）。
            你需要在 DNS 服务商处把该子域的 NS 指向接入主机。
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="addDialog = false">取消</el-button>
        <el-button type="primary" :loading="adding" @click="add">创建托管</el-button>
      </template>
    </el-dialog>

    <!-- 委派检测结果 -->
    <el-dialog v-model="checkDialog" :title="`委派检测 · ${checkResult?.name || ''}`" width="620px">
      <template v-if="checkResult">
        <el-result v-if="checkResult.delegated" icon="success" title="委派已生效" sub-title="解析由本系统智能 DNS 接管（直接应答节点 IP）" />
        <el-result v-else icon="warning" title="委派未生效" :sub-title="checkResult.hint || checkResult.error || '请检查 NS 记录'" />
        <div class="small">
          <div class="mb-1"><b>递归解析结果：</b>{{ (checkResult.resolved || []).join(', ') || '无' }}</div>
          <div><b>本系统节点 IP：</b>{{ (checkResult.nodeIps || []).join(', ') }}</div>
        </div>
      </template>
      <template #footer><el-button @click="checkDialog = false">关闭</el-button></template>
    </el-dialog>
  </div>
</template>
