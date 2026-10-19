<script setup lang="ts">
/** 刷新预热 —— 刷新我站点缓存（/api/user/servers/cache/purge） */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'
const gate = ref()
const myServers = ref<any[]>([])

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = myServers.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})

const form = ref({ matchType: 'host', host: '', value: '' })
const loadData = async () => { myServers.value = await cdnApi.get('/user/servers'); return myServers.value }
async function purge() {
  if (form.value.matchType !== 'all' && !form.value.host) return ElMessage.warning('请选择域名')
  try {
    const r = await cdnApi.post('/user/servers/cache/purge', form.value) as any
    ElMessage.success(r.message || '刷新指令已下发')
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '刷新失败') }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">刷新预热</h2><div class="text-muted">CDN加速 · 刷新我站点边缘缓存</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card class="mb-3">
          <el-form inline>
            <el-form-item label="类型">
              <el-select v-model="form.matchType" style="width:120px">
                <el-option value="url" label="URL" /><el-option value="directory" label="目录" />
                <el-option value="host" label="域名" /><el-option value="all" label="我的全部" />
              </el-select>
            </el-form-item>
            <el-form-item label="域名">
              <el-select v-model="form.host" style="width:220px" filterable :disabled="form.matchType==='all'">
                <template v-for="s in myServers" :key="s.id">
                  <el-option v-for="d in (s.domains||[])" :key="s.id+'-'+d" :value="d" :label="d" />
                </template>
              </el-select>
            </el-form-item>
            <el-form-item label="路径"><el-input v-model="form.value" placeholder="/path/file" style="width:200px" :disabled="form.matchType==='host'||form.matchType==='all'" /></el-form-item>
            <el-form-item><el-button type="primary" @click="purge()">提交刷新</el-button></el-form-item>
          </el-form>
        </el-card>
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新站点列表</el-button></div>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows" stripe>
            <el-table-column label="域名" min-width="210"><template #default="{ row }">{{ (row.domains||[]).join(', ') }}</template></el-table-column>
            <el-table-column label="节点在线" width="110"><template #default="{ row }">{{ row.nodesOnline }} 在线</template></el-table-column>
            <el-table-column label="状态" width="90"><template #default="{ row }"><el-tag :type="row.enabled?'success':'info'" size="small">{{ row.enabled?'运行中':'停用' }}</el-tag></template></el-table-column>
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
