<script setup lang="ts">
/** 套餐 —— 当前套餐/用量/到期（用户端特有） */
import { ref, onMounted } from 'vue'
import { cdnApi } from '@/api/cdn'

const props = defineProps<{ serverId: number; server: any }>()
const data = ref<any>(null)
const loading = ref(true)
onMounted(async () => {
  try { data.value = await cdnApi.get('/user/my-plan') } finally { loading.value = false }
})
</script>

<template>
  <div v-loading="loading">
    <h5 class="mb-3">我的套餐</h5>
    <template v-if="data">
      <el-table :data="data.plans || []" size="small" stripe class="mb-3" style="max-width: 720px">
        <el-table-column prop="planName" label="套餐" min-width="160" />
        <el-table-column label="站点数上限" width="110"><template #default="{ row }">{{ row.maxServers || '—' }}</template></el-table-column>
        <el-table-column label="月流量(GB)" width="120"><template #default="{ row }">{{ row.monthlyTrafficGB || '不限' }}</template></el-table-column>
        <el-table-column label="到期时间" width="120"><template #default="{ row }">{{ new Date(row.expiresAt).toLocaleDateString() }}</template></el-table-column>
      </el-table>
      <el-descriptions :column="2" border style="max-width: 720px">
        <el-descriptions-item label="已用站点数">{{ data.usage?.servers ?? 0 }}</el-descriptions-item>
        <el-descriptions-item label="本月流量（GB）">{{ data.usage?.monthTrafficGB ?? 0 }}</el-descriptions-item>
      </el-descriptions>
    </template>
    <el-empty v-else-if="!loading" description="暂无生效套餐，前往「套餐管理」购买" />
  </div>
</template>
