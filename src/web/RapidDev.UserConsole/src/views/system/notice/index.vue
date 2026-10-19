<script setup lang="ts">
import { TCrud } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { noticeApi } from '@/api/system'

const columns: TableColumn[] = [
  { prop: 'noticeTitle', label: '标题', minWidth: 200 },
  { prop: 'noticeType', label: '类型', width: 100, slot: 'noticeType', align: 'center' },
  { prop: 'status', label: '状态', width: 90, slot: 'status', align: 'center' },
  { prop: 'createdAt', label: '创建时间', minWidth: 160 },
]
const searchSchemas: FormItemSchema[] = [{ prop: 'title', label: '标题', type: 'input' }]
const formSchemas: FormItemSchema[] = [
  { prop: 'noticeTitle', label: '标题', required: true, span: 12 },
  { prop: 'noticeType', label: '类型', type: 'select', defaultValue: 1, span: 6, options: [{ label: '通知', value: 1 }, { label: '公告', value: 2 }] },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 1, span: 6, options: [{ label: '显示', value: 1 }, { label: '隐藏', value: 0 }] },
  { prop: 'noticeContent', label: '内容', type: 'textarea', span: 12 },
]
</script>
<template>
  <TCrud :columns="columns" :search-schemas="searchSchemas" :form-schemas="formSchemas"
    :fetch="noticeApi.list" :create="noticeApi.add" :detail="noticeApi.get" :update="noticeApi.edit" :remove="noticeApi.remove">
    <template #body-noticeType="{ row }">
      <span class="badge" :class="row.noticeType === 1 ? 'bg-info-lt' : 'bg-success-lt'">{{ row.noticeType === 1 ? '通知' : '公告' }}</span>
    </template>
    <template #body-status="{ row }">
      <span class="badge" :class="row.status === 1 ? 'bg-success-lt' : 'bg-secondary-lt'">{{ row.status === 1 ? '显示' : '隐藏' }}</span>
    </template>
  </TCrud>
</template>
