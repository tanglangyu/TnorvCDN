<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { healthApi } from '@/api/features'
import { formatTime } from '@/utils/time'
import { TCard, TButton } from '@/components/t'

const loading = ref(false)
const data = ref<any>(null)

async function load() {
  loading.value = true
  try { data.value = await healthApi.dashboard() } finally { loading.value = false }
}

// 后端 uptime 是 TimeSpan 字符串(序列化后无法直接运算),用 startTime 计算运行秒数
function uptimeSeconds(startTime: any): number {
  const t = Date.now() - new Date(startTime).getTime()
  return isNaN(t) ? 0 : Math.max(0, Math.floor(t / 1000))
}
function fmtUptime(startTime: any): string {
  const s = uptimeSeconds(startTime)
  const d = Math.floor(s / 86400)
  const h = Math.floor((s % 86400) / 3600)
  const m = Math.floor((s % 3600) / 60)
  if (d > 0) return `${d}d ${h}h ${m}m`
  if (h > 0) return `${h}h ${m}m`
  return `${m}m ${s % 60}s`
}

onMounted(load)
</script>

<template>
  <TCard title="健康检查仪表盘">
    <template #actions><TButton variant="ghost-secondary" @click="load" :loading="loading"><i class="ti ti-refresh"></i></TButton></template>
    <div v-if="data">
      <div class="row g-3 mb-4">
        <div class="col-md-3">
          <div class="card bg-azure-lt">
            <div class="card-body text-center">
              <div class="text-muted small">数据库</div>
              <div class="h2" :class="data.checks.database.status === 'up' ? 'text-success' : 'text-danger'">
                <i class="ti" :class="data.checks.database.status === 'up' ? 'ti-check' : 'ti-x'"></i>
              </div>
              <div class="text-muted small">{{ data.checks.database.latencyMs }}ms</div>
            </div>
          </div>
        </div>
        <div class="col-md-3">
          <div class="card bg-violet-lt">
            <div class="card-body text-center">
              <div class="text-muted small">Redis</div>
              <div class="h2" :class="data.checks.redis.status === 'up' ? 'text-success' : 'text-danger'">
                <i class="ti" :class="data.checks.redis.status === 'up' ? 'ti-check' : 'ti-x'"></i>
              </div>
            </div>
          </div>
        </div>
        <div class="col-md-3">
          <div class="card bg-green-lt">
            <div class="card-body text-center">
              <div class="text-muted small">CPU 使用率</div>
              <div class="h2">{{ data.system.cpuUsage }}%</div>
              <div class="text-muted small">{{ data.system.cpuCount }} 核</div>
            </div>
          </div>
        </div>
        <div class="col-md-3">
          <div class="card bg-orange-lt">
            <div class="card-body text-center">
              <div class="text-muted small">内存使用</div>
              <div class="h2">{{ data.process.memoryUsageRate }}%</div>
              <div class="text-muted small">{{ data.process.usedMemoryMb }} / {{ data.process.totalMemoryMb }} MB</div>
            </div>
          </div>
        </div>
      </div>

      <div class="row g-3">
        <div class="col-md-6">
          <h5>系统信息</h5>
          <table class="table table-sm">
            <tr><td class="text-muted">主机名</td><td>{{ data.system.hostname }}</td></tr>
            <tr><td class="text-muted">操作系统</td><td>{{ data.system.os }}</td></tr>
            <tr><td class="text-muted">.NET 版本</td><td>{{ data.system.dotnet }}</td></tr>
            <tr><td class="text-muted">CPU 核数</td><td>{{ data.system.cpuCount }}</td></tr>
            <tr><td class="text-muted">CPU 使用率</td><td>{{ data.system.cpuUsage }}%</td></tr>
          </table>
        </div>
        <div class="col-md-6">
          <h5>进程信息</h5>
          <table class="table table-sm">
            <tr><td class="text-muted">PID</td><td>{{ data.process.processId }}</td></tr>
            <tr><td class="text-muted">启动时间</td><td>{{ formatTime(data.process.startTime) }}</td></tr>
            <tr><td class="text-muted">运行时长</td><td>{{ fmtUptime(data.process.startTime) }}</td></tr>
            <tr><td class="text-muted">内存</td><td>{{ data.process.usedMemoryMb }} MB / {{ data.process.totalMemoryMb }} MB ({{ data.process.memoryUsageRate }}%)</td></tr>
            <tr><td class="text-muted">线程数</td><td>{{ data.process.threadCount }}</td></tr>
            <tr><td class="text-muted">GC 回收</td><td>Gen0={{ data.process.gcCollections.gen0 }} Gen1={{ data.process.gcCollections.gen1 }} Gen2={{ data.process.gcCollections.gen2 }}</td></tr>
          </table>
        </div>
      </div>

      <h5 class="mt-3">磁盘</h5>
      <table class="table table-sm">
        <thead><tr><th>盘符</th><th>总量</th><th>可用</th><th>使用率</th></tr></thead>
        <tbody>
          <tr v-for="d in data.system.disks" :key="d.name">
            <td>{{ d.name }}</td><td>{{ d.totalGb }} GB</td><td>{{ d.freeGb }} GB</td>
            <td><span :class="d.usageRate > 80 ? 'text-danger' : 'text-success'">{{ d.usageRate }}%</span></td>
          </tr>
        </tbody>
      </table>
    </div>
  </TCard>
</template>
