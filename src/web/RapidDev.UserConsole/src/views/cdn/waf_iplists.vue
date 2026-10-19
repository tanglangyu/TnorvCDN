<script setup lang="ts">
/** 拦截IP（U4）—— 近24h 拦截来源聚合 + 加黑名单（写本人站点 Security.IpBlacklist） */
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])

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

const servers = ref<any[]>([])
const adding = ref('')
const blDialog = ref(false)
const blForm = ref({ ip: '', note: '', serverId: 0 })

const loadData = async () => {
  const resp = (await cdnApi.get('/user/waf/logs', { page: 1, pageSize: 200 })) as any
  const d = resp?.data ?? resp
  const m = new Map<string, { ip: string; count: number; reasons: Set<string>; last: string }>()
  for (const l of d?.items || []) {
    const e = m.get(l.remoteAddr) || { ip: l.remoteAddr, count: 0, reasons: new Set<string>(), last: '' }
    e.count++
    if (l.blockReason) e.reasons.add(l.blockReason)
    if (l.timestamp && l.timestamp > e.last) e.last = l.timestamp
    m.set(l.remoteAddr, e)
  }
  rows.value = [...m.values()].map((e) => ({ ip: e.ip, count: e.count, reason: [...e.reasons].join('、'), last: e.last }))
    .sort((a, b) => b.count - a.count)
  return rows.value
}

const openBlacklist = (ip: string) => {
  blForm.value = { ip, note: '来自拦截记录', serverId: 0 }
  blDialog.value = true
  cdnApi.get('/user/servers', { page: 1, pageSize: 100 }).then((r: any) => {
    const d = r?.data ?? r
    servers.value = (Array.isArray(d) ? d : d?.items || []).filter((s: any) => s.enabled !== false)
  })
}

async function saveBlacklist() {
  adding.value = blForm.value.ip
  try {
    await cdnApi.post('/user/waf/blacklist', {
      ip: blForm.value.ip,
      note: blForm.value.note || null,
      serverId: blForm.value.serverId || null,
    })
    ElMessage.success('已加入黑名单并下发到节点')
    blDialog.value = false
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || '操作失败')
  } finally { adding.value = '' }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">拦截IP</h2><div class="text-muted">WAF安全 · 近期拦截来源统计，可一键加入站点 IP 黑名单</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button>
            <span class="text-muted small ms-2">基于近 200 条拦截记录聚合</span></div>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows" stripe>
            <el-table-column prop="ip" label="来源IP" width="160" />
            <el-table-column prop="count" label="拦截次数" width="110" sortable />
            <el-table-column prop="reason" label="拦截原因" min-width="220" show-overflow-tooltip />
            <el-table-column label="最近拦截" width="170">
              <template #default="{ row }">{{ row.last ? new Date(row.last).toLocaleString() : '-' }}</template>
            </el-table-column>
            <el-table-column label="操作" width="130" fixed="right">
              <template #default="{ row }">
                <el-button size="small" type="danger" plain :loading="adding === row.ip" @click="openBlacklist(row.ip)">加黑名单</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无拦截记录（网站运行正常）" />
        </el-card>

        <el-dialog v-model="blDialog" title="加入 IP 黑名单" width="480px">
          <el-form label-width="90px">
            <el-form-item label="IP"><el-input v-model="blForm.ip" disabled /></el-form-item>
            <el-form-item label="备注"><el-input v-model="blForm.note" placeholder="可选" /></el-form-item>
            <el-form-item label="应用范围">
              <el-select v-model="blForm.serverId" style="width: 100%">
                <el-option :value="0" label="全部启用中的站点" />
                <el-option v-for="s in servers" :key="s.id" :value="s.id" :label="(s.domains || []).join(', ') || ('站点#' + s.id)" />
              </el-select>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="blDialog = false">取消</el-button>
            <el-button type="danger" :loading="adding === blForm.ip" @click="saveBlacklist">加入黑名单</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
