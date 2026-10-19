<script setup lang="ts">
/**
 * 路由规则（locations）—— 通用设计
 * 按路径前缀配置独立覆盖：缓存 / CC / IP黑白名单 / 防盗链 / 请求限制 / 访客IP头。
 * 未配置的字段继承站点级；先配置的规则优先；真实下发节点执行。
 */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const rules = ref<any[]>([])
const loading = ref(false)
const load = async () => {
  loading.value = true
  try {
    rules.value = await cdnApi.get(`/admin/servers/${props.serverId}/locations`)
  } finally {
    loading.value = false
  }
}
watch(() => props.serverId, load, { immediate: true })

const blankRule = () => ({
  pathPrefix: '',
  enabled: true,
  cache: null as any,
  security: null as any,
  advanced: null as any,
  requestLimitPerMinute: -1,
  remoteAddrHeader: null as string | null,
})
const add = () => rules.value.push(blankRule())
const rm = (i: number) => rules.value.splice(i, 1)
const up = (i: number) => { if (i > 0) { const [r] = rules.value.splice(i, 1); rules.value.splice(i - 1, 0, r) } }

const editing = ref(-1) // 展开编辑的规则下标
const toggleCache = (r: any) => {
  if (r.cache) r.cache = null
  else r.cache = { enabled: true, rules: [{ matchType: 'extension', value: 'png,jpg,css,js', ttlSeconds: 3600, clientTtlSeconds: 0, ignoreQueryString: false }] }
}
const toggleSecurity = (r: any) => {
  if (r.security) r.security = null
  else r.security = { cc: { enabled: true, threshold: 120, windowSeconds: 60, banSeconds: 300, excludePaths: [] }, ipBlacklist: [], ipWhitelist: [], referers: null }
}
const toggleAdvanced = (r: any) => {
  if (r.advanced) r.advanced = null
  else r.advanced = { headers: [], rewrites: [], compression: null, charset: '' }
}
const toggleRef = (r: any) => {
  const s = r.security || (r.security = {})
  if (s.referers) s.referers = null
  else s.referers = { enabled: true, allowEmpty: true, whitelist: [], blacklist: [] }
}

const saving = ref(false)
const save = async () => {
  saving.value = true
  try {
    await cdnApi.put(`/admin/servers/${props.serverId}/locations`, rules.value)
    ElMessage.success('路由规则已保存并下发到节点')
    await load()
    emit('saved')
  } finally {
    saving.value = false
  }
}

const matchTypes = [
  { v: 'extension', l: '文件扩展名' },
  { v: 'path-prefix', l: '路径前缀' },
  { v: 'path-exact', l: '精确路径' },
  { v: 'homepage', l: '首页' },
  { v: 'all', l: '全部' },
]
const csvOf = (v: any) => (Array.isArray(v) ? v.join(',') : v || '')
const parseCsv = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean)
</script>

