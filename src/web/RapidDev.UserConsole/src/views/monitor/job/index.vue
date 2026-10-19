<script setup lang="ts">
import { ref } from 'vue'
import { TCrud } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { jobApi } from '@/api/monitor'
import { useRouter } from 'vue-router'

const router = useRouter()
const crudRef = ref<any>(null)

const columns: TableColumn[] = [
  { prop: 'jobName', label: '任务名称', minWidth: 140 },
  { prop: 'jobGroup', label: '任务组', width: 100 },
  { prop: 'invokeTarget', label: '调用目标', minWidth: 200 },
  { prop: 'cronExpression', label: 'Cron 表达式', width: 150 },
  { prop: 'status', label: '状态', width: 90, slot: 'status', align: 'center' },
]
const searchSchemas: FormItemSchema[] = [{ prop: 'name', label: '任务名称', type: 'input' }]
const formSchemas: FormItemSchema[] = [
  { prop: 'jobName', label: '任务名称', required: true, span: 6 },
  { prop: 'jobGroup', label: '任务组', defaultValue: 'DEFAULT', span: 6 },
  { prop: 'cronExpression', label: 'Cron 表达式', required: true, span: 12 },
  { prop: 'invokeTarget', label: '调用目标', type: 'textarea', required: true, span: 12 },
  { prop: 'methodParams', label: '方法参数', type: 'textarea', span: 12 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 1, span: 6, options: [{ label: '运行', value: 0 }, { label: '暂停', value: 1 }] },
  { prop: 'concurrent', label: '并发执行', type: 'select', defaultValue: 0, span: 6, options: [{ label: '允许', value: 0 }, { label: '禁止', value: 1 }] },
]

async function handleResume(id: number) {
  await jobApi.resume(id)
  crudRef.value?.refresh()
}
async function handlePause(id: number) {
  await jobApi.pause(id)
  crudRef.value?.refresh()
}
async function handleRun(id: number) {
  await jobApi.run(id)
  crudRef.value?.refresh()
}
</script>

<template>
  <TCrud ref="crudRef" :columns="columns" :search-schemas="searchSchemas" :form-schemas="formSchemas"
    :fetch="jobApi.list" :create="jobApi.add" :detail="jobApi.get" :update="jobApi.edit" :remove="jobApi.remove" add-title="新增任务">
    <template #toolbar>
      <button class="btn btn-ghost-secondary btn-sm" @click="router.push('/monitor/job/log')"><i class="ti ti-history me-1"></i>执行日志</button>
    </template>
    <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-warning-lt'">{{ row.status === 0 ? '运行' : '暂停' }}</span></template>
    <template #action="{ row }">
      <button v-if="row.status === 1" class="btn btn-ghost-success btn-sm" title="恢复" @click="handleResume(row.id)"><i class="ti ti-player-play"></i></button>
      <button v-else class="btn btn-ghost-warning btn-sm" title="暂停" @click="handlePause(row.id)"><i class="ti ti-player-pause"></i></button>
      <button class="btn btn-ghost-primary btn-sm" title="执行一次" @click="handleRun(row.id)"><i class="ti ti-bolt"></i></button>
    </template>
  </TCrud>
</template>
