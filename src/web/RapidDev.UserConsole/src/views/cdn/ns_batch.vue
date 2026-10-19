<script setup lang="ts">
/** 批量操作 —— 用户侧 DNS 记录批量管理（复用 /api/user/ns/domains 全量 + /batch 端点） */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
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

const selected = ref<number[]>([])

const loadData = async () => {
  const r = await cdnApi.get('/user/ns/domains') as any
  rows.value = r?.data || []
  return rows.value
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
    <div class="page-header mb-4"><h2 class="page-title">批量操作</h2><div class="text-muted">域名解析 · DNS 记录批量管理</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button size="small" @click="batch('enable')" :disabled="!selected.length">批量启用</el-button>
            <el-button size="small" @click="batch('disable')" :disabled="!selected.length">批量停用</el-button>
            <el-button size="small" type="danger" @click="batch('delete')" :disabled="!selected.length">批量删除</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows" stripe @selection-change="(sel: any[]) => selected = sel.map(x => x.id)">
            <el-table-column type="selection" width="45" />
            <el-table-column prop="name" label="域名" min-width="220" />
            <el-table-column prop="type" label="类型" width="90" />
            <el-table-column prop="value" label="记录值" min-width="180" />
            <el-table-column prop="line" label="线路" width="110" />
            <el-table-column prop="status" label="状态" width="90">
              <template #default="{ row }">{{ row.status === 'active' ? '正常' : '停用' }}</template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无记录，请先到「我的域名」添加" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
