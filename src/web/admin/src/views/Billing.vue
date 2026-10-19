<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>套餐与计费</span>
        <el-button type="primary" @click="openPlan(null)"><el-icon><Plus /></el-icon>创建套餐</el-button>
      </div>
    </template>
    <el-tabs>
      <el-tab-pane label="套餐">
        <el-table :data="plans" v-loading="loading" size="small">
          <el-table-column prop="id" label="ID" width="60" />
          <el-table-column prop="name" label="名称" />
          <el-table-column label="网站数" width="90"><template #default="{ row }">{{ row.maxServers }}</template></el-table-column>
          <el-table-column label="域名数" width="90"><template #default="{ row }">{{ row.maxDomains }}</template></el-table-column>
          <el-table-column label="月流量" width="110"><template #default="{ row }">{{ row.monthlyTrafficMB ? row.monthlyTrafficMB + ' MB' : '不限' }}</template></el-table-column>
          <el-table-column label="月价" width="110"><template #default="{ row }">¥{{ (row.priceCents / 100).toFixed(2) }}</template></el-table-column>
          <el-table-column label="状态" width="90"><template #default="{ row }"><el-tag :type="row.enabled ? 'success' : 'danger'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag></template></el-table-column>
          <el-table-column label="操作" width="180">
            <template #default="{ row }">
              <el-button size="small" @click="openPlan(row)">编辑</el-button>
              <el-popconfirm title="确认删除？" @confirm="delPlan(row)">
                <template #reference><el-button size="small" type="danger">删除</el-button></template>
              </el-popconfirm>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
      <el-tab-pane label="用户订阅">
        <div style="margin-bottom:12px">
          <el-select v-model="newSub.userId" placeholder="用户" style="width:180px">
            <el-option v-for="u in users" :key="u.id" :value="u.id" :label="u.username" />
          </el-select>
          <el-select v-model="newSub.planId" placeholder="套餐" style="width:180px">
            <el-option v-for="p in plans" :key="p.id" :value="p.id" :label="p.name" />
          </el-select>
          <el-button type="primary" @click="addSub">添加订阅（30天）</el-button>
        </div>
        <el-table :data="subs" size="small">
          <el-table-column label="用户" width="140"><template #default="{ row }">{{ userName(row.userId) }}</template></el-table-column>
          <el-table-column label="套餐" width="140"><template #default="{ row }">{{ planName(row.planId) }}</template></el-table-column>
          <el-table-column label="开始" width="170"><template #default="{ row }">{{ fmt(row.startsAt) }}</template></el-table-column>
          <el-table-column label="到期" width="170"><template #default="{ row }">{{ fmt(row.expiresAt) }}</template></el-table-column>
          <el-table-column label="操作"><template #default="{ row }">
            <el-popconfirm title="确认删除订阅？" @confirm="delSub(row)"><template #reference><el-button size="small" type="danger">删除</el-button></template></el-popconfirm>
          </template></el-table-column>
        </el-table>
      </el-tab-pane>
      <el-tab-pane label="本月用量">
        <el-table :data="usage" size="small">
          <el-table-column label="用户" width="160"><template #default="{ row }">{{ row.userId === 0 ? '(未归属)' : userName(row.userId) }}</template></el-table-column>
          <el-table-column prop="requests" label="请求数" width="140" />
          <el-table-column label="流量" width="140"><template #default="{ row }">{{ fmtMB(row.trafficMB) }}</template></el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog v-model="planDialog" :title="form.id ? '编辑套餐' : '创建套餐'" width="480px">
      <el-form label-width="100px">
        <el-form-item label="名称"><el-input v-model="form.name" /></el-form-item>
        <el-form-item label="网站数上限"><el-input-number v-model="form.maxServers" :min="1" /></el-form-item>
        <el-form-item label="域名数上限"><el-input-number v-model="form.maxDomains" :min="1" /></el-form-item>
        <el-form-item label="月流量(MB)"><el-input-number v-model="form.monthlyTrafficMB" :min="0" :step="1024" /><span class="tip">0=不限</span></el-form-item>
        <el-form-item label="月价(元)"><el-input-number v-model="priceYuan" :min="0" :precision="2" /></el-form-item>
        <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="planDialog = false">取消</el-button>
        <el-button type="primary" @click="savePlan">保存</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import api from '../api'

const plans = ref([])
const subs = ref([])
const users = ref([])
const usage = ref([])
const loading = ref(false)
const planDialog = ref(false)
const form = ref({})
const newSub = ref({ userId: null, planId: null })

const priceYuan = computed({
  get: () => (form.value.priceCents ?? 0) / 100,
  set: (v) => { form.value.priceCents = Math.round((v || 0) * 100) },
})

const fmt = (t) => t ? new Date(t).toLocaleString() : '-'
const fmtMB = (mb) => mb >= 1024 ? (mb / 1024).toFixed(2) + ' GB' : mb + ' MB'
const userName = (id) => users.value.find(u => u.id === id)?.username ?? `用户${id}`
const planName = (id) => plans.value.find(p => p.id === id)?.name ?? `套餐${id}`

async function load() {
  loading.value = true
  try {
    const [p, s, u, us] = await Promise.all([
      api.get('/admin/plans'), api.get('/admin/subscriptions'),
      api.get('/admin/users'), api.get('/admin/usage'),
    ])
    plans.value = p; subs.value = s; users.value = u; usage.value = us.usage
  } finally { loading.value = false }
}
onMounted(load)

function openPlan(row) {
  form.value = row ? JSON.parse(JSON.stringify(row)) : { name: '', maxServers: 10, maxDomains: 20, monthlyTrafficMB: 51200, priceCents: 0, enabled: true }
  planDialog.value = true
}

async function savePlan() {
  if (!form.value.name) return ElMessage.warning('请填写名称')
  if (form.value.id) await api.put(`/admin/plans/${form.value.id}`, form.value)
  else await api.post('/admin/plans', form.value)
  ElMessage.success('已保存')
  planDialog.value = false
  load()
}

async function delPlan(row) {
  try { await api.delete(`/admin/plans/${row.id}`); ElMessage.success('已删除'); load() }
  catch (e) { ElMessage.error(e.response?.data || '删除失败') }
}

async function addSub() {
  if (!newSub.value.userId || !newSub.value.planId) return ElMessage.warning('请选择用户与套餐')
  const now = new Date()
  await api.post('/admin/subscriptions', {
    userId: newSub.value.userId, planId: newSub.value.planId,
    startsAt: now.toISOString(), expiresAt: new Date(now.getTime() + 30 * 86400e3).toISOString(), enabled: true,
  })
  ElMessage.success('已添加订阅')
  load()
}

async function delSub(row) {
  await api.delete(`/admin/subscriptions/${row.id}`)
  ElMessage.success('已删除')
  load()
}
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
.tip { margin-left: 8px; color: var(--el-text-color-secondary); font-size: 12px; }
</style>
