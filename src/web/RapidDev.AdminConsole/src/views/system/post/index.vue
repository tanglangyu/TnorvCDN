<script setup lang="ts">
import { TCrud } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { postApi } from '@/api/system'

const columns: TableColumn[] = [
  { prop: 'postCode', label: '岗位编码', minWidth: 120 },
  { prop: 'postName', label: '岗位名称', minWidth: 120 },
  { prop: 'sort', label: '排序', width: 80, align: 'center' },
  { prop: 'status', label: '状态', width: 90, slot: 'status', align: 'center' },
]
const searchSchemas: FormItemSchema[] = [{ prop: 'name', label: '岗位名称', type: 'input' }]
const formSchemas: FormItemSchema[] = [
  { prop: 'postCode', label: '岗位编码', required: true, span: 6 },
  { prop: 'postName', label: '岗位名称', required: true, span: 6 },
  { prop: 'sort', label: '排序', type: 'number', defaultValue: 0, span: 6 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }] },
]
</script>
<template>
  <TCrud :columns="columns" :search-schemas="searchSchemas" :form-schemas="formSchemas"
    :fetch="postApi.list" :create="postApi.add" :update="postApi.edit" :remove="postApi.remove">
    <template #body-status="{ row }">
      <span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-danger-lt'">{{ row.status === 0 ? '启用' : '停用' }}</span>
    </template>
  </TCrud>
</template>
