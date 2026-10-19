<script setup lang="ts">
/** 高防IP —— 对接 TnorvCdn 主控 /api/admin/protection */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = instances.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})
const instances = ref<any[]>([])
const dialog = ref(false)
const form = ref<any>({})

const loadData = async () => { instances.value = await cdnApi.get('/admin/protection'); return instances.value }

function openEdit(row: any) {
  form.value = row ? JSON.parse(JSON.stringify(row)) : { name: '', ip: '', line: 'BGP', protectedMbps: 100, peakMbps: 1000, protectedTargets: '', status: 'active', expireAt: null }
  dialog.value = true
}

async function save() {
  if (!form.value.name || !form.value.ip) return ElMessage.warning('请填写名称和高防 IP')
  if (form.value.id) await cdnApi.put(`/admin/protection/${form.value.id}`, form.value)
  else await cdnApi.post('/admin/protection', form.value)
  ElMessage.success('已保存')
  dialog.value = false
  gate.value?.refresh?.()
}

async function del(row: any) {
  await cdnApi.delete(`/admin/protection/${row.id}`)
  ElMessage.success('已删除')
  gate.value?.refresh?.()
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">高防IP</h2><div class="text-muted">边缘节点 · 高防实例/线路/防护对象</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
            <el-button type="primary" size="small" @click="openEdit(null)">+ 新增实例</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-table :data="viewRows" stripe>
            <el-table-column prop="name" label="实例名" />
            <el-table-column prop="ip" label="高防 IP" width="140" />
            <el-table-column prop="line" label="线路" width="90" />
            <el-table-column label="保底/峰值 (Mbps)" width="150">
              <template #default="{ row }">{{ row.protectedMbps }} / {{ row.peakMbps }}</template>
            </el-table-column>
            <el-table-column prop="protectedTargets" label="防护对象" show-overflow-tooltip />
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="row.status === 'active' ? 'success' : 'danger'" size="small">{{ row.status === 'active' ? '生效中' : '已到期' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button size="small" @click="openEdit(row)">编辑</el-button>
              <el-button size="small" type="danger" @click="del(row)">删除</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
        </el-card>

        <el-dialog v-model="dialog" :title="form.id ? '编辑高防实例' : '新增高防实例'" width="520px">
          <el-form label-width="120px">
            <el-form-item label="实例名"><el-input v-model="form.name" /></el-form-item>
            <el-form-item label="高防 IP"><el-input v-model="form.ip" /></el-form-item>
            <el-form-item label="线路">
              <el-select v-model="form.line" style="width:200px"><el-option value="BGP" /><el-option value="三线" /><el-option value="海外" /></el-select>
            </el-form-item>
            <el-form-item label="保底带宽 (Mbps)"><el-input-number v-model="form.protectedMbps" :min="10" /></el-form-item>
            <el-form-item label="峰值带宽 (Mbps)"><el-input-number v-model="form.peakMbps" :min="100" /></el-form-item>
            <el-form-item label="防护对象"><el-input v-model="form.protectedTargets" placeholder="域名，逗号分隔" /></el-form-item>
            <el-form-item label="状态">
              <el-select v-model="form.status" style="width:200px"><el-option value="active" label="生效中" /><el-option value="expired" label="已到期" /></el-select>
            </el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" @click="save">保存</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
