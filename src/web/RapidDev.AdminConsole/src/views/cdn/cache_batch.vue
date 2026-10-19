<script setup lang="ts">
/** 刷新预热 —— 缓存刷新（URL/目录/域名/全部），对接 /admin/servers/cache/purge */
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
const form = ref({ matchType: 'url', host: '', value: '' })
const loadData = async () => { rows.value = await cdnApi.get('/admin/servers'); return rows.value }
async function purge() {
  if (form.value.matchType !== 'all' && !form.value.host) return ElMessage.warning('请填写 host')
  try {
    const r = await cdnApi.post('/admin/servers/cache/purge', form.value)
    ElMessage.success(`刷新指令已下发 ${r.queued ?? ''} 个集群`)
  } catch (e: any) { ElMessage.error(e?.response?.data || '刷新失败') }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">刷新预热</h2><div class="text-muted">网站列表 · 缓存刷新（URL/目录/域名/全部）</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card class="mb-3">
          <el-form inline>
            <el-form-item label="类型">
              <el-select v-model="form.matchType" style="width:120px">
                <el-option value="url" label="URL" /><el-option value="directory" label="目录" />
                <el-option value="host" label="域名" /><el-option value="all" label="全部" />
              </el-select>
            </el-form-item>
            <el-form-item label="Host"><el-input v-model="form.host" placeholder="test.example.com" style="width:200px" :disabled="form.matchType==='all'" /></el-form-item>
            <el-form-item label="值"><el-input v-model="form.value" placeholder="路径，如 /img/a.png" style="width:220px" :disabled="form.matchType==='host'||form.matchType==='all'" /></el-form-item>
            <el-form-item><el-button type="primary" @click="purge()">提交刷新</el-button></el-form-item>
          </el-form>
        </el-card>
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新列表</el-button></div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="id" label="ID" width="70" />
            <el-table-column label="域名" min-width="210"><template #default="{ row }">{{ (row.domains||[]).join(', ') }}</template></el-table-column>
            <el-table-column label="缓存" width="100"><template #default="{ row }">{{ row.cache?.enabled ? '启用' : '停用' }}</template></el-table-column>
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
