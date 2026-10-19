<script setup lang="ts">
import { TCrud } from '@/components/t'
import type { TableColumn, FormItemSchema } from '@/components/t/types'
import { rateLimitApi } from '@/api/monitor'
const columns: TableColumn[] = [
  { prop: 'policyName', label: '策略名称', minWidth: 140 },
  { prop: 'routePattern', label: '路由', minWidth: 180 },
  { prop: 'algorithm', label: '算法', width: 120, slot: 'algorithm' },
  { prop: 'partitionBy', label: '分区维度', width: 100 },
  { prop: 'permitLimit', label: '上限', width: 80, align: 'right' },
  { prop: 'periodSeconds', label: '窗口(秒)', width: 100, align: 'right' },
  { prop: 'status', label: '状态', width: 90, slot: 'status', align: 'center' },
]
const searchSchemas: FormItemSchema[] = [{ prop: 'name', label: '策略名称', type: 'input' }]
const formSchemas: FormItemSchema[] = [
  { prop: 'policyName', label: '策略名称', required: true, span: 6 },
  { prop: 'routePattern', label: '路由模式', required: true, span: 6, placeholder: '/api/business/*' },
  { prop: 'httpMethods', label: 'HTTP 方法', defaultValue: '*', span: 6, placeholder: '* 或 GET,POST' },
  { prop: 'partitionBy', label: '分区维度', type: 'select', defaultValue: 'ip', span: 6, options: [{ label: 'IP', value: 'ip' }, { label: '用户', value: 'user' }, { label: '客户端', value: 'client' }] },
  { prop: 'algorithm', label: '算法', type: 'select', defaultValue: 'tokenBucket', span: 6, options: [{ label: '令牌桶', value: 'tokenBucket' }, { label: '滑动窗口', value: 'slidingWindow' }, { label: '固定窗口', value: 'fixedWindow' }, { label: '并发', value: 'concurrency' }] },
  { prop: 'permitLimit', label: '请求上限', type: 'number', defaultValue: 100, span: 6 },
  { prop: 'tokensPerPeriod', label: '每窗口令牌', type: 'number', defaultValue: 100, span: 6 },
  { prop: 'periodSeconds', label: '窗口(秒)', type: 'number', defaultValue: 60, span: 6 },
  { prop: 'queueLimit', label: '队列上限', type: 'number', defaultValue: 0, span: 6 },
  { prop: 'status', label: '状态', type: 'select', defaultValue: 0, span: 6, options: [{ label: '启用', value: 0 }, { label: '停用', value: 1 }] },
  { prop: 'remark', label: '备注', type: 'textarea', span: 12 },
]
const algoMap: Record<string, string> = { tokenBucket: '令牌桶', slidingWindow: '滑动窗口', fixedWindow: '固定窗口', concurrency: '并发' }
</script>
<template>
  <TCrud :columns="columns" :search-schemas="searchSchemas" :form-schemas="formSchemas"
    :fetch="rateLimitApi.list" :create="rateLimitApi.add" :detail="rateLimitApi.get" :update="rateLimitApi.edit" :remove="rateLimitApi.remove" add-title="新增策略">
    <template #body-algorithm="{ row }"><span class="badge bg-info-lt">{{ algoMap[row.algorithm] || row.algorithm }}</span></template>
    <template #body-status="{ row }"><span class="badge" :class="row.status === 0 ? 'bg-success-lt' : 'bg-secondary-lt'">{{ row.status === 0 ? '启用' : '停用' }}</span></template>
  </TCrud>
</template>
