<script setup lang="ts">
/** 备案管理 —— 我的站点备案状态查看 + 提交/重新提交备案（管理员人工审核） */
import { ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { cdnApi } from '@/api/cdn'
import CdnGate from './gate.vue'

const gate = ref()
const rows = ref<any[]>([])

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = rows.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})

const dialog = ref(false)
const form = ref<any>({})

const regionLabel = (r: string) =>
  ({ global: '全球', cn: '中国大陆', overseas: '非中国大陆' } as any)[r] || r
const statusLabel = (st: string) =>
  ({ none: '未备案', pending: '审核中', approved: '已备案', rejected: '已驳回' } as any)[st] || st
const statusTag = (st: string) =>
  ({ none: 'info', pending: 'warning', approved: 'success', rejected: 'danger' } as any)[st] || 'info'

const loadData = async () => {
  const list = await cdnApi.get('/user/servers/icp')
  rows.value = Array.isArray(list) ? list : (list?.data || [])
  return rows.value
}

function openSubmit(row: any) {
  form.value = { serverId: row.id, domains: (row.domains || []).join(', '), icpNumber: row.icpNumber || '', note: '' }
  dialog.value = true
}

async function submit() {
  try {
    await cdnApi.post('/user/servers/icp/submit', {
      serverId: form.value.serverId,
      icpNumber: form.value.icpNumber,
      note: form.value.note,
    })
    ElMessage.success('已提交备案审核，请等待管理员审核')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || e?.message || '提交失败')
  }
}
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">备案管理</h2><div class="text-muted">CDN加速 · 域名 ICP 备案提交与状态</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default>
        <el-card>
          <el-alert type="info" :closable="false" class="mb-3"
            title="使用中国大陆节点加速的域名须完成 ICP 备案。提交后由管理员人工审核，审核通过前中国大陆节点不会为其提供加速；全球/海外区域不受影响。" />
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column label="域名" min-width="200">
              <template #default="{ row }">{{ (row.domains || []).join(', ') }}</template>
            </el-table-column>
            <el-table-column label="加速区域" width="110">
              <template #default="{ row }">
                <el-tag size="small" :type="row.region === 'cn' ? 'warning' : 'info'">{{ regionLabel(row.region) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="备案号" min-width="170">
              <template #default="{ row }">{{ row.icpNumber || '—' }}</template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag size="small" :type="statusTag(row.icpStatus)">{{ statusLabel(row.icpStatus) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="备注" min-width="160">
              <template #default="{ row }">{{ row.icpNote || '—' }}</template>
            </el-table-column>
            <el-table-column label="操作" fixed="right">
              <template #default="{ row }">
                <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
              <el-button v-if="row.icpStatus !== 'approved' && row.icpStatus !== 'pending'"
              size="small" type="primary" plain @click="openSubmit(row)">
              {{ row.icpStatus === 'rejected' ? '重新提交' : '提交备案' }}
              </el-button>
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
      </template>
    </CdnGate>
    <el-dialog v-model="dialog" title="提交备案" width="480px">
      <el-form label-width="90px">
        <el-form-item label="域名">
          <div class="text-muted" style="font-size:13px">{{ form.domains }}</div>
        </el-form-item>
        <el-form-item label="备案号" required>
          <el-input v-model="form.icpNumber" placeholder="如 粤ICP备XXXXXXXX号" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.note" type="textarea" :rows="2" placeholder="选填，主办单位名称等补充信息" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" @click="submit">提交审核</el-button>
      </template>
    </el-dialog>
  </div>
</template>
