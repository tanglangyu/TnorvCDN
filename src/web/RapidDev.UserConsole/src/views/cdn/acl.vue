<script setup lang="ts">
/**
 * 访问控制 / API 密钥 —— 通用设计
 * 用户自助创建/启停/删除 API 密钥（OpenAPI Bearer 认证）。Secret 仅创建时展示一次。
 */
import { ref, computed, onMounted } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'

const keys = ref<any[]>([])

// 分页 + 高级查询（客户端过滤，兼容各接口返回结构）
const pager = ref({ page: 1, pageSize: 20, total: 0 })
const filterQ = ref('')
const viewRows = computed(() => {
  const kw = (filterQ.value || '').trim().toLowerCase()
  let list = keys.value
  if (kw) list = list.filter((r: any) => JSON.stringify(r).toLowerCase().includes(kw))
  pager.value.total = list.length
  const start = (pager.value.page - 1) * pager.value.pageSize
  return list.slice(start, start + pager.value.pageSize)
})

const loading = ref(false)
const load = async () => {
  loading.value = true
  try {
    keys.value = await cdnApi.get('/user/accesskeys')
  } finally {
    loading.value = false
  }
}
onMounted(load)

const visible = ref(false)
const newName = ref('')
const creating = ref(false)
const created = ref<any>(null) // { keyId, secret } 仅一次展示

const openCreate = () => {
  newName.value = ''
  created.value = null
  visible.value = true
}
const doCreate = async () => {
  creating.value = true
  try {
    created.value = await cdnApi.post('/user/accesskeys', { name: newName.value || '默认密钥' })
    ElMessage.success('密钥已创建')
    await load()
  } finally {
    creating.value = false
  }
}
const toggle = async (row: any) => {
  await cdnApi.put(`/user/accesskeys/${row.id}/toggle`)
  ElMessage.success(row.enabled ? '已停用' : '已启用')
  await load()
}
const removeOne = async (row: any) => {
  await ElMessageBox.confirm(`确定删除密钥「${row.name}」？使用该密钥的调用将立即失效。`, '提示', { type: 'warning' })
  await cdnApi.delete(`/user/accesskeys/${row.id}`)
  ElMessage.success('已删除')
  await load()
}
const fmtTime = (t: string) => (t ? new Date(t).toLocaleString() : '—')
const copyText = async (text: string, label: string) => {
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success(label + '已复制')
  } catch {
    ElMessage.warning('复制失败，请手动选择复制')
  }
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">API 密钥</h2>
          <div class="text-muted">OpenAPI 调用凭证（Authorization: Bearer ak_xxx.sk_xxx）；密钥 Secret 仅创建时可见，请妥善保管</div>
        </div>
        <div class="col-auto ms-auto">
          <el-button type="primary" size="small" @click="openCreate">+ 创建密钥</el-button>
        </div>
      </div>
    </div>

    <el-card v-loading="loading">
      <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows" stripe>
        <el-table-column prop="name" label="名称" min-width="160" />
        <el-table-column prop="keyId" label="Key ID" min-width="200">
          <template #default="{ row }">
            <span class="font-monospace small">{{ row.keyId }}</span>
            <el-button link size="small" type="primary" @click="copyText(row.keyId, 'Key ID')">复制</el-button>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最近使用" width="170">
          <template #default="{ row }">{{ row.lastUsedAt ? fmtTime(row.lastUsedAt) : '从未使用' }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" fixed="right">
          <template #default="{ row }">
            <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
          <el-button link size="small" type="primary" @click="toggle(row)">{{ row.enabled ? '停用' : '启用' }}</el-button>
          <el-button link size="small" type="danger" @click="removeOne(row)">删除</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
      <el-empty v-if="!loading && !keys.length" description="暂无密钥，创建后即可通过 OpenAPI 管理您的站点" />
    </el-card>

    <el-dialog align-center v-model="visible" title="创建 API 密钥" width="520px">
      <template v-if="!created">
        <el-form label-width="80px">
          <el-form-item label="名称">
            <el-input v-model="newName" placeholder="如：CI 部署 / 监控脚本" maxlength="50" />
          </el-form-item>
        </el-form>
        <div class="text-end mt-3">
          <el-button @click="visible = false">取消</el-button>
          <el-button type="primary" :loading="creating" @click="doCreate">创建</el-button>
        </div>
      </template>
      <template v-else>
        <el-alert type="warning" :closable="false" class="mb-3"
          title="请立即保存 Secret——关闭弹窗后将不再展示（服务端仅存哈希，丢失只能重新创建）" />
        <el-form label-width="90px">
          <el-form-item label="Key ID">
            <el-input :model-value="created.keyId" readonly>
              <template #append><el-button @click="copyText(created.keyId, 'Key ID')">复制</el-button></template>
            </el-input>
          </el-form-item>
          <el-form-item label="Secret">
            <el-input :model-value="created.secret" readonly class="font-monospace">
              <template #append><el-button @click="copyText(created.secret, 'Secret')">复制</el-button></template>
            </el-input>
          </el-form-item>
        </el-form>
        <div class="text-end mt-3">
          <el-button type="primary" @click="visible = false">我已保存</el-button>
        </div>
      </template>
    </el-dialog>
  </div>
</template>
