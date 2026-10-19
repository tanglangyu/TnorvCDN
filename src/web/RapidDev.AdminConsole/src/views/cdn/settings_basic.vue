<script setup lang="ts">
/** 基础设置 —— 对接 TnorvCDN 主控 /overview */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/overview'

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

const cols: any[] = [{k:"servers",t:"网站数",w:110},{k:"nodesOnline",t:"在线节点",w:120},{k:"users",t:"用户数",w:110},{k:"requests24h",t:"24h请求",w:120}]

function fmtVal(row: any, col: any) {
  const v = row[col.k]
  if (col.fmt === 'time') return v ? new Date(v).toLocaleString() : '-'
  if (col.fmt === 'bytes') return v >= 1073741824 ? (v / 1073741824).toFixed(2) + ' GB' : v >= 1048576 ? (v / 1048576).toFixed(1) + ' MB' : (v || 0) + ' B'
  if (col.fmt === 'bool') return v ? '是' : '否'
  if (col.fmt === 'join') return Array.isArray(v) ? v.join(', ') : v
  return v ?? '-'
}

// 数据备份：流式下载 pg_dump 导出
const backupLoading = ref(false)
const doBackup = async () => {
  backupLoading.value = true
  try {
    const token = localStorage.getItem('token') || ''
    const resp = await fetch('/api/admin/database/backup', { headers: { Authorization: 'Bearer ' + token } })
    if (!resp.ok) { ElMessage.error('备份失败（HTTP ' + resp.status + '）'); return }
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `tnorv-backup-${new Date().toISOString().slice(0, 10)}.sql.gz`
    a.click()
    URL.revokeObjectURL(url)
    ElMessage.success('备份已开始下载')
  } finally {
    backupLoading.value = false
  }
}

// IP 库状态与解析测试
const geoStatus = ref<any>(null)
const geoLoading = ref(false)
const geoTestIp = ref('')
const geoTestResult = ref('')
const loadGeo = async () => {
  geoLoading.value = true
  try { geoStatus.value = await cdnApi.get('/admin/geoip/status') } catch { } finally { geoLoading.value = false }
}
loadGeo()
const testGeo = async () => {
  if (!geoTestIp.value.trim()) { ElMessage.warning('请输入 IP'); return }
  geoLoading.value = true
  try {
    const r = await cdnApi.get('/admin/geoip/status', { testIp: geoTestIp.value.trim() })
    geoTestResult.value = r.testResult || '未知'
  } finally { geoLoading.value = false }
}

// IP 库清单
const libItems = ref<any[]>([])
const libName = ref('')
const libKind = ref('deny')
const loadLib = async () => { try { libItems.value = await cdnApi.get('/admin/iplib') } catch { } }
loadLib()
const addLib = async () => {
  if (!libName.value.trim()) { ElMessage.warning('请填清单名'); return }
  const cidrs = window.prompt('粘贴 IP/CIDR（每行一个，如 1.2.3.0/24）')
  if (!cidrs) return
  await cdnApi.post('/admin/iplib', { name: libName.value.trim(), kind: libKind.value, cidrs })
  ElMessage.success('清单已保存并重发布')
  await loadLib()
}
const delLib = async (row: any) => {
  await cdnApi.delete('/admin/iplib/' + row.id)
  ElMessage.success('已删除并重发布')
  await loadLib()
}

