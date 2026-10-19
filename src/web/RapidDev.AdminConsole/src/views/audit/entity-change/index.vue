<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { auditApi } from '@/api/features'
import { http } from '@/utils/request'
import { TCard, TButton, TInput, TModal, TPagination } from '@/components/t'

const loading = ref(false)
const list = ref<any[]>([])
const total = ref(0)
const pageIndex = ref(1)
const pageSize = ref(20)
const filterEntityType = ref('')
const filterRecordId = ref('')
const detailModal = ref(false)
const detailData = ref<any>(null)

async function load() {
  loading.value = true
  try {
    const params: any = { pageIndex: pageIndex.value, pageSize: pageSize.value }
    if (filterEntityType.value) params.entityType = filterEntityType.value
    if (filterRecordId.value) params.recordId = filterRecordId.value
    const r: any = await auditApi.entityChanges(params)
    list.value = r.list || []
    total.value = r.total || 0
  } finally { loading.value = false }
}

async function viewHistory(row: any) {
  try {
    detailData.value = await auditApi.entityHistory(row.entityType, row.recordId)
    detailModal.value = true
  } catch {}
}

function parseDetail(d: string) {
  try { return JSON.parse(d) } catch { return [] }
}

onMounted(load)
</script>

<template>
  <TCard title="实体变更追踪">
    <template #actions>
      <div class="d-flex gap-2 align-items-center">
        <TInput v-model="filterEntityType" placeholder="实体类型" style="width: 160px" />
        <TInput v-model="filterRecordId" placeholder="记录ID" style="width: 120px" />
        <TButton variant="ghost-secondary" @click="load"><i class="ti ti-search"></i></TButton>
      </div>
    </template>

    <table class="table table-vcenter">
      <thead>
        <tr><th>实体类型</th><th>记录ID</th><th>操作</th><th>变更详情</th><th>操作人</th><th>时间</th><th></th></tr>
      </thead>
      <tbody>
        <tr v-for="row in list" :key="row.id">
          <td><span class="badge bg-primary-lt">{{ row.entityType }}</span></td>
          <td>{{ row.recordId }}</td>
          <td><span class="badge" :class="row.operation === 'Update' ? 'bg-warning-lt' : 'bg-success-lt'">{{ row.operation }}</span></td>
          <td class="text-truncate" style="max-width: 300px;">
            <code class="small">{{ row.changeDetail?.substring(0, 120) }}</code>
          </td>
          <td>{{ row.operUserName || '-' }}</td>
          <td class="small">{{ new Date(row.operTime).toLocaleString() }}</td>
          <td><TButton variant="outline-secondary" size="sm" @click="viewHistory(row)"><i class="ti ti-history"></i></TButton></td>
        </tr>
      </tbody>
    </table>
    <TPagination :total="total" v-model:pageIndex="pageIndex" v-model:pageSize="pageSize" @change="load" />
  </TCard>

  <!-- 变更历史弹窗 -->
  <TModal v-model="detailModal" title="完整变更历史" size="lg">
    <table class="table table-sm" v-if="detailData?.length">
      <thead><tr><th>时间</th><th>操作</th><th>变更</th><th>操作人</th></tr></thead>
      <tbody>
        <tr v-for="item in detailData" :key="item.id">
          <td class="small">{{ new Date(item.operTime).toLocaleString() }}</td>
          <td><span class="badge bg-warning-lt">{{ item.operation }}</span></td>
          <td>
            <div v-for="d in parseDetail(item.changeDetail)" :key="d.field" class="mb-1">
              <code>{{ d.field }}</code>:
              <span class="text-danger">{{ d.old }}</span> →
              <span class="text-success">{{ d.new }}</span>
            </div>
          </td>
          <td>{{ item.operUserName }}</td>
        </tr>
      </tbody>
    </table>
    <div v-else class="text-muted text-center py-3">无变更记录</div>
  </TModal>
</template>
