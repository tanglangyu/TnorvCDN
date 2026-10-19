<template>
  <div>
    <el-row :gutter="16">
      <el-col :span="6"><el-card><el-statistic title="总请求数 (24h)" :value="stats.totalRequests" /></el-card></el-col>
      <el-col :span="6"><el-card><el-statistic title="缓存命中率" :value="stats.cacheHitRate" suffix="%" /></el-card></el-col>
      <el-col :span="6"><el-card><el-statistic title="4xx 错误" :value="stats.status4xx" /></el-card></el-col>
      <el-col :span="6"><el-card><el-statistic title="5xx 错误" :value="stats.status5xx" /></el-card></el-col>
    </el-row>

    <el-row :gutter="16" style="margin-top:16px">
      <el-col :span="16">
        <el-card header="流量趋势（请求数 / 缓存命中 / 攻击拦截）">
          <div ref="trendChart" style="height:320px"></div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card header="状态码分布">
          <div ref="statusChart" style="height:320px"></div>
        </el-card>
      </el-col>
    </el-row>
    <el-row :gutter="16" style="margin-top:16px">
      <el-col :span="8">
        <el-card header="攻击拦截 (WAF/CC)">
          <div ref="attackChart" style="height:280px"></div>
        </el-card>
      </el-col>
      <el-col :span="16">
        <el-card header="在线节点">
          <el-table :data="nodes" size="small">
            <el-table-column prop="name" label="节点" />
            <el-table-column label="状态">
              <template #default="{ row }">
                <el-tag :type="row.isOnline ? 'success' : 'danger'" size="small">{{ row.isOnline ? '在线' : '离线' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="version" label="版本" width="90" />
            <el-table-column label="最后心跳" width="170">
              <template #default="{ row }">{{ row.lastHeartbeat ? new Date(row.lastHeartbeat).toLocaleString() : '-' }}</template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import * as echarts from 'echarts'
import api from '../api'

const stats = ref({ totalRequests: 0, cacheHitRate: 0, status4xx: 0, status5xx: 0 })
const nodes = ref([])
const trendChart = ref(null)
const statusChart = ref(null)
const attackChart = ref(null)

onMounted(async () => {
  const to = new Date().toISOString()
  const from = new Date(Date.now() - 86400000).toISOString()
  try { stats.value = await api.get('/admin/logs/stats', { params: { from, to } }) } catch {}
  try { nodes.value = await api.get('/admin/nodes') } catch {}

  // 全量日志聚合：趋势（按小时三系列）+ 状态码饼图 + 攻击拦截
  try {
    const logs = await api.get('/admin/logs', { params: { from, to, size: 500 } })
    const items = logs.items || []
    const hours = {}, hits = {}, blocks = {}
    const statusDist = {}
    const attackDist = {}
    for (const l of items) {
      const key = new Date(l.timestamp).getHours() + ':00'
      hours[key] = (hours[key] || 0) + 1
      if (l.cacheStatus === 'HIT') hits[key] = (hits[key] || 0) + 1
      if (l.wafAction === 'block' || l.status === 403 || l.status === 429) {
        blocks[key] = (blocks[key] || 0) + 1
        const reason = l.blockReason || 'other'
        const tag = reason.split(':')[0].trim() || 'other'
        attackDist[tag] = (attackDist[tag] || 0) + 1
      }
      const band = l.status < 300 ? '2xx' : l.status < 400 ? '3xx' : l.status < 500 ? '4xx' : l.status ? '5xx' : '未知'
      statusDist[band] = (statusDist[band] || 0) + 1
    }
    const sorted = Object.keys(hours).sort((a, b) => parseInt(a) - parseInt(b))

    echarts.init(trendChart.value).setOption({
      tooltip: { trigger: 'axis' },
      legend: { data: ['总请求', '缓存命中', '拦截'] },
      grid: { left: 40, right: 20, top: 40, bottom: 30 },
      xAxis: { type: 'category', data: sorted },
      yAxis: { type: 'value' },
      series: [
        { name: '总请求', type: 'line', smooth: true, areaStyle: { opacity: 0.15 }, data: sorted.map(h => hours[h] || 0) },
        { name: '缓存命中', type: 'line', smooth: true, areaStyle: { opacity: 0.15 }, data: sorted.map(h => hits[h] || 0) },
        { name: '拦截', type: 'line', smooth: true, data: sorted.map(h => blocks[h] || 0), color: '#e6593c' },
      ],
    })

    echarts.init(statusChart.value).setOption({
      tooltip: { trigger: 'item' },
      series: [{ type: 'pie', radius: ['40%', '70%'],
        data: Object.entries(statusDist).map(([name, value]) => ({ name, value, itemStyle: { color: { '2xx': '#67c23a', '3xx': '#909399', '4xx': '#e6a23c', '5xx': '#f56c6c' }[name] } })) }],
    })

    const attackData = Object.entries(attackDist).map(([name, value]) => ({ name, value }))
    echarts.init(attackChart.value).setOption({
      tooltip: { trigger: 'item' },
      series: [{ type: 'pie', radius: '65%', data: attackData.length ? attackData : [{ name: '暂无拦截', value: 1, itemStyle: { color: '#dcdfe6' } }] }],
    })
  } catch {}
})
</script>
