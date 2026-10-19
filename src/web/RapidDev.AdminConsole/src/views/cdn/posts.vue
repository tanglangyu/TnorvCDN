<script setup lang="ts">
/**
 * 公告管理 —— 通用设计
 * 控制台公告栏/登录弹窗公告；受众可指定用户端/管理端/全部。
 */
import { ref, onMounted } from 'vue'
import { http } from '@/utils/request'
import { ElMessage, ElMessageBox } from 'element-plus'

const items = ref<any[]>([])
const loading = ref(false)
const filterQ = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const load = async () => {
  loading.value = true
  try {
    const data = await http.get('/admin/posts', {
      params: {
        page: pager.value.page,
        pageSize: pager.value.pageSize,
        q: filterQ.value || undefined,
      },
    })
    items.value = Array.isArray(data) ? data : (data?.items || [])
    pager.value.total = Array.isArray(data) ? items.value.length : (data?.total ?? items.value.length)
  } finally {
    loading.value = false
  }
}

const onSearch = () => {
  pager.value.page = 1
  load()
}
onMounted(load)

const visible = ref(false)
const saving = ref(false)
const form = ref<any>(null)
const openNew = () => {
  form.value = { title: '', content: '', enabled: true, popup: false, audience: 'user' }
  visible.value = true
}
const openEdit = (row: any) => {
  form.value = { ...row }
  visible.value = true
}
const save = async () => {
  if (!form.value.title) { ElMessage.warning('请填写标题'); return }
  saving.value = true
  try {
    if (form.value.id) await http.put(`/admin/posts/${form.value.id}`, form.value)
    else await http.post('/admin/posts', form.value)
    ElMessage.success('公告已保存')
    visible.value = false
    await load()
  } finally {
    saving.value = false
  }
}
const removeOne = async (row: any) => {
  await ElMessageBox.confirm(`确定删除公告「${row.title}」？`, '提示', { type: 'warning' })
  await http.delete(`/admin/posts/${row.id}`)
  ElMessage.success('已删除')
  await load()
}
const fmtTime = (t: string) => (t ? new Date(t).toLocaleString() : '—')
const audienceLabel: Record<string, string> = { user: '用户端', admin: '管理端', all: '全部' }
</script>

<template>
  <div>
    <div class="page-header mb-3">
      <div class="row align-items-center">
        <div class="col-auto">
          <h2 class="page-title">公告管理</h2>
          <div class="text-muted">控制台公告栏与登录弹窗公告（用户端/管理端）</div>
        </div>
        <div class="col-auto ms-auto">
          <el-input v-model="filterQ" placeholder="公告标题关键词" size="small" clearable style="width: 220px"
            @keyup.enter="onSearch" @clear="onSearch">
            <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
          </el-input>
          <el-button type="primary" size="small" @click="openNew">发布公告</el-button>
        </div>
      </div>
    </div>

    <el-card v-loading="loading">
      <el-table :data="items" stripe>
        <el-table-column prop="title" label="标题" min-width="200" show-overflow-tooltip />
        <el-table-column prop="content" label="内容" min-width="320" show-overflow-tooltip />
        <el-table-column label="受众" width="90">
          <template #default="{ row }">
            <el-tag size="small" :type="row.audience === 'admin' ? 'warning' : 'primary'">{{ audienceLabel[row.audience] || row.audience }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="弹窗" width="70">
          <template #default="{ row }"><el-tag v-if="row.popup" size="small" type="danger" effect="plain">弹窗</el-tag><span v-else class="text-muted small">—</span></template>
        </el-table-column>
        <el-table-column label="状态" width="80">
          <template #default="{ row }">
            <el-tag v-if="row.enabled" type="success" size="small">上架中</el-tag>
            <el-tag v-else type="info" size="small">已下架</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="发布时间" width="170">
          <template #default="{ row }">{{ fmtTime(row.createdAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" fixed="right">
          <template #default="{ row }">
            <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
          <el-button link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" size="small" @click="removeOne(row)">删除</el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
        layout="total, sizes, prev, pager, next" :total="pager.total"
        :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
        @current-change="(p: number) => { pager.page = p; load() }"
        @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; load() }" />
      <el-empty v-if="!loading && items.length === 0" description="暂无公告" />
    </el-card>

    <el-dialog align-center v-model="visible" :title="form?.id ? '编辑公告' : '发布公告'" width="560px">
      <el-form label-width="90px" v-if="form">
        <el-form-item label="标题">
          <el-input v-model="form.title" maxlength="100" placeholder="公告标题" />
        </el-form-item>
        <el-form-item label="内容">
          <el-input v-model="form.content" type="textarea" :rows="5" placeholder="公告内容（支持换行）" />
        </el-form-item>
        <el-form-item label="受众">
          <el-radio-group v-model="form.audience">
            <el-radio value="user">用户端</el-radio>
            <el-radio value="admin">管理端</el-radio>
            <el-radio value="all">全部</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="展示方式">
          <el-checkbox v-model="form.enabled">上架（展示在公告栏）</el-checkbox>
          <el-checkbox v-model="form.popup">登录后弹窗</el-checkbox>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="visible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="save">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
