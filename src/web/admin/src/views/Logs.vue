<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>访问日志</span>
        <el-form inline>
          <el-form-item><el-select v-model="q.status" placeholder="状态码" clearable style="width:110px"><el-option v-for="s in [200,301,403,404,429,500,502]" :key="s" :value="s" :label="s" /></el-select></el-form-item>
          <el-form-item><el-input v-model="q.remoteAddr" placeholder="客户端 IP" clearable style="width:150px" /></el-form-item>
          <el-form-item><el-select v-model="q.cacheStatus" placeholder="缓存" clearable style="width:100px"><el-option value="HIT" /><el-option value="MISS" /></el-select></el-form-item>
          <el-form-item><el-button type="primary" @click="load(1)">查询</el-button></el-form-item>
        </el-form>
      </div>
    </template>
    <el-table :data="items" v-loading="loading" size="small">
      <el-table-column label="时间" width="170"><template #default="{ row }">{{ fmt(row.timestamp) }}</template></el-table-column>
      <el-table-column prop="remoteAddr" label="客户端 IP" width="140" />
      <el-table-column prop="method" label="方法" width="70" />
      <el-table-column prop="host" label="域名" width="160" />
      <el-table-column prop="uri" label="URI" min-width="220" show-overflow-tooltip />
      <el-table-column label="状态" width="70">
        <template #default="{ row }"><el-tag :type="statusType(row.status)" size="small">{{ row.status }}</el-tag></template>
      </el-table-column>
      <el-table-column prop="cacheStatus" label="缓存" width="80" />
      <el-table-column prop="requestTimeMs" label="耗时(ms)" width="90"><template #default="{ row }">{{ row.requestTimeMs?.toFixed(1) }}</template></el-table-column>
      <el-table-column label="WAF" width="90">
        <template #default="{ row }"><el-tag v-if="row.wafAction" type="danger" size="small">{{ row.wafAction }}: {{ row.blockReason }}</el-tag></template>
      </el-table-column>
    </el-table>
    <el-pagination style="margin-top:12px" layout="total, prev, pager, next" :total="total" :page-size="q.size"
      :current-page="q.page" @current-change="load" />
  </el-card>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import api from '../api'

const items = ref([])
const total = ref(0)
const loading = ref(false)
const q = ref({ page: 1, size: 50, status: null, remoteAddr: '', cacheStatus: null })

const fmt = (t) => t ? new Date(t).toLocaleString() : '-'
const statusType = (s) => s < 300 ? 'success' : s < 400 ? 'info' : s < 500 ? 'warning' : 'danger'

async function load(page) {
  if (page) q.value.page = page
  loading.value = true
  try {
    const res = await api.get('/admin/logs', { params: { ...q.value, remoteAddr: q.value.remoteAddr || undefined } })
    items.value = res.items
    total.value = res.total
  } finally { loading.value = false }
}
onMounted(() => load())
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
</style>
