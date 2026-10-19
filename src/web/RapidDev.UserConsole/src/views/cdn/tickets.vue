<script setup lang="ts">
/** 工单系统（U6）—— 气泡式对话详情 + 固定底部回复框 + 分类/优先级 + 状态徽标 */
import { ref, nextTick } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()
const newDialog = ref(false)
const chatDialog = ref(false)
const detail = ref<any>(null)
const detailLoading = ref(false)
const replyText = ref('')
const replying = ref(false)
const msgsRef = ref<HTMLElement>()
const form = ref({ title: '', category: '网站问题', priority: 'medium', content: '' })
const filterQ = ref('')
const statusFilter = ref('')
const pager = ref({ page: 1, pageSize: 20, total: 0 })

const statusLabel = (s: string) => ({ open: '进行中', replied: '已回复', closed: '已关闭' } as any)[s] || s
const statusTag = (s: string) => ({ open: 'warning', replied: 'success', closed: 'info' } as any)[s] || 'info'
const priorityLabel = (p: string) => ({ low: '低', medium: '中', high: '高' } as any)[p] || p
const priorityTag = (p: string) => ({ low: 'info', medium: 'primary', high: 'danger' } as any)[p] || 'info'

const loadData = async () => {
  const r = await cdnApi.get('/user/tickets', {
    page: pager.value.page,
    pageSize: pager.value.pageSize,
    q: filterQ.value || undefined,
    status: statusFilter.value || undefined,
  }) as any
  const d = r?.data ?? r
  if (d && !Array.isArray(d) && 'total' in d) {
    pager.value.total = d.total
    return d.items || []
  }
  pager.value.total = Array.isArray(d) ? d.length : 0
  return d || []
}

const onSearch = () => {
  pager.value.page = 1
  gate.value?.refresh?.()
}

async function openDetail(row: any) {
  detailLoading.value = true
  try {
    const r = (await cdnApi.get(`/user/tickets/${row.id}`)) as any
    detail.value = r?.data ?? r
    chatDialog.value = true
    await nextTick()
    scrollBottom()
  } catch { ElMessage.error('加载工单详情失败') }
  finally { detailLoading.value = false }
}

function scrollBottom() {
  if (msgsRef.value) msgsRef.value.scrollTop = msgsRef.value.scrollHeight
}

async function sendReply() {
  if (!replyText.value.trim() || !detail.value) return
  replying.value = true
  try {
    await cdnApi.post(`/user/tickets/${detail.value.id}/reply`, { content: replyText.value.trim() })
    replyText.value = ''
    const r = (await cdnApi.get(`/user/tickets/${detail.value.id}`)) as any
    detail.value = r?.data ?? r
    ElMessage.success('已回复')
    gate.value?.refresh?.()
    await nextTick()
    scrollBottom()
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '回复失败') }
  finally { replying.value = false }
}

async function closeTicket() {
  if (!detail.value) return
  try {
    await cdnApi.post(`/user/tickets/${detail.value.id}/close`)
    detail.value.status = 'closed'
    ElMessage.success('工单已关闭')
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '操作失败') }
}

