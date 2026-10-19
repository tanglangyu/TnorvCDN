<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { opsApi } from '@/api/features'
import { TCard, TButton, TInput, TSwitch } from '@/components/t'
import { formatTime } from '@/utils/time'

const loading = ref(false)
const backups = ref<any[]>([])
const backupName = ref('')
const statusMsg = ref('')
const statusType = ref<'success' | 'danger'>('success')

// 定时备份配置
const scheduleLoading = ref(false)
const schedule = ref({ enabled: false, cron: '0 0 2 * * ?', retentionDays: 7 })
const scheduleLastRun = ref<any>(null)
const scheduleMsg = ref('')
const cronPresets = [
  { label: '每天 2:00', value: '0 0 2 * * ?' },
  { label: '每 6 小时', value: '0 0 0/6 * * ?' },
  { label: '每周日 2:00', value: '0 0 2 ? * SUN' },
  { label: '每月 1 日 2:00', value: '0 0 2 1 * ?' },
]

async function load() {
  loading.value = true
  statusMsg.value = ''
  try { backups.value = (await opsApi.backups()) || [] } catch (e: any) { statusMsg.value = '加载失败: ' + (e?.message || ''); statusType.value = 'danger' } finally { loading.value = false }
}

async function createBackup() {
  loading.value = true
  statusMsg.value = ''
  try {
    const name = backupName.value?.trim() || undefined
    const r: any = await opsApi.createBackup(name)
    if (r?.error) { statusMsg.value = '备份失败: ' + r.error; statusType.value = 'danger'; return }
    backupName.value = ''
    statusMsg.value = `备份成功: ${r.fileName} (${r.sizeMb} MB, ${r.tables} 表)`
    statusType.value = 'success'
    await load()
  } catch (e: any) { statusMsg.value = '备份错误: ' + (e?.message || '未知错误'); statusType.value = 'danger' } finally { loading.value = false }
}

async function deleteBackup(name: string) {
  if (!confirm(`确定删除 ${name}?`)) return
  try { await opsApi.deleteBackup(name); await load() } catch (e: any) { statusMsg.value = '删除失败: ' + (e?.message || ''); statusType.value = 'danger' }
}

async function loadSchedule() {
  scheduleLoading.value = true
  try {
    const r: any = await opsApi.backupSchedule()
    schedule.value = { enabled: r.enabled, cron: r.cron, retentionDays: r.retentionDays }
    scheduleLastRun.value = { at: r.lastRunAt, status: r.lastStatus }
  } catch {} finally { scheduleLoading.value = false }
}

async function saveSchedule() {
  scheduleMsg.value = ''
  try {
    const r: any = await opsApi.updateBackupSchedule(schedule.value)
    scheduleLastRun.value = { at: r.lastRunAt, status: r.lastStatus }
    scheduleMsg.value = '定时备份配置已保存并重新调度'
  } catch (e: any) { scheduleMsg.value = '保存失败: ' + (e?.message || 'Cron 表达式无效'); }
}

function formatSize(bytes: number) {
  if (!bytes || bytes < 0) return '0 B'
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / 1048576).toFixed(2) + ' MB'
}

onMounted(() => { load(); loadSchedule() })
</script>

