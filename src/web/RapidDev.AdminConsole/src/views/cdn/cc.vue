<script setup lang="ts">
/** CC事件 —— 对接 TnorvCDN 主控 /admin/cc（WAF 拦截记录聚合） */
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 20

const loadData = async () => {
  const data = await cdnApi.get('/admin/cc', { page: page.value, pageSize }) as any
  rows.value = data?.items || []
  total.value = data?.total || 0
  return data
}

function search() { page.value = 1; gate.value?.refresh?.() }
const fmt = (t: string) => (t ? new Date(t).toLocaleString() : '-')

// CC 解封：按客户端 IP 下发解封指令（经配置快照到全部节点，清除 CC 封禁状态）
const unbanning = ref('')
async function unban(ip: string) {
  try { await ElMessageBox.confirm('确定解封 IP ' + ip + '？解封指令将在节点下次拉取配置（≤10s）时生效。', 'CC 解封', { type: 'warning' }) } catch { return }
  unbanning.value = ip
  try {
    await cdnApi.post('/admin/cc/unban', { ip })
    ElMessage.success('已下发解封指令：' + ip)
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '解封失败') }
  finally { unbanning.value = '' }
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">CC事件</h2><div class="text-muted">网站列表 · WAF/CC 拦截记录</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button type="primary" size="small" @click="search()">查询</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="rows" stripe>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ fmt(row.timestamp) }}</template>
            </el-table-column>
            <el-table-column prop="remoteAddr" label="客户端IP" width="140" />
            <el-table-column prop="host" label="域名" width="170" />
            <el-table-column prop="uri" label="URI" min-width="200" show-overflow-tooltip />
            <el-table-column prop="blockReason" label="拦截原因" min-width="160" show-overflow-tooltip />
            <el-table-column label="操作" width="100">
              <template #default="{ row }">
                <el-button size="small" type="warning" plain :loading="unbanning === row.remoteAddr" @click="unban(row.remoteAddr)">解封</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" layout="prev, pager, next" :total="total" :page-size="pageSize"
            :current-page="page" @current-change="(p: number) => { page = p; gate?.refresh?.() }" />
          <el-empty v-if="!rows.length" description="暂无拦截记录" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