async function submit() {
  if (!form.value.title) return ElMessage.warning('请填写标题')
  if (!form.value.content) return ElMessage.warning('请填写问题描述')
  try {
    await cdnApi.post('/user/tickets', form.value)
    ElMessage.success('工单已提交')
    newDialog.value = false
    form.value = { title: '', category: '网站问题', priority: 'medium', content: '' }
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data?.message || '提交失败') }
}
</script>
<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">工单系统</h2><div class="text-muted">提交与跟踪支持工单，点击行查看对话详情</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ data, refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2 align-items-center flex-wrap">
            <el-button type="primary" size="small" @click="newDialog = true">+ 提交工单</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
            <el-radio-group v-model="statusFilter" size="small" @change="onSearch">
              <el-radio-button value="">全部</el-radio-button>
              <el-radio-button value="open">进行中</el-radio-button>
              <el-radio-button value="replied">已回复</el-radio-button>
              <el-radio-button value="closed">已关闭</el-radio-button>
            </el-radio-group>
            <el-input v-model="filterQ" placeholder="标题/内容关键词" size="small" clearable style="width: 200px"
              @keyup.enter="onSearch" @clear="onSearch">
              <template #append><el-button @click="onSearch"><i class="ti ti-search"></i></el-button></template>
            </el-input>
          </div>
          <el-table :data="data || []" stripe @row-click="openDetail" style="cursor: pointer">
            <el-table-column prop="id" label="ID" width="70" />
            <el-table-column prop="title" label="标题" min-width="180" />
            <el-table-column label="分类" width="110">
              <template #default="{ row }"><el-tag v-if="row.category" size="small" type="info">{{ row.category }}</el-tag><span v-else class="text-muted">—</span></template>
            </el-table-column>
            <el-table-column label="状态" width="100">
              <template #default="{ row }"><el-tag size="small" :type="statusTag(row.status)">{{ statusLabel(row.status) }}</el-tag></template>
            </el-table-column>
            <el-table-column label="优先级" width="90">
              <template #default="{ row }"><el-tag size="small" :type="priorityTag(row.priority)" effect="plain">{{ priorityLabel(row.priority) }}</el-tag></template>
            </el-table-column>
            <el-table-column label="时间" width="170">
              <template #default="{ row }">{{ row.createdAt ? new Date(row.createdAt).toLocaleString() : '-' }}</template>
            </el-table-column>
          </el-table>
          <el-pagination v-if="pager.total > pager.pageSize" class="mt-3" background
            layout="total, sizes, prev, pager, next" :total="pager.total"
            :current-page="pager.page" :page-size="pager.pageSize" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p; gate.value?.refresh?.() }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1; gate.value?.refresh?.() }" />
          <el-empty v-if="!(data || []).length" description="暂无工单" />
        </el-card>

        <!-- 提交工单 -->
        <el-dialog v-model="newDialog" title="提交工单" width="520px">
          <el-form label-width="90px">
            <el-form-item label="分类">
              <el-select v-model="form.category" style="width: 180px">
                <el-option v-for="c in ['账号问题','计费充值','网站接入','HTTPS证书','智能DNS','攻击防护','功能建议','其他']" :key="c" :value="c" :label="c" />
              </el-select>
            </el-form-item>
            <el-form-item label="优先级">
              <el-radio-group v-model="form.priority">
                <el-radio-button value="low">低</el-radio-button>
                <el-radio-button value="medium">中</el-radio-button>
                <el-radio-button value="high">高</el-radio-button>
              </el-radio-group>
            </el-form-item>
            <el-form-item label="标题"><el-input v-model="form.title" /></el-form-item>
            <el-form-item label="问题描述"><el-input v-model="form.content" type="textarea" :rows="4" placeholder="请尽量描述清楚问题现象、涉及域名与时间" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="newDialog = false">取消</el-button>
            <el-button type="primary" @click="submit">提交</el-button>
          </template>
        </el-dialog>

        <!-- 工单详情：气泡对话 -->
        <el-dialog v-model="chatDialog" :title="detail?.title || '工单详情'" width="640px" @closed="detail = null">
          <div class="mb-2 d-flex align-items-center gap-2">
            <el-tag size="small" :type="statusTag(detail?.status)">{{ statusLabel(detail?.status || '') }}</el-tag>
            <el-tag v-if="detail?.category" size="small" type="info">{{ detail.category }}</el-tag>
            <el-tag size="small" :type="priorityTag(detail?.priority || 'medium')" effect="plain">{{ priorityLabel(detail?.priority || 'medium') }}优先</el-tag>
            <el-button v-if="detail && detail.status !== 'closed'" size="small" text type="danger" class="ms-auto" @click="closeTicket">关闭工单</el-button>
          </div>
          <div ref="msgsRef" style="max-height: 380px; overflow-y: auto; padding: 8px 4px; background: var(--bs-tertiary-bg, #f7f8fa); border-radius: 8px">
            <div v-for="(m, i) in detail?.messages || []" :key="i"
              class="d-flex mb-2" :class="m.from === 'user' ? 'justify-content-end' : 'justify-content-start'">
              <div :class="m.from === 'user' ? 'text-end' : 'text-start'" style="max-width: 78%">
                <div class="text-muted" style="font-size: 11px; margin: 0 6px 2px">{{ m.from === 'user' ? '我' : '客服' }} · {{ new Date(m.at).toLocaleString() }}</div>
                <div style="padding: 8px 12px; border-radius: 10px; display: inline-block; text-align: left; white-space: pre-wrap; word-break: break-word"
                  :style="m.from === 'user'
                    ? 'background: #409EFF; color: #fff; border-top-right-radius: 2px'
                    : 'background: #fff; color: #303133; border: 1px solid #e4e7ed; border-top-left-radius: 2px'">
                  {{ m.content }}
                </div>
              </div>
            </div>
            <el-empty v-if="!(detail?.messages || []).length" description="暂无对话" :image-size="50" />
          </div>
          <div v-if="detail && detail.status !== 'closed'" class="d-flex gap-2 mt-3">
            <el-input v-model="replyText" type="textarea" :rows="2" placeholder="输入回复内容…（Ctrl+Enter 发送）"
              @keydown.ctrl.enter="sendReply" />
            <el-button type="primary" :loading="replying" @click="sendReply" style="align-self: flex-end">发送</el-button>
          </div>
          <el-alert v-else type="info" :closable="false" title="工单已关闭，如需继续请重新提交" class="mt-3" />
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
