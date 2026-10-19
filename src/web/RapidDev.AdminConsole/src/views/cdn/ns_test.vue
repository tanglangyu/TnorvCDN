<script setup lang="ts">
/** 解析测试 —— 对接 TnorvCDN 主控 /dns/status */
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
const apiUrl = '/admin/dns/status'

// ---- 真实解析测试：向在线节点内建权威 DNS（:53）发 UDP 查询 ----
const testDomain = ref('')
const testType = ref('A')
const testing = ref(false)
const testResult = ref<any>(null)
async function runTest() {
  const name = (testDomain.value || '').trim()
  if (!name) return ElMessage.warning('请输入要测试的域名')
  testing.value = true
  testResult.value = null
  try {
    testResult.value = await cdnApi.post('/admin/dns/resolve-test', { name, type: testType.value })
    if (!testResult.value?.ok) ElMessage.warning(testResult.value?.error || ('解析失败 rcode=' + testResult.value?.rcode))
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '测试请求失败') }
  finally { testing.value = false }
}

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

const cols: any[] = [{k:"enabled",t:"三方DNS",w:130,fmt:"bool"}]

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
        <div class="col-auto"><h2 class="page-title">解析测试</h2><div class="text-muted">TnorvCDN · 解析测试</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card class="mb-4">
          <template #header><b>解析测试</b><span class="text-muted ms-2 small">向在线节点的内建权威 DNS（:53）发起真实 UDP 查询</span></template>
          <div class="d-flex gap-2 align-items-center flex-wrap">
            <el-input v-model="testDomain" placeholder="域名，如 www.example.com" style="width: 280px" @keyup.enter="runTest" />
            <el-select v-model="testType" style="width: 110px">
              <el-option value="A" /><el-option value="AAAA" /><el-option value="CNAME" /><el-option value="NS" /><el-option value="TXT" />
            </el-select>
            <el-button type="primary" :loading="testing" @click="runTest">查询</el-button>
          </div>
          <el-descriptions v-if="testResult" :column="1" border size="small" class="mt-3">
            <el-descriptions-item label="DNS 服务器">{{ testResult.server }}</el-descriptions-item>
            <el-descriptions-item label="结果">
              <template v-if="testResult.ok && (testResult.answers || []).length">
                <el-tag v-for="a in testResult.answers" :key="a" size="small" class="me-1">{{ a }}</el-tag>
              </template>
              <template v-else-if="testResult.ok">无记录（rcode=0 NOERROR）</template>
              <template v-else>失败{{ testResult.rcode != null ? '（rcode=' + testResult.rcode + '）' : '' }}{{ testResult.error || '' }}</template>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>

        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
            
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="viewRows" stripe>
            <el-table-column v-for="col in cols" :key="col.k" :label="col.t" :width="col.w" :min-width="col.min">
              <template #default="{ row }">{{ fmtVal(row, col) }}</template>
            </el-table-column>
            
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

      </template>
    </CdnGate>
  </div>
</template>
