<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { codegenApi } from '@/api/codegen'
import { TCard, TButton, TModal, TForm, TTable, TTag } from '@/components/t'
import type { FormItemSchema, TableColumn } from '@/components/t/types'

const dbTables = ref<any[]>([])
const genTables = ref<any[]>([])
const tabLoading = ref(false)
const importVisible = ref(false)
const importModel = ref<any>({ moduleName: 'Business', businessName: '' })
const previewVisible = ref(false)
const previewFiles = ref<Record<string, string>>({})
const previewFileId = ref(0)
const activeTab = ref('')

const importSchemas: FormItemSchema[] = [
  { prop: 'tableName', label: '表名', required: true, span: 12 },
  { prop: 'moduleName', label: '模块名', required: true, span: 6, defaultValue: 'Business' },
  { prop: 'businessName', label: '业务名', required: true, span: 6 },
]

const genColumns: TableColumn[] = [
  { prop: 'tableName', label: '表名', minWidth: 180 },
  { prop: 'tableComment', label: '表注释', minWidth: 180 },
  { prop: 'className', label: '实体类名', minWidth: 150 },
  { prop: 'moduleName', label: '模块', width: 100 },
]

async function loadDbTables() { try { dbTables.value = (await codegenApi.dbTables()) as any } catch {} }
async function loadGenTables() { tabLoading.value = true; try { const res = (await codegenApi.list({ pageIndex: 1, pageSize: 100 })) as any; genTables.value = res?.list || res?.data || (Array.isArray(res) ? res : []) } finally { tabLoading.value = false } }
function openImport() { importModel.value = { moduleName: 'Business', businessName: '' }; importVisible.value = true }
async function doImport() { await codegenApi.import(importModel.value); importVisible.value = false; loadGenTables() }
async function preview(id: number) { previewFileId.value = id; previewFiles.value = (await codegenApi.preview(id)) as any; activeTab.value = Object.keys(previewFiles.value)[0] || ''; previewVisible.value = true }
function downloadCode(id: number) { window.open(`/api/tool/gen/download/${id}`, '_blank') }
onMounted(() => { loadDbTables(); loadGenTables() })
</script>

<template>
  <div class="row">
    <div class="col-lg-5">
      <TCard title="数据库表">
        <template #actions><TButton size="sm" variant="ghost-secondary" @click="loadDbTables"><i class="ti ti-refresh"></i></TButton></template>
        <div class="table-responsive" style="max-height: 600px; overflow-y: auto">
          <table class="table table-vcenter card-table table-sm table-hover">
            <thead><tr><th>表名</th><th>注释</th><th>导入</th></tr></thead>
            <tbody>
              <tr v-for="t in dbTables" :key="t.tableName">
                <td><TTag variant="primary">{{ t.tableName }}</TTag></td>
                <td class="text-muted">{{ t.tableComment }}</td>
                <td><button class="btn btn-ghost-primary btn-sm" @click="importModel.tableName = t.tableName; openImport()"><i class="ti ti-download"></i></button></td>
              </tr>
            </tbody>
          </table>
        </div>
      </TCard>
    </div>
    <div class="col-lg-7">
      <TCard title="已导入表">
        <template #actions><TButton size="sm" variant="ghost-secondary" @click="loadGenTables"><i class="ti ti-refresh"></i></TButton></template>
        <TTable :columns="genColumns" :data="genTables" :loading="tabLoading">
          <template #action="{ row }">
            <button class="btn btn-ghost-primary btn-sm" @click="preview(row.id)"><i class="ti ti-eye"></i>预览</button>
          </template>
        </TTable>
      </TCard>
    </div>
  </div>

  <TModal v-model="importVisible" title="导入表">
    <TForm v-model="importModel" :schemas="importSchemas" />
    <template #footer><TButton variant="ghost-secondary" @click="importVisible = false">取消</TButton><TButton variant="primary" @click="doImport">导入</TButton></template>
  </TModal>

  <TModal v-model="previewVisible" title="代码预览" size="xl">
    <div class="mb-2 d-flex justify-content-between">
      <ul class="nav nav-tabs">
        <li class="nav-item" v-for="(_, name) in previewFiles" :key="name">
          <a class="nav-link" :class="{ active: activeTab === name }" href="javascript:;" @click="activeTab = name">{{ name }}</a>
        </li>
      </ul>
      <TButton variant="success" size="sm" @click="downloadCode(previewFileId)"><i class="ti ti-download me-1"></i>下载 ZIP</TButton>
    </div>
    <pre class="bg-dark text-light p-3 rounded" style="max-height: 450px; overflow: auto"><code>{{ activeTab ? previewFiles[activeTab] : '' }}</code></pre>
  </TModal>
</template>
