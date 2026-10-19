<script setup lang="ts">
import { TCrud } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { configApi } from '@/api/system'

const columns: TableColumn[] = [
  { prop: 'configName', label: '参数名称', minWidth: 150 },
  { prop: 'configKey', label: '参数键名', minWidth: 180 },
  { prop: 'configValue', label: '参数键值', minWidth: 150 },
  { prop: 'configType', label: '系统内置', width: 100, slot: 'configType', align: 'center' },
]
const searchSchemas: FormItemSchema[] = [{ prop: 'key', label: '键名', type: 'input' }]
const formSchemas: FormItemSchema[] = [
  { prop: 'configName', label: '参数名称', required: true, span: 12 },
  { prop: 'configKey', label: '参数键名', required: true, span: 12 },
  { prop: 'configValue', label: '参数键值', type: 'textarea', span: 12 },
  { prop: 'configType', label: '系统内置', type: 'select', defaultValue: 0, span: 6, options: [{ label: '是', value: 1 }, { label: '否', value: 0 }] },
]
</script>
<template>
  <TCrud :columns="columns" :search-schemas="searchSchemas" :form-schemas="formSchemas"
    :fetch="configApi.list" :create="configApi.add" :update="configApi.edit" :remove="configApi.remove">
    <template #body-configType="{ row }">
      <span class="badge" :class="row.configType === 1 ? 'bg-warning-lt' : 'bg-secondary-lt'">{{ row.configType === 1 ? '是' : '否' }}</span>
    </template>
  </TCrud>
</template>
