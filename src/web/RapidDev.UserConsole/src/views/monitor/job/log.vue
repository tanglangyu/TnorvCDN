<script setup lang="ts">
import { TTable } from '@/components/t'
import type { TableColumn } from '@/components/t/types'
import { ref } from 'vue'
import { jobApi } from '@/api/monitor'

const loading = ref(false)
const data = ref<any[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(10)
const columns: TableColumn[] = [
  { prop: 'jobName', label: '任务名称', minWidth: 140 },
  { prop: 'invokeTarget', label: '调用目标', minWidth: 200 },
  { prop: 'status', label: '状态', width: 80, slot: 'status', align: 'center' },
  { prop: 'costTime', label: '耗时(ms)', width: 110, align: 'right' },
  { prop: 'startTime', label: '开始时间', minWidth: 160 },
  { prop: 'endTime', label: '结束时间', minWidth: 160 },
  { prop: 'errorMsg', label: '错误信息', minWidth: 200 },
]
async function load() { loading.value = true; try { const r: any = await jobApi.logs({ pageIndex: pageIndex.value, pageSize: pageSize.value }); data.value = r.list || []; total.value = r.total || 0 } finally { loading.value = false } }
load()
</script>
<template>
  <div class="card"><div class="card-header"><h3 class="card-title">任务执行日志</h3></div>
    <div class="card-table table-responsive">
      <TTable :columns="columns" :data="data" :loading="loading" :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load">
        <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '成功' : '失败' }}</span></template>
      </TTable>
    </div>
  </div>
</template>
