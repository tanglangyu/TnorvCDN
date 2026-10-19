<script setup lang="ts">
/**
 * IP 盒查询 —— 通用设计
 * 单 IP 归属地解析 + 近期访问记录（站点/URL/状态码/缓存命中）。
 */
import { ref } from 'vue'
import { http } from '@/utils/request'

const ip = ref('')
const hours = ref(24)
const loading = ref(false)
const result = ref<any>(null)
const searched = ref(false)

const query = async () => {
  if (!ip.value.trim()) return
  loading.value = true
  searched.value = true
  try {
    result.value = await http.get('/admin/ipbox', { ip: ip.value.trim(), hours: hours.value })
  } finally {
    loading.value = false
  }
}
const fmtTime = (t: any) => (t ? new Date(t * 1000).toLocaleString() : '—')
</script>

<template>
  <div>
    <div class="page-header mb-3">
      <div class="col-auto">
        <h2 class="page-title">IP 盒</h2>
        <div class="text-muted">IP 归属地与近期访问行为查询（风控排查工具）</div>
      </div>
    </div>

    <el-card class="mb-3">
      <div class="d-flex align-items-center gap-2">
        <el-input v-model="ip" placeholder="输入 IP 地址，如 1.2.3.4" style="width: 320px" clearable @keyup.enter="query" />
        <el-select v-model="hours" style="width: 120px">
          <el-option :value="1" label="近 1 小时" />
          <el-option :value="6" label="近 6 小时" />
          <el-option :value="24" label="近 24 小时" />
          <el-option :value="168" label="近 7 天" />
        </el-select>
        <el-button type="primary" :loading="loading" @click="query">查询</el-button>
      </div>
    </el-card>

    <template v-if="result">
      <el-card class="mb-3">
        <el-descriptions :column="3" border>
          <el-descriptions-item label="IP">{{ result.ip }}</el-descriptions-item>
          <el-descriptions-item label="国家/地区">{{ result.country || '未知' }}</el-descriptions-item>
          <el-descriptions-item label="省份">{{ result.province || '—' }}</el-descriptions-item>
          <el-descriptions-item label="近期请求数">{{ result.requestCount }}</el-descriptions-item>
          <el-descriptions-item label="涉及站点" :span="2">
            <el-tag v-for="s in result.servers || []" :key="s.id" size="small" class="me-1">
              {{ (s.domains && s.domains[0]) || '站点#' + s.id }}
            </el-tag>
            <span v-if="!(result.servers || []).length" class="text-muted">无</span>
          </el-descriptions-item>
        </el-descriptions>
      </el-card>

      <el-card v-loading="loading">
        <template #header><b>近期访问记录（最多 50 条）</b></template>
        <el-table :data="result.recent || []" stripe size="small">
          <el-table-column label="时间" width="170"><template #default="{ row }">{{ fmtTime(row.timestamp) }}</template></el-table-column>
          <el-table-column prop="host" label="域名" min-width="150" show-overflow-tooltip />
          <el-table-column prop="uri" label="URL" min-width="220" show-overflow-tooltip />
          <el-table-column prop="method" label="方法" width="70" />
          <el-table-column label="状态码" width="85">
            <template #default="{ row }">
              <el-tag :type="row.status < 400 ? 'success' : row.status < 500 ? 'warning' : 'danger'" size="small">{{ row.status }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="cacheStatus" label="缓存" width="85" />
        </el-table>
        <el-empty v-if="!(result.recent || []).length && !loading" description="该时间范围内无访问记录" />
      </el-card>
    </template>
    <el-empty v-else-if="searched && !loading" description="查询完成，无数据" />
  </div>
</template>
