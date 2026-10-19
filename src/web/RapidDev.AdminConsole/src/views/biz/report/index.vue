<script setup lang="ts">
import { ref } from 'vue'
import { reportApi } from '@/api/features'
import { http } from '@/utils/request'
import { TCard, TButton } from '@/components/t'

const sql = ref('SELECT u.UserName, u.NickName, u.Email, d.DeptName\nFROM sys_user u\nLEFT JOIN sys_dept d ON u.DeptId = d.Id\nLIMIT 20')
const results = ref<any[]>([])
const columns = ref<string[]>([])
const loading = ref(false)
const error = ref('')

async function runQuery() {
  loading.value = true; error.value = ''
  try {
    results.value = await reportApi.query(sql.value) || []
    if (results.value.length > 0) columns.value = Object.keys(results.value[0])
    else columns.value = []
  } catch (e: any) { error.value = e?.message || '查询失败' }
  finally { loading.value = false }
}

async function exportCsv() {
  loading.value = true; error.value = ''
  try {
    const resp: any = await http.post('/report/export', { sql: sql.value }, { responseType: 'blob' })
    const blob = new Blob([resp], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url; a.download = `report_${Date.now()}.csv`; a.click()
    URL.revokeObjectURL(url)
  } catch (e: any) { error.value = e?.message || '导出失败' }
  finally { loading.value = false }
}
</script>

<template>
  <TCard title="报表中心">
    <div class="mb-3">
      <label class="form-label">SQL 查询(仅 SELECT)</label>
      <textarea class="form-control" v-model="sql" rows="5" style="font-family: monospace; font-size: 13px;"></textarea>
    </div>
    <div class="d-flex gap-2 mb-3">
      <TButton variant="primary" @click="runQuery" :loading="loading"><i class="ti ti-player-play me-2"></i>执行</TButton>
      <TButton variant="ghost-secondary" @click="exportCsv" :loading="loading"><i class="ti ti-download me-2"></i>导出CSV</TButton>
    </div>

    <div v-if="error" class="alert alert-danger">{{ error }}</div>

    <div v-if="results.length > 0">
      <div class="text-muted mb-2">共 {{ results.length }} 条记录</div>
      <div class="table-responsive">
        <table class="table table-sm table-striped">
          <thead>
            <tr><th v-for="col in columns" :key="col">{{ col }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="(row, i) in results" :key="i">
              <td v-for="col in columns" :key="col" class="small">{{ row[col] }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
    <div v-else-if="!loading && !error" class="text-muted text-center py-4">
      执行 SQL 查询以查看结果
    </div>
  </TCard>
</template>
