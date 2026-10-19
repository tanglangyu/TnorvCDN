<script setup lang="ts">
/** 区域监控 —— 对接 TnorvCdn 主控 /api/admin/monitor/probes */
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const loadData = async () => cdnApi.get('/admin/monitor/probes')

const fmt = (t: string) => t ? new Date(t).toLocaleString() : '-'
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">区域监控</h2><div class="text-muted">边缘节点 · 各节点对站点的可访问性探测</div></div>
    <CdnGate :load-data="loadData">
      <template #default="{ data, refresh }">
        <el-card>
          <div class="mb-3"><el-button size="small" @click="refresh()">刷新</el-button></div>
          <el-table :data="data?.probes || []" stripe>
            <el-table-column prop="serverId" label="网站ID" width="80" />
            <el-table-column prop="nodeId" label="节点ID" width="80" />
            <el-table-column label="可用率" width="110">
              <template #default="{ row }">
                <el-tag :type="row.okRate >= 99 ? 'success' : row.okRate >= 90 ? 'warning' : 'danger'" size="small">{{ Math.round(row.okRate) }}%</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="平均延迟" width="110">
              <template #default="{ row }">{{ Math.round(row.avgLatencyMs) }} ms</template>
            </el-table-column>
            <el-table-column prop="lastError" label="最近错误" show-overflow-tooltip />
            <el-table-column label="最近探测" width="170">
              <template #default="{ row }">{{ fmt(row.lastSeen) }}</template>
            </el-table-column>
          </el-table>
          <el-empty v-if="!(data?.probes || []).length" description="暂无探测数据（节点每 15 秒上报一轮）" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
