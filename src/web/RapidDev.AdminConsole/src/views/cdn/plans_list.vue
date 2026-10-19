<script setup lang="ts">
/** 套餐列表 —— 对接 TnorvCDN 主控 /plans；支持功能开关/配额矩阵编辑（Plan.Features） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})
const apiUrl = '/admin/plans'

// 功能开关矩阵（与后端 PlanFeatures.FeatureKeys 一致）
const FEATURE_LABELS: [string, string][] = [
  ['cache', '缓存加速'], ['https', 'HTTPS/SSL'], ['compression', '内容压缩'], ['webp', 'WebP 自适应'],
  ['websocket', 'WebSocket'], ['grpc', 'gRPC 回源'], ['customPages', '自定义页面'],
  ['waf', 'WAF 规则'], ['cc', 'CC 防护'], ['jsShield', '5秒盾'], ['urlAuth', 'URL 鉴权'],
  ['referers', '防盗链'], ['edgeScripts', '边缘脚本'], ['aiGuard', 'AI 语义防护'],
  ['locations', '路径规则'], ['lb', '负载均衡(L4)'], ['ns', '智能DNS'], ['antiDdos', '高防'],
  ['api', 'API 密钥'], ['tickets', '工单'], ['finance', '财务'],
]

// 分页 + 关键词筛选（后端 /admin/plans 支持 page/pageSize/q）
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
  if (typeof data === 'object' && data !== null) return [data]
  return []
}

function parseFeatures(row: any): Record<string, any> {
  try { return row.features ? JSON.parse(row.features) : {} } catch { return {} }
}

function openAdd() {
  form.value = { name: '', priceCents: 0, maxServers: 10, maxDomains: 20, monthlyTrafficGB: 50, enabled: true, feat: {}, maxLocations: 10, maxCerts: 5 }
  dialog.value = true
}

function openEdit(row: any) {
  const f = parseFeatures(row)
  form.value = {
    id: row.id, name: row.name, priceCents: row.priceCents,
    maxServers: row.maxServers, maxDomains: row.maxDomains,
    monthlyTrafficGB: Math.round((row.monthlyTrafficMB || 0) / 1024),
    enabled: row.enabled,
    feat: { ...f },
    maxLocations: f.maxLocations ?? 10,
    maxCerts: f.maxCerts ?? 5,
  }
  dialog.value = true
}

async function save() {
  try {
    const f = form.value
    // 功能矩阵：去掉数值配额键后整体作为 features JSON；未配置任何键 = 全开
    const feat: Record<string, any> = { ...f.feat, maxLocations: Number(f.maxLocations) || 0, maxCerts: Number(f.maxCerts) || 0 }
    const payload = {
      name: f.name, priceCents: Number(f.priceCents) || 0,
      maxServers: f.maxServers, maxDomains: f.maxDomains ?? 20,
      monthlyTrafficMB: (Number(f.monthlyTrafficGB) || 0) * 1024,
      enabled: f.enabled ?? true,
      features: Object.keys(feat).length ? JSON.stringify(feat) : null,
    }
    if (f.id) await cdnApi.put(`${apiUrl}/${f.id}`, payload)
    else await cdnApi.post(apiUrl, payload)
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

function featSummary(row: any): string {
  const f = parseFeatures(row)
  const keys = Object.keys(f).filter((k) => f[k] === false)
  if (!Object.keys(f).length) return '全部功能'
  if (!keys.length) return '全部功能（含配额）'
  return '不含: ' + keys.length + ' 项'
}

const cols: any[] = [{k:"id",t:"ID",w:70},{k:"name",t:"名称",min:150},{k:"priceCents",t:"价格(分)",w:110},{k:"maxServers",t:"网站上限",w:110},{k:"monthlyTrafficGB",t:"流量(GB)",w:110},{k:"feat",t:"功能",min:130},{k:"enabled",t:"启用",w:90,fmt:"bool"}]

function fmtVal(row: any, col: any) {
  if (col.k === 'feat') return featSummary(row)
  const v = row[col.k]
  if (col.fmt === 'bool') return v ? '是' : '否'
  return v ?? '-'
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">套餐列表</h2><div class="text-muted">套餐配额与功能开关矩阵（用户端按此禁用功能）</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="openAdd()">+ 新增套餐</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-input v-model="filterQ" placeholder="套餐名称关键词" size="small" clearable style="width: 220px"
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
              <el-button size="small" @click="openEdit(row)">编辑</el-button>
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
          <el-empty v-if="!rows.length" description="暂无数据" />
        </el-card>

        <el-dialog v-model="dialog" title="套餐配置" width="720px">
          <el-form label-width="110px">
            <el-form-item label="名称"><el-input v-model="form.name" /></el-form-item>
            <el-form-item label="月价(分)"><el-input-number v-model="form.priceCents" :min="0" style="width:200px" /></el-form-item>
            <el-form-item label="网站上限"><el-input-number v-model="form.maxServers" :min="0" style="width:200px" /></el-form-item>
            <el-form-item label="域名上限"><el-input-number v-model="form.maxDomains" :min="0" style="width:200px" /></el-form-item>
            <el-form-item label="月流量(GB)"><el-input-number v-model="form.monthlyTrafficGB" :min="0" style="width:200px" /><span class="ms-1 text-muted small">0=不限</span></el-form-item>
            <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
            <el-form-item label="功能开关">
              <div style="width: 100%">
                <div class="text-muted small mb-2">关闭的功能对订阅该套餐的用户不可用（保存配置时服务端强制拦截）；不设置任何开关 = 全部开放。</div>
                <div class="d-flex flex-wrap" style="gap: 10px 18px">
                  <label v-for="[k, label] in FEATURE_LABELS" :key="k" class="d-flex align-items-center" style="gap: 6px; width: 180px">
                    <el-switch v-model="form.feat[k]" :active-value="true" :inactive-value="false" />
                    <span class="small">{{ label }}</span>
                  </label>
                </div>
              </div>
            </el-form-item>
            <el-form-item label="路径规则数"><el-input-number v-model="form.maxLocations" :min="0" style="width:200px" /><span class="ms-1 text-muted small">0=不限</span></el-form-item>
            <el-form-item label="证书数上限"><el-input-number v-model="form.maxCerts" :min="0" style="width:200px" /><span class="ms-1 text-muted small">0=不限</span></el-form-item>
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