// 数据迁移：导出/导入
const importVisible = ref(false)
const importLoading = ref(false)
const importFile = ref<any>(null)
const doExport = async () => {
  const token = localStorage.getItem('token') || ''
  const resp = await fetch('/api/admin/database/export', { headers: { Authorization: 'Bearer ' + token } })
  if (!resp.ok) { ElMessage.error('导出失败'); return }
  const blob = await resp.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `tnorv-config-export-${new Date().toISOString().slice(0, 10)}.json`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success('配置已导出')
}
const doImport = async () => {
  const f = importFile.value?.files?.[0]
  if (!f) { ElMessage.warning('请选择 JSON 文件'); return }
  importLoading.value = true
  try {
    const text = await f.text()
    const body = JSON.parse(text)
    const r = await cdnApi.post('/admin/database/import', body)
    ElMessage.success(`导入完成：${(r.imported || []).length} 行补齐`)
    importVisible.value = false
  } catch (e: any) {
    ElMessage.error(e?.message || '导入失败（JSON 格式错误？）')
  } finally {
    importLoading.value = false
  }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">基础设置</h2><div class="text-muted">TnorvCDN · 基础设置</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column v-for="col in cols" :key="col.k" :label="col.t" :width="col.w" :min-width="col.min">
              <template #default="{ row }">{{ fmtVal(row, col) }}</template>
            </el-table-column>
            
          </el-table>
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

        <el-card class="mt-3">
          <template #header><b>数据备份</b><span class="text-muted ms-2 small">PostgreSQL 全库导出（pg_dump，gzip 压缩）</span></template>
          <div class="d-flex gap-2 align-items-center">
            <el-button type="primary" size="small" :loading="backupLoading" @click="doBackup">下载备份</el-button>
            <span class="text-muted small">备份操作会记录审计日志；建议定期下载并存档到离线存储</span>
          </div>
        </el-card>

        <el-card class="mt-3">
          <template #header><b>IP 库</b><span class="text-muted ms-2 small">访客归属地解析通用设计</span></template>
          <div v-loading="geoLoading">
            <el-descriptions v-if="geoStatus" :column="3" border size="small">
              <el-descriptions-item label="供应商">{{ geoStatus.provider }}</el-descriptions-item>
              <el-descriptions-item label="近 500 条解析覆盖">{{ geoStatus.coverage }}%</el-descriptions-item>
              <el-descriptions-item label="已缓存样本">{{ geoStatus.resolvedRecent }}/{{ geoStatus.sampledRecent }}</el-descriptions-item>
            </el-descriptions>
            <div class="d-flex gap-2 align-items-center mt-2">
              <el-input v-model="geoTestIp" placeholder="解析测试：输入 IP 如 114.114.114.114" style="width: 280px" size="small" clearable />
              <el-button size="small" @click="testGeo">解析测试</el-button>
              <el-tag v-if="geoTestResult" size="small" type="success">{{ geoTestResult }}</el-tag>
            </div>
          </div>
        </el-card>

        <el-card class="mt-3">
          <template #header><b>IP 库清单</b><span class="text-muted ms-2 small">手动上传 IP 段，发布期合并进全集群安全配置（deny→黑名单 / allow→白名单）</span></template>
          <div class="mb-2 d-flex gap-2">
            <el-input v-model="libName" size="small" placeholder="清单名（如 恶意采集器）" style="width: 180px" />
            <el-select v-model="libKind" size="small" style="width: 110px">
              <el-option value="deny" label="黑名单" /><el-option value="allow" label="白名单" />
            </el-select>
            <el-button size="small" type="primary" @click="addLib">添加清单</el-button>
          </div>
          <el-table :data="libItems" size="small" stripe>
            <el-table-column prop="name" label="名称" min-width="140" />
            <el-table-column label="类型" width="90">
              <template #default="{ row }"><el-tag :type="row.kind === 'deny' ? 'danger' : 'success'" size="small">{{ row.kind === 'deny' ? '黑名单' : '白名单' }}</el-tag></template>
            </el-table-column>
            <el-table-column prop="cidrs" label="IP 段" min-width="240" show-overflow-tooltip />
            <el-table-column label="操作" width="90" fixed="right">
              <template #default="{ row }"><el-button link type="danger" size="small" @click="delLib(row)">删除</el-button></template>
            </el-table-column>
          </el-table>
          <el-empty v-if="!libItems.length" description="暂无清单（粘贴 CIDR 每行一个，如 1.2.3.0/24）" :image-size="40" />
        </el-card>

        <el-card class="mt-3">
          <template #header><b>数据迁移</b><span class="text-muted ms-2 small">站点/集群/套餐配置 JSON 导出与导入通用设计</span></template>
          <div class="d-flex gap-2 align-items-center">
            <el-button size="small" @click="doExport">导出配置 JSON</el-button>
            <el-button size="small" type="warning" plain @click="importVisible = true">导入配置 JSON</el-button>
            <span class="text-muted small">导入仅补齐缺失行（按 ID 冲突跳过），幂等安全</span>
          </div>
        </el-card>

      </template>
    </CdnGate>

    <el-dialog v-model="importVisible" title="导入配置 JSON" width="520px" align-center>
      <el-alert type="warning" :closable="false" class="mb-3" title="仅导入当前库中缺失的集群/套餐/站点；已存在的 ID 会跳过" />
      <input ref="importFile" type="file" accept=".json" class="form-control" />
      <template #footer>
        <el-button @click="importVisible = false">取消</el-button>
        <el-button type="primary" :loading="importLoading" @click="doImport">开始导入</el-button>
      </template>
    </el-dialog>
  </div>
</template>
