<script setup lang="ts">
/** ACME 签发账户 —— 运营配置（存数据库，管理员可编辑）。
 *  邮箱是 ACME 账户唯一标识：单邮箱签发证书过多可能被 CA 限流，
 *  支持邮箱模板拼接（{userId}/{serverId} 占位符）派生不同邮箱分散账户。 */
import { ref, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage, ElMessageBox } from 'element-plus'
import CdnGate from './gate.vue'

const gate = ref()

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
const rows = ref<any[]>([])
const dialog = ref(false)
const saving = ref(false)
const form = ref<any>({})

const loadData = async () => { rows.value = await cdnApi.get('/admin/acme-accounts'); return rows.value }

function openAdd() {
  form.value = { name: '', emailTemplate: 'u{userId}.tnorvcdnssl@deathin.icu', defaultEmail: '', enabled: true, note: '',
    provider: 'letsencrypt', directoryUrl: '', backupDirectoryUrl: '', eabKid: '', eabHmacKey: '' }
  dialog.value = true
}
function openEdit(row: any) { form.value = JSON.parse(JSON.stringify(row)); dialog.value = true }

async function save() {
  saving.value = true
  try {
    if (form.value.id) await cdnApi.put(`/admin/acme-accounts/${form.value.id}`, form.value)
    else await cdnApi.post('/admin/acme-accounts', form.value)
    ElMessage.success('已保存，配置将推送到所有节点')
    dialog.value = false
    gate.value?.refresh?.()
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}

async function del(row: any) {
  await ElMessageBox.confirm(`确认删除签发账户「${row.name || row.defaultEmail}」？节点将回退到无邮箱模式。`, '删除账户', { type: 'warning' })
  await cdnApi.delete(`/admin/acme-accounts/${row.id}`)
  ElMessage.success('已删除')
  gate.value?.refresh?.()
}

const emailPreview = (row: any) => {
  const tpl = row.emailTemplate || row.defaultEmail || ''
  return tpl.replace('{userId}', '42').replace('{serverId}', '7')
}
</script>

<template>
  <div>
    <div class="page-header mb-4">
      <div class="row align-items-center">
        <div class="col-auto"><h2 class="page-title">ACME 签发账户</h2><div class="text-muted">系统设置 · 证书自动签发账户（运营配置，存数据库）</div></div>
      </div>
    </div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" />
            <el-button type="primary" size="small" @click="openAdd()">+ 新增账户</el-button>
            <el-button size="small" @click="refresh()">刷新</el-button>
          </div>
          <el-alert type="info" :closable="false" class="mb-3"
            title="一个邮箱注册的 ACME 账户签发证书过多时可能被 Let's Encrypt 限流。使用邮箱模板（如 u{userId}.tnorvcdnssl@deathin.icu）可按用户派生独立邮箱分散签发账户。邮箱变更后节点会重建 ACME 账户。"
            show-icon />
          <el-table :data="viewRows" stripe>
            <el-table-column prop="id" label="ID" width="60" />
            <el-table-column prop="name" label="账户名" width="160">
              <template #default="{ row }">{{ row.name || '（未命名）' }}</template>
            </el-table-column>
            <el-table-column label="邮箱模板" min-width="240">
              <template #default="{ row }">
                <div>{{ row.emailTemplate || row.defaultEmail }}</div>
                <div class="text-muted small">示例派生：{{ emailPreview(row) }}</div>
              </template>
            </el-table-column>
            <el-table-column label="启用" width="80">
              <template #default="{ row }">
                <el-tag size="small" :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '启用' : '停用' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="note" label="备注" min-width="160" />
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
          <el-empty v-if="!rows.length" description="尚未配置签发账户：节点将以无邮箱模式签发（每节点独立账户）" />
        </el-card>

        <el-dialog v-model="dialog" :title="form.id ? '编辑签发账户' : '新增签发账户'" width="560px">
          <el-form label-width="110px">
            <el-form-item label="账户名"><el-input v-model="form.name" placeholder="如 主签发账户" /></el-form-item>
            <el-form-item label="邮箱模板">
              <el-input v-model="form.emailTemplate" placeholder="u{userId}.tnorvcdnssl@deathin.icu" />
              <div class="text-muted small mt-1">支持占位符 {userId}（网站所属用户）、{serverId}（节点 ID）；留空则使用固定邮箱</div>
            </el-form-item>
            <el-form-item label="固定邮箱">
              <el-input v-model="form.defaultEmail" placeholder="所有签发共用一个邮箱（不推荐大量站点使用）" />
            </el-form-item>
            <el-form-item label="启用"><el-switch v-model="form.enabled" /></el-form-item>
            <el-divider>CA 提供商（限流备用）</el-divider>
            <el-form-item label="主 CA">
              <el-select v-model="form.provider" style="width:240px">
                <el-option value="letsencrypt" label="Let's Encrypt（默认）" />
                <el-option value="letsencrypt-staging" label="Let's Encrypt 测试环境" />
                <el-option value="google" label="Google Trust Services" />
                <el-option value="zerossl" label="ZeroSSL" />
                <el-option value="custom" label="自定义 ACME" />
              </el-select>
            </el-form-item>
            <el-form-item v-if="form.provider === 'custom'" label="目录 URL">
              <el-input v-model="form.directoryUrl" placeholder="https://acme.example.com/directory" />
            </el-form-item>
            <el-form-item label="备用 CA 目录">
              <el-input v-model="form.backupDirectoryUrl" placeholder="https://dv.acme-v02.api.pki.goog/directory" />
              <div class="text-muted small mt-1">主 CA 限流（如 Let's Encrypt 每域名 5 次失败上限，需等 30 分钟）时自动切换备用 CA 重试，避免等待</div>
            </el-form-item>
            <el-form-item label="EAB Key ID">
              <el-input v-model="form.eabKid" placeholder="备用 CA 的 EAB KeyID（Google/ZeroSSL 需要）" />
            </el-form-item>
            <el-form-item label="EAB HMAC Key">
              <el-input v-model="form.eabHmacKey" placeholder="备用 CA 的 EAB HMAC Key（Base64URL）" />
            </el-form-item>
            <el-form-item label="备注"><el-input v-model="form.note" type="textarea" :rows="2" /></el-form-item>
          </el-form>
          <template #footer>
            <el-button @click="dialog = false">取消</el-button>
            <el-button type="primary" :loading="saving" @click="save">保存</el-button>
          </template>
        </el-dialog>
      </template>
    </CdnGate>
  </div>
</template>
