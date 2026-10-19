<script setup lang="ts">
/**
 * 服务监控 —— 展示后端进程的 CPU / 内存 / 磁盘 / 运行时指标。
 * 数据来源:GET /api/monitor/server
 */
import { ref, onMounted } from 'vue'
import { TCard, TButton } from '@/components/t'
import { monitorApi, type ServerInfo } from '@/api/monitor'
import { formatTime } from '@/utils/time'

const loading = ref(false)
const info = ref<ServerInfo | null>(null)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    info.value = (await monitorApi.server()) as ServerInfo
  } catch (e: any) {
    error.value = e?.message || '加载失败'
  } finally {
    loading.value = false
  }
}

/** 把秒数格式化为 x天 x小时 x分 */
function formatRunTime(s: string | undefined): string {
  if (!s) return '-'
  // 后端返回 TimeSpan 的 "1.02:03:04" 或 "02:03:04" 格式
  const parts = s.split(':').map(Number)
  let days = 0, hours = 0, minutes = 0, seconds = 0
  if (parts.length === 4) { [days, hours, minutes, seconds] = parts }
  else if (parts.length === 3) { [hours, minutes, seconds] = parts }
  const seg: string[] = []
  if (days) seg.push(`${days}天`)
  if (hours) seg.push(`${hours}小时`)
  if (minutes) seg.push(`${minutes}分`)
  seg.push(`${seconds}秒`)
  return seg.join(' ')
}

function usageClass(rate: number): string {
  if (rate >= 90) return 'bg-danger'
  if (rate >= 70) return 'bg-warning'
  return 'bg-success'
}

onMounted(load)
</script>

<template>
  <TCard title="服务监控">
    <template #actions>
      <TButton size="sm" variant="ghost-secondary" :loading="loading" @click="load"><i class="ti ti-refresh"></i></TButton>
    </template>

    <div v-if="loading" class="text-center text-muted py-4">
      <div class="spinner-border spinner-border-sm me-2"></div>加载中...
    </div>
    <div v-else-if="error" class="alert alert-danger">{{ error }}</div>

    <div v-else-if="info" class="row g-3">
      <!-- 主机信息 -->
      <div class="col-md-6">
        <div class="card">
          <div class="card-header"><h3 class="card-title">主机信息</h3></div>
          <div class="card-table">
            <table class="table table-vcenter">
              <tbody>
                <tr><td class="text-muted w-25">主机名</td><td>{{ info.host.hostname }}</td></tr>
                <tr><td class="text-muted">操作系统</td><td>{{ info.host.osName }}</td></tr>
                <tr><td class="text-muted">.NET 版本</td><td>{{ info.host.dotnetVersion }}</td></tr>
                <tr><td class="text-muted">CPU 核心数</td><td>{{ info.host.processorCount }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- 运行时 -->
      <div class="col-md-6">
        <div class="card">
          <div class="card-header"><h3 class="card-title">运行时</h3></div>
          <div class="card-table">
            <table class="table table-vcenter">
              <tbody>
                <tr><td class="text-muted w-25">运行时</td><td>{{ info.jvm.runtimeName }}</td></tr>
                <tr><td class="text-muted">启动时间</td><td>{{ formatTime(info.jvm.startTime) }}</td></tr>
                <tr><td class="text-muted">运行时长</td><td>{{ formatRunTime(info.jvm.runTime) }}</td></tr>
                <tr><td class="text-muted">线程数</td><td>{{ info.jvm.threadCount }}</td></tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- CPU -->
      <div class="col-md-6">
        <div class="card">
          <div class="card-header"><h3 class="card-title">CPU 使用率</h3></div>
          <div class="card-body">
            <div class="d-flex align-items-center mb-2">
              <span class="me-2" style="min-width: 56px">{{ info.sys.cpuUsage }}%</span>
              <div class="progress progress-sm flex-fill">
                <div class="progress-bar" :class="usageClass(info.sys.cpuUsage)" :style="{ width: info.sys.cpuUsage + '%' }"></div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 内存 -->
      <div class="col-md-6">
        <div class="card">
          <div class="card-header"><h3 class="card-title">内存</h3></div>
          <div class="card-body">
            <div class="mb-2 text-muted">已用 {{ info.jvm.usedMemoryMb }} MB / 总计 {{ info.jvm.totalMemoryMb }} MB</div>
            <div class="progress progress-sm">
              <div class="progress-bar" :class="usageClass(info.jvm.totalMemoryMb ? (info.jvm.usedMemoryMb / info.jvm.totalMemoryMb * 100) : 0)" :style="{ width: (info.jvm.totalMemoryMb ? (info.jvm.usedMemoryMb / info.jvm.totalMemoryMb * 100) : 0) + '%' }"></div>
            </div>
          </div>
        </div>
      </div>

      <!-- 磁盘 -->
      <div class="col-12">
        <div class="card">
          <div class="card-header"><h3 class="card-title">磁盘</h3></div>
          <div class="card-table table-responsive">
            <table class="table table-vcenter card-table">
              <thead><tr><th>盘符</th><th>总容量(GB)</th><th>可用(GB)</th><th>已用率</th></tr></thead>
              <tbody>
                <tr v-for="d in info.sys.disk" :key="d.name">
                  <td>{{ d.name }}</td>
                  <td>{{ d.totalGb }}</td>
                  <td>{{ d.freeGb }}</td>
                  <td>
                    <div class="d-flex align-items-center">
                      <span class="me-2" style="min-width: 48px">{{ d.usageRate }}%</span>
                      <div class="progress progress-sm" style="min-width: 160px">
                        <div class="progress-bar" :class="usageClass(d.usageRate)" :style="{ width: d.usageRate + '%' }"></div>
                      </div>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </TCard>
</template>
