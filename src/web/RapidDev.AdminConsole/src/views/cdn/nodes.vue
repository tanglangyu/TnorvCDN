<script setup lang="ts">
/** CDN 节点列表 —— 对接 TnorvCdn 主控 /api/admin/nodes（在线状态/线路/探测结果） */
import { ref } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const nodes = ref<any[]>([])
const probes = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})

const loadData = async () => {
  const [n, m] = await Promise.all([
    cdnApi.get('/admin/nodes'),
    cdnApi.get('/admin/monitor/probes').catch(() => ({ probes: [] })),
  ])
  nodes.value = n
  probes.value = (m as any).probes || []
  return n
}

const nodeProbes = (id: number) => probes.value.filter((p: any) => p.nodeId === id)

// 探测详情弹窗：展示该节点 × 各站点(10min)的可用率/延迟/最近错误
const probeDialog = ref(false)
const probeNode = ref<any>(null)
const probeRows = ref<any[]>([])
function openProbe(row: any) {
  probeNode.value = row
  probeRows.value = nodeProbes(row.id)
  probeDialog.value = true
}
const fmtTime = (t?: string) => (t ? new Date(t).toLocaleString() : '-')

function openEdit(row: any) {
  form.value = JSON.parse(JSON.stringify(row))
  dialog.value = true
}

async function save() {
  await cdnApi.put(`/admin/nodes/${form.value.id}`, { name: form.value.name, line: form.value.line })
  ElMessage.success('已保存')
  dialog.value = false
  gate.value?.refresh?.()
}

const lines = ['default', 'telecom', 'unicom', 'mobile', 'inland', 'overseas']
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">节点IP</h2><div class="text-muted">边缘节点 · 在线状态与智能DNS线路</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="nodes" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column prop="name" label="节点名" width="120" />
            <el-table-column label="公网 IP" min-width="160">
              <template #default="{ row }">{{ (row.publicIps || []).join(', ') || '-' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="row.isOnline ? 'success' : 'danger'" size="small">{{ row.isOnline ? '在线' : '离线' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="role" label="角色" width="80" />
            <el-table-column prop="line" label="DNS线路" width="110" />
            <el-table-column prop="version" label="版本" width="90" />
            <el-table-column label="探测(10min)" width="200">
              <template #default="{ row }">
                <span v-for="p in nodeProbes(row.id).slice(0, 2)" :key="p.serverId" class="me-2">
                  <el-tag :type="(p.okRate ?? 0) >= 90 ? 'success' : 'warning'" size="small">
                    {{ Math.round(p.okRate) }}% · {{ Math.round(p.avgLatencyMs) }}ms
                  </el-tag>
                </span>
                <span v-if="nodeProbes(row.id).length > 2" class="text-muted me-2">+{{ nodeProbes(row.id).length - 2 }}</span>
                <span v-if="!nodeProbes(row.id).length" class="text-muted me-2">-</span>
                <el-button size="small" link type="primary" @click="openProbe(row)">详情</el-button>
              </template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" @click="openEdit(row)">编辑</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </el-card>

        <el-dialog v-model="probeDialog" :title="`探测详情 · ${probeNode?.name || ''}（最近10分钟）`" width="760px">
          <div class="text-muted small mb-2">
            每个边缘节点定期探测其可服务的各站点（serverId 维度）的可用性与延迟，聚合最近 10 分钟的探测记录。
          </div>
          <el-table :data="probeRows" stripe size="small">
            <el-table-column prop="serverId" label="站点ID" width="80" />
            <el-table-column label="可用率" width="120">
              <template #default="{ row }">
                <el-tag :type="(row.okRate ?? 0) >= 90 ? 'success' : (row.okRate ?? 0) >= 50 ? 'warning' : 'danger'" size="small">
                  {{ Math.round(row.okRate ?? 0) }}%
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="平均延迟" width="110">
              <template #default="{ row }">{{ Math.round(row.avgLatencyMs ?? 0) }} ms</template>
            </el-table-column>
            <el-table-column label="最近错误" min-width="200">
              <template #default="{ row }"><span class="text-muted">{{ row.lastError || '-' }}</span></template>
            </el-table-column>
            <el-table-column label="最近探测时间" width="170">
              <template #default="{ row }">{{ fmtTime(row.lastSeen) }}</template>
            </el-table-column>
          </el-table>
          <el-empty v-if="!probeRows.length" description="该节点最近 10 分钟无探测记录" />
          <template #footer>
            <el-button @click="probeDialog = false">关闭</el-button>
          </template>
        </el-dialog>

        <el-dialog v-model="dialog" title="编辑节点" width="480px">
          <el-form label-width="110px">
            <el-form-item label="节点名"><el-input v-model="form.name" /></el-form-item>
            <el-form-item label="DNS线路">
              <el-select v-model="form.line" style="width:240px">
                <el-option v-for="l in lines" :key="l" :value="l" :label="l" />
              </el-select>
            </el-form-item>
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
