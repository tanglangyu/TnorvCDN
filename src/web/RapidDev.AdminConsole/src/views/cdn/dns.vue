<script setup lang="ts">
/** DNS 集群 —— 对接 TnorvCdn 主控 /api/admin/clusters（DNS 子域名 + 同步）与 /admin/dns/status */
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
  let list = clusters.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})
const clusters = ref<any[]>([])
const status = ref<any>({})

const loadData = async () => {
  const [c, s] = await Promise.all([
    cdnApi.get('/admin/clusters'),
    cdnApi.get('/admin/dns/status').catch(() => ({ enabled: false })),
  ])
  clusters.value = c
  status.value = s
  return c
}

async function sync(row: any) {
  await cdnApi.post(`/admin/dns/clusters/${row.id}/sync`)
  ElMessage.success(`已同步 ${row.name} 的解析记录`)
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">集群列表</h2><div class="text-muted">域名解析 · 三方 DNS 自动 CNAME / 分线路解析</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card class="mb-3">
          <el-alert :type="status.enabled ? 'success' : 'warning'" :closable="false"
            :title="status.enabled ? '三方 DNS 已启用（聚合DNS/阿里云/DNSPod/华为云/Cloudflare）' : '三方 DNS 未启用（在主控 appsettings.json 的 Dns 节配置）'" />
        </el-card>
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column prop="name" label="集群" />
            <el-table-column prop="dnsDomain" label="DNS 子域名（CNAME 目标）" />
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" @click="sync(row)">同步解析</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
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
