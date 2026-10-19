<script setup lang="ts">
import { TCrud } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { tenantApi } from '@/api/system'

const columns: TableColumn[] = [
  { prop: 'name', label: '租户名称', minWidth: 150 },
  { prop: 'code', label: '租户编码', minWidth: 120 },
  { prop: 'contact', label: '联系人', width: 120 },
  { prop: 'phone', label: '联系电话', width: 140 },
  { prop: 'accountLimit', label: '账号上限', width: 100, align: 'center' },
  { prop: 'expireAt', label: '到期时间', minWidth: 160 },
  { prop: 'status', label: '状态', width: 90, slot: 'status', align: 'center' },
]
const searchSchemas: FormItemSchema[] = [{ prop: 'name', label: '租户名称', type: 'input' }]
const formSchemas: FormItemSchema[] = [
  { prop: 'name', label: '租户名称', required: true, span: 6 },
  { prop: 'code', label: '租户编码', required: true, span: 6 },
  { prop: 'contact', label: '联系人', span: 6 },
  { prop: 'phone', label: '联系电话', span: 6 },
  { prop: 'accountLimit', label: '账号上限', type: 'number', defaultValue: 100, span: 6 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }, { label: '已过期', value: 2 }] },
  { prop: 'expireAt', label: '到期时间', type: 'datetime', span: 12 },
]
</script>
<template>
  <TCrud :columns="columns" :search-schemas="searchSchemas" :form-schemas="formSchemas"
    :fetch="tenantApi.list" :create="tenantApi.add" :detail="tenantApi.get" :update="tenantApi.edit" :remove="tenantApi.remove" add-title="新增租户">
    <template #body-status="{ row }">
      <span v-if="row.status === 0" class="badge bg-success-lt">启用</span>
      <span v-else-if="row.status === 1" class="badge bg-danger-lt">停用</span>
      <span v-else class="badge bg-warning-lt">已过期</span>
    </template>
  </TCrud>
</template>
