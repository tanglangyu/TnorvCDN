<script setup lang="ts">
import { ref } from 'vue'
import { TCard, TButton } from '@/components/t'
import { codegenApi } from '@/api/codegen'
import { useRoute } from 'vue-router'

const route = useRoute()
const id = Number(route.params.id)
const table = ref<any>(null)
async function load() { table.value = await codegenApi.get(id) }
load()
</script>
<template>
  <TCard :title="`编辑生成配置 - ${table?.tableName || ''}`">
    <div class="text-muted">在此配置表生成选项:模块、业务名、字段属性、生成层级等。完整实现待 M9。</div>
  </TCard>
</template>
