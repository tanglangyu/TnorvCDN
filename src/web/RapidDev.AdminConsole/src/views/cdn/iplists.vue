<script setup lang="ts">
/** IP名单 —— 对接 TnorvCDN 主控 /admin/iplists（各站点安全配置聚合） */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
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
const typeFilter = ref('')

const loadData = async () => {
  const data = await cdnApi.get('/admin/iplists')
  rows.value = Array.isArray(data) ? data : []
  return rows.value
}

const filtered = computed(() =>
  typeFilter.value ? rows.value.filter((r: any) => r.type === typeFilter.value) : rows.value,
)
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">IP名单</h2><div class="text-muted">网站列表 · 各站点 IP 黑白名单聚合（在站点配置页维护）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
            <el-radio-group v-model="typeFilter" size="small">
              <el-radio-button value="">全部</el-radio-button>
              <el-radio-button value="black">黑名单</el-radio-button>
              <el-radio-button value="white">白名单</el-radio-button>
            </el-radio-group>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="filtered" stripe>
            <el-table-column prop="ip" label="IP / CIDR" min-width="170" />
            <el-table-column label="类型" width="100">
              <template #default="{ row }">
                <el-tag size="small" :type="row.type === 'black' ? 'danger' : 'success'">{{ row.type === 'black' ? '黑名单' : '白名单' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="server" label="所属站点" min-width="200" show-overflow-tooltip />
            <el-table-column prop="note" label="备注" min-width="150" />
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!filtered.length" description="暂无 IP 名单（在「站点配置 → 安全防护」页添加）" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