<template>
  <div v-loading="loading">
    <h5 class="mb-2">路由规则</h5>
    <el-alert type="info" :closable="false" class="mb-3"
      title="命中路径前缀的请求按本条规则覆盖站点设置（缓存/CC/IP名单/防盗链/请求限制/访客IP头）；先配置的规则优先；未配置项继承站点级" />

    <el-table :data="rules" size="small" stripe style="max-width: 980px">
      <el-table-column label="顺序" width="70">
        <template #default="{ $index }">
          <el-button size="small" text :disabled="$index === 0" @click="up($index)">↑</el-button>
        </template>
      </el-table-column>
      <el-table-column label="路径前缀" min-width="150">
        <template #default="{ row }"><el-input v-model="row.pathPrefix" size="small" placeholder="/api/" /></template>
      </el-table-column>
      <el-table-column label="覆盖项" min-width="220">
        <template #default="{ row }">
          <el-tag v-if="row.cache" size="small" class="me-1">缓存</el-tag>
          <el-tag v-if="row.security?.cc" size="small" type="warning" class="me-1">CC {{ row.security.cc.threshold }}/分</el-tag>
          <el-tag v-if="row.security?.ipBlacklist?.length" size="small" type="danger" class="me-1">黑名单×{{ row.security.ipBlacklist.length }}</el-tag>
          <el-tag v-if="row.security?.ipWhitelist?.length" size="small" type="success" class="me-1">白名单×{{ row.security.ipWhitelist.length }}</el-tag>
          <el-tag v-if="row.security?.referers" size="small" type="warning" class="me-1">防盗链</el-tag>
          <el-tag v-if="row.advanced" size="small" type="info" class="me-1">高级覆盖</el-tag>
          <el-tag v-if="row.requestLimitPerMinute > 0" size="small" class="me-1">限速 {{ row.requestLimitPerMinute }}/分</el-tag>
          <el-tag v-if="row.remoteAddrHeader" size="small" type="info">{{ row.remoteAddrHeader }}</el-tag>
          <span v-if="!row.cache && !row.security && !row.advanced && row.requestLimitPerMinute < 0 && !row.remoteAddrHeader" class="text-muted small">（仅路径标记，全部继承）</span>
        </template>
      </el-table-column>
      <el-table-column label="启用" width="70">
        <template #default="{ row }"><el-switch v-model="row.enabled" size="small" /></template>
      </el-table-column>
      <el-table-column label="操作" fixed="right">
        <template #default="{ row, $index }">
          <div class="d-flex align-items-center" style="gap: 4px; flex-wrap: nowrap">
          <el-button size="small" text type="primary" @click="editing = editing === $index ? -1 : $index">{{ editing === $index ? '收起' : '配置' }}</el-button>
          <el-button size="small" text type="danger" @click="rm($index)">删除</el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <div class="mt-2 mb-3">
      <el-button size="small" @click="add">+ 添加规则</el-button>
      <el-button type="primary" :loading="saving" @click="save">保存并下发</el-button>
    </div>

    <!-- 规则详情编辑 -->
    <el-card v-for="(r, i) in rules" v-show="editing === i" :key="i" class="mb-3" style="max-width: 980px"
      body-style="padding: 14px 18px">
      <template #header><b>规则：{{ r.pathPrefix || '（未填前缀）' }}</b></template>

      <el-form label-width="130px" size="small">
        <el-divider content-position="left">缓存覆盖</el-divider>
        <el-form-item label="启用缓存覆盖">
          <el-switch :model-value="!!r.cache" @change="toggleCache(r)" />
          <span class="text-muted ms-2 small">{{ r.cache ? '该路径使用独立缓存规则' : '继承站点级缓存' }}</span>
        </el-form-item>
        <template v-if="r.cache">
          <el-form-item v-for="(cr, ci) in r.cache.rules" :key="ci" :label="`规则 ${ci + 1}`">
            <el-select v-model="cr.matchType" style="width: 130px" size="small">
              <el-option v-for="m in matchTypes" :key="m.v" :value="m.v" :label="m.l" />
            </el-select>
            <el-input v-if="['extension', 'path-prefix', 'path-exact'].includes(cr.matchType)" v-model="cr.value"
              class="ms-2" style="width: 200px" size="small"
              :placeholder="cr.matchType === 'extension' ? 'png,jpg,css' : '/static/'" />
            <span class="ms-2 small">TTL</span>
            <el-input-number v-model="cr.ttlSeconds" :min="1" size="small" controls-position="right" class="ms-1" style="width: 130px" />
            <span class="small ms-1">秒</span>
            <el-button size="small" text type="danger" class="ms-2" @click="r.cache.rules.splice(ci, 1)">删除</el-button>
          </el-form-item>
          <el-form-item label="">
            <el-button size="small" @click="r.cache.rules.push({ matchType: 'extension', value: '', ttlSeconds: 3600, clientTtlSeconds: 0, ignoreQueryString: false })">+ 添加缓存规则</el-button>
          </el-form-item>
        </template>

        <el-divider content-position="left">安全覆盖</el-divider>
        <el-form-item label="启用安全覆盖">
          <el-switch :model-value="!!r.security" @change="toggleSecurity(r)" />
          <span class="text-muted ms-2 small">{{ r.security ? '该路径使用独立安全配置' : '继承站点级安全' }}</span>
        </el-form-item>
        <template v-if="r.security">
          <el-form-item label="CC 限流">
            <el-switch v-model="r.security.cc.enabled" size="small" class="me-2" />
            <span class="small">阈值</span>
            <el-input-number v-model="r.security.cc.threshold" :min="1" size="small" controls-position="right" class="mx-1" style="width: 120px" />
            <span class="small">次/分钟，封禁</span>
            <el-input-number v-model="r.security.cc.banSeconds" :min="0" size="small" controls-position="right" class="mx-1" style="width: 120px" />
            <span class="small">秒</span>
          </el-form-item>
          <el-form-item label="IP 黑名单">
            <el-input :model-value="csvOf((r.security.ipBlacklist || []).map((x: any) => x.cidr))"
              @update:model-value="(v: string) => r.security.ipBlacklist = parseCsv(v).map((c: string) => ({ cidr: c }))" placeholder="1.2.3.4, 10.0.0.0/8" />
          </el-form-item>
          <el-form-item label="IP 白名单">
            <el-input :model-value="csvOf((r.security.ipWhitelist || []).map((x: any) => x.cidr))"
              @update:model-value="(v: string) => r.security.ipWhitelist = parseCsv(v).map((c: string) => ({ cidr: c }))" placeholder="命中后跳过整条安全链" />
          </el-form-item>
          <el-form-item label="Referer 防盗链">
            <el-switch :model-value="!!r.security.referers" @change="toggleRef(r)" size="small" />
            <template v-if="r.security.referers">
              <el-input :model-value="csvOf(r.security.referers.whitelist)"
                @update:model-value="(v: string) => r.security.referers.whitelist = parseCsv(v)" class="ms-2" style="width: 240px" placeholder="白名单域名 example.com" />
              <el-checkbox v-model="r.security.referers.allowEmpty" class="ms-2">允许空 Referer</el-checkbox>
            </template>
          </el-form-item>
        </template>

        <el-divider content-position="left">高级覆盖（报头/重写/压缩/字符集）</el-divider>
        <el-form-item label="启用高级覆盖">
          <el-switch :model-value="!!r.advanced" @change="toggleAdvanced(r)" />
          <span class="text-muted ms-2 small">{{ r.advanced ? '报头与重写整组替换，压缩/字符集可独立覆盖' : '继承站点级高级配置' }}</span>
        </el-form-item>
        <template v-if="r.advanced">
          <el-form-item v-for="(h, hi) in r.advanced.headers" :key="'h' + hi" :label="`报头 ${hi + 1}`">
            <el-select v-model="h.direction" size="small" style="width: 100px">
              <el-option value="request" label="请求" /><el-option value="response" label="响应" />
            </el-select>
            <el-select v-model="h.action" size="small" style="width: 90px" class="ms-1">
              <el-option value="set" label="设置" /><el-option value="add" label="追加" /><el-option value="del" label="删除" />
            </el-select>
            <el-input v-model="h.name" size="small" class="ms-1" style="width: 150px" placeholder="X-Custom" />
            <el-input v-model="h.value" size="small" class="ms-1" style="width: 150px" placeholder="值" />
            <el-button size="small" text type="danger" class="ms-1" @click="r.advanced.headers.splice(hi, 1)">删除</el-button>
          </el-form-item>
          <el-form-item label="">
            <el-button size="small" @click="r.advanced.headers.push({ direction: 'response', action: 'set', name: '', value: '', enabled: true })">+ 添加报头</el-button>
          </el-form-item>
          <el-form-item label="禁用压缩">
            <el-select v-model="r.advanced.compression" size="small" style="width: 160px" clearable placeholder="继承站点级">
              <el-option :value="false" label="禁用压缩" /><el-option :value="true" label="启用压缩" />
            </el-select>
          </el-form-item>
          <el-form-item label="字符集">
            <el-input v-model="r.advanced.charset" size="small" style="width: 160px" placeholder="空 = 继承站点级" />
          </el-form-item>
        </template>

        <el-divider content-position="left">其他覆盖</el-divider>
        <el-form-item label="请求限制">
          <el-input-number :model-value="r.requestLimitPerMinute" :min="-1" size="small" controls-position="right" style="width: 150px"
            @update:model-value="(v: number) => r.requestLimitPerMinute = v" />
          <span class="text-muted ms-2 small">-1 = 继承站点级；&gt;0 = 该路径每 IP 每分钟上限</span>
        </el-form-item>
        <el-form-item label="访客IP头">
          <el-input v-model="r.remoteAddrHeader" style="width: 240px" placeholder="如 CF-Connecting-IP；留空=继承" />
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>