<template>
  <div class="row g-3">
    <div class="col-12">
      <h3 class="page-title mb-3">数据库备份管理</h3>
    </div>

    <!-- 立即备份 -->
    <div class="col-md-4">
      <TCard title="立即备份">
        <div class="mb-2">
          <label class="form-label small text-muted">备份名称(可选)</label>
          <TInput v-model="backupName" placeholder="留空自动生成时间戳名" />
        </div>
        <div class="text-muted small mb-3">使用纯 C# 逻辑导出,无需 mysqldump,导出 DDL + 数据并 gzip 压缩。</div>
        <TButton variant="primary" class="w-100" @click="createBackup" :disabled="loading">
          <i class="ti ti-player-play me-1"></i>{{ loading ? '备份中...' : '立即备份' }}
        </TButton>
        <p v-if="statusMsg" class="small mt-2 mb-0" :class="statusType === 'danger' ? 'text-danger' : 'text-success'">
          {{ statusMsg }}
        </p>
      </TCard>
    </div>

    <!-- 定时备份 -->
    <div class="col-md-8">
      <TCard title="定时备份">
        <template #actions>
          <span v-if="scheduleLastRun?.status" class="badge" :class="{
            'bg-success-lt': scheduleLastRun.status === 'success',
            'bg-danger-lt': scheduleLastRun.status === 'failed',
            'bg-warning-lt': scheduleLastRun.status === 'running',
          }">
            <i class="ti ti-clock me-1"></i>{{ scheduleLastRun.status === 'success' ? '上次成功' : scheduleLastRun.status === 'failed' ? '上次失败' : '运行中' }}
            <span v-if="scheduleLastRun.at" class="ms-1">{{ formatTime(scheduleLastRun.at) }}</span>
          </span>
        </template>
        <div class="row g-3">
          <div class="col-md-5 d-flex align-items-center">
            <div class="form-check form-switch me-3">
              <TSwitch v-model="schedule.enabled" />
            </div>
            <label class="form-check-label">启用定时备份</label>
          </div>
          <div class="col-md-4">
            <label class="form-label small text-muted">Cron 表达式</label>
            <TInput v-model="schedule.cron" placeholder="0 0 2 * * ?" />
          </div>
          <div class="col-md-3">
            <label class="form-label small text-muted">保留天数(0=永久)</label>
            <TInput v-model.number="schedule.retentionDays" type="number" />
          </div>
          <div class="col-12">
            <div class="d-flex flex-wrap gap-1 mb-3">
              <span class="text-muted small me-1">预设:</span>
              <button v-for="p in cronPresets" :key="p.value" class="btn btn-sm btn-ghost-secondary"
                @click="schedule.cron = p.value" :class="{ 'active': schedule.cron === p.value }">{{ p.label }}</button>
            </div>
            <TButton variant="primary" @click="saveSchedule" :disabled="scheduleLoading">
              <i class="ti ti-device-floppy me-1"></i>保存并重新调度
            </TButton>
            <span v-if="scheduleMsg" class="small ms-2" :class="scheduleMsg.includes('失败') ? 'text-danger' : 'text-success'">{{ scheduleMsg }}</span>
          </div>
        </div>
      </TCard>
    </div>

    <!-- 备份列表 -->
    <div class="col-12">
      <TCard>
        <template #title><span><i class="ti ti-archive me-2"></i>备份列表</span></template>
        <template #actions>
          <TButton variant="outline-secondary" size="sm" @click="load" :disabled="loading">
            <i class="ti ti-refresh"></i>
          </TButton>
        </template>
        <div class="table-responsive">
          <table class="table table-vcenter table-sm card-table m-0">
            <thead><tr><th>文件名</th><th style="width: 120px">大小</th><th style="width: 170px">创建时间</th><th style="width: 70px">类型</th><th style="width: 100px">操作</th></tr></thead>
            <tbody>
              <tr v-if="loading"><td colspan="5" class="text-center text-muted py-3">加载中...</td></tr>
              <tr v-else-if="!backups.length"><td colspan="5" class="text-center text-muted py-3">暂无备份文件</td></tr>
              <tr v-for="b in backups" :key="b.fileName">
                <td class="text-truncate" style="max-width: 360px;" :title="b.fileName"><code class="small">{{ b.fileName }}</code></td>
                <td>{{ formatSize(b.sizeBytes) }}</td>
                <td class="small">{{ formatTime(b.created) }}</td>
                <td><span class="badge" :class="b.isAuto ? 'bg-info-lt' : 'bg-secondary-lt'">{{ b.isAuto ? '自动' : '手动' }}</span></td>
                <td>
                  <a :href="opsApi.downloadBackup(b.fileName)" class="btn btn-sm btn-ghost-secondary" download>
                    <i class="ti ti-download"></i>
                  </a>
                  <button class="btn btn-sm btn-ghost-danger" @click="deleteBackup(b.fileName)">
                    <i class="ti ti-trash"></i>
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </TCard>
    </div>
  </div>
</template>
