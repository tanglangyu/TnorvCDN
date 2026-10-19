<script setup lang="ts">
/** 线路管理 —— 解析线路（信息页） */
import { computed, ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const pg = ref({ page: 1, size: 20 })
const filterQ = ref('')
const viewRows = (data: any[]) => {
  const start = (pg.value.page - 1) * pg.value.size
  const kw = (filterQ.value || '').trim().toLowerCase()
  const list = kw ? (data || []).filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw)) : (data || [])
  return list.slice(start, start + pg.value.size)
}
const loadData = async () => (await cdnApi.get('/user/ns/routes')) as any
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">线路管理</h2><div class="text-muted">域名解析 · 分线路解析说明</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data }">
        <el-card>
          <el-alert type="info" :closable="false" class="mb-3"
            title="平台按运营商线路（默认/电信/联通/移动/境内/境外）自动分配边缘节点并同步三方 DNS，无需手动配置" />
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows(data)" stripe>
            <el-table-column prop="name" label="域名" min-width="220" />
            <el-table-column prop="line" label="当前线路" width="110" />
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="(data || []).length" :page-size="pg.size" :current-page="pg.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pg.page = p }"
            @size-change="(s2: number) => { pg.size = s2; pg.page = 1 }" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
