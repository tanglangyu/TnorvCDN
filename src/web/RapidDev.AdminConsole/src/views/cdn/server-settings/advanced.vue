<script setup lang="ts">
/**
 * 高级功能子页（field 驱动）：redirects / rewrites / headers / compression / optimization
 * webp / charset / requestLimit / websocket / grpc / multimedia / remoteAddr / pages / traffic
 * 保存走 PUT /admin/servers/{id}/advanced（整体对象提交）。
 */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any; field: string }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const adv = ref<any>({})
watch(() => props.server, (s) => {
  if (!s) return
  const a = JSON.parse(JSON.stringify(s.advanced || {}))
  a.redirects = a.redirects || []
  a.rewrites = a.rewrites || []
  a.headers = a.headers || []
  a.basicAuth = a.basicAuth || { enabled: false, users: [] }
  a.customPages = a.customPages || {}
  a.multimedia = a.multimedia || { mp4Drag: true, flvLive: false }
  a.trafficLimit = a.trafficLimit || { monthlyGB: 0, action: 'block' }
  a.websocketTimeoutSeconds = a.websocketTimeoutSeconds || 0
  a.remoteAddrHeader = a.remoteAddrHeader || ''
  a.charset = a.charset || ''
  adv.value = a
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  saving.value = true
  try {
    await cdnApi.put('/admin/servers/' + props.serverId + '/advanced', adv.value)
    ElMessage.success('已保存并下发到节点')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}

const titles: any = {
  redirects: 'URL 跳转', rewrites: '重写规则', headers: 'HTTP 报头', compression: '内容压缩',
  optimization: '页面优化', webp: 'WebP 自适应', charset: '字符编码', requestLimit: '请求限制',
  websocket: 'Websocket', grpc: 'gRPC', multimedia: '音视频设置', remoteAddr: '访客IP地址',
  pages: '自定义页面', traffic: '流量限制',
}
</script>

<template>
  <div>
    <h5 class="mb-3">{{ titles[field] }}</h5>

    <!-- URL 跳转 -->
    <template v-if="field === 'redirects'">
      <el-table :data="adv.redirects" size="small" stripe class="mb-2" style="max-width: 900px">
        <el-table-column label="匹配（前缀/正则）" min-width="200"><template #default="{ row }"><el-input v-model="row.pattern" size="small" placeholder="^/old/(.*)$" /></template></el-table-column>
        <el-table-column label="目标（支持 $1）" min-width="220"><template #default="{ row }"><el-input v-model="row.target" size="small" placeholder="https://new.com/$1" /></template></el-table-column>
        <el-table-column label="状态码" width="120">
          <template #default="{ row }"><el-select v-model="row.code" size="small"><el-option :value="301" /><el-option :value="302" /><el-option :value="307" /><el-option :value="308" /></el-select></template>
        </el-table-column>
        <el-table-column label="启用" width="70"><template #default="{ row }"><el-switch v-model="row.enabled" size="small" /></template></el-table-column>
        <el-table-column label="操作" width="70"><template #default="{ $index }"><el-button size="small" type="danger" text @click="adv.redirects.splice($index, 1)">删除</el-button></template></el-table-column>
      </el-table>
      <el-button size="small" class="mb-3" @click="adv.redirects.push({ pattern: '', target: '', code: 301, enabled: true })">+ 添加跳转</el-button>
    </template>

    <!-- 重写规则 -->
    <template v-else-if="field === 'rewrites'">
      <el-table :data="adv.rewrites" size="small" stripe class="mb-2" style="max-width: 900px">
        <el-table-column label="正则" min-width="240"><template #default="{ row }"><el-input v-model="row.pattern" size="small" placeholder="^/a/(.*)$" /></template></el-table-column>
        <el-table-column label="替换为" min-width="240"><template #default="{ row }"><el-input v-model="row.replacement" size="small" placeholder="/b/$1" /></template></el-table-column>
        <el-table-column label="启用" width="70"><template #default="{ row }"><el-switch v-model="row.enabled" size="small" /></template></el-table-column>
        <el-table-column label="操作" width="70"><template #default="{ $index }"><el-button size="small" type="danger" text @click="adv.rewrites.splice($index, 1)">删除</el-button></template></el-table-column>
      </el-table>
      <el-button size="small" class="mb-3" @click="adv.rewrites.push({ pattern: '', replacement: '', enabled: true })">+ 添加规则</el-button>
    </template>

    <!-- HTTP 报头 -->
    <template v-else-if="field === 'headers'">
      <el-table :data="adv.headers" size="small" stripe class="mb-2" style="max-width: 900px">
        <el-table-column label="方向" width="120">
          <template #default="{ row }"><el-select v-model="row.direction" size="small"><el-option value="request" label="请求" /><el-option value="response" label="响应" /></el-select></template>
        </el-table-column>
        <el-table-column label="动作" width="110">
          <template #default="{ row }"><el-select v-model="row.action" size="small"><el-option value="set" label="设置" /><el-option value="add" label="添加" /><el-option value="del" label="删除" /></el-select></template>
        </el-table-column>
        <el-table-column label="头名" min-width="160"><template #default="{ row }"><el-input v-model="row.name" size="small" placeholder="X-Custom-Header" /></template></el-table-column>
        <el-table-column label="值" min-width="200"><template #default="{ row }"><el-input v-model="row.value" size="small" /></template></el-table-column>
        <el-table-column label="启用" width="70"><template #default="{ row }"><el-switch v-model="row.enabled" size="small" /></template></el-table-column>
        <el-table-column label="操作" width="70"><template #default="{ $index }"><el-button size="small" type="danger" text @click="adv.headers.splice($index, 1)">删除</el-button></template></el-table-column>
      </el-table>
      <el-button size="small" class="mb-3" @click="adv.headers.push({ direction: 'response', action: 'set', name: '', value: '', enabled: true })">+ 添加报头</el-button>
    </template>

    <!-- 内容压缩 / 页面优化 -->
    <template v-else-if="field === 'compression' || field === 'optimization'">
      <el-form label-width="180px" style="max-width: 620px">
        <el-form-item :label="field === 'compression' ? 'Gzip/Brotli 压缩' : 'HTML/文本压缩输出'">
          <el-switch v-model="adv.compression" />
          <span class="text-muted small ms-2">对 text/html、css、js、json 等响应自动压缩</span>
        </el-form-item>
        <el-form-item v-if="field === 'optimization'" label="说明">
          <span class="text-muted small">页面优化（空白符精简）与内容压缩共用同一开关，节点在压缩时处理文本类响应</span>
        </el-form-item>
      </el-form>
    </template>

    <!-- WebP -->
    <template v-else-if="field === 'webp'">
      <el-form label-width="180px" style="max-width: 620px">
        <el-form-item label="WebP 自适应">
          <el-switch v-model="adv.webpOptimization" />
          <span class="text-muted small ms-2">浏览器支持 webp 时 png/jpg 请求自动改写为 .webp 路径（404 自动回退原图）</span>
        </el-form-item>
      </el-form>
    </template>

    <!-- 字符编码 -->
    <template v-else-if="field === 'charset'">
      <el-form label-width="180px" style="max-width: 620px">
        <el-form-item label="强制字符编码">
          <el-select v-model="adv.charset" allow-create filterable clearable placeholder="跟随源站" style="width: 220px">
            <el-option value="utf-8" /><el-option value="gbk" /><el-option value="gb2312" />
          </el-select>
          <span class="text-muted small ms-2">源站未声明 charset 时为 text/* 响应追加</span>
        </el-form-item>
      </el-form>
    </template>

    <!-- 请求限制 -->
    <template v-else-if="field === 'requestLimit'">
      <el-form label-width="220px" style="max-width: 640px">
        <el-form-item label="单 IP 请求限制（次/分钟）">
          <el-input-number v-model="adv.requestLimitPerMinute" :min="0" :max="1000000" />
          <span class="text-muted small ms-2">0 = 不限制</span>
        </el-form-item>
        <el-form-item label="请求体上限（MB）">
          <el-input-number v-model="adv.maxBodySizeMB" :min="0" :max="10240" />
          <span class="text-muted small ms-2">0 = 不限制</span>
        </el-form-item>
      </el-form>
    </template>

    <!-- Websocket -->
    <template v-else-if="field === 'websocket'">
      <el-form label-width="220px" style="max-width: 640px">
        <el-form-item label="Websocket 透传"><el-switch v-model="adv.websocket" /></el-form-item>
        <el-form-item label="空闲超时（秒）">
          <el-input-number v-model="adv.websocketTimeoutSeconds" :min="0" :max="86400" :disabled="true" />
          <span class="text-muted small ms-2">节点当前保持连接不主动断开（0 = 默认）</span>
        </el-form-item>
      </el-form>
    </template>

    <!-- gRPC -->
    <template v-else-if="field === 'grpc'">
      <el-form label-width="220px" style="max-width: 680px">
        <el-form-item label="gRPC（HTTP/2 回源）">
          <el-switch v-model="adv.grpc" />
        </el-form-item>
        <el-alert type="info" :closable="false" title="保存配置；节点 HTTP/2 回源通道上线后自动生效（当前透传 HTTP/1.1）" />
      </el-form>
    </template>

    <!-- 音视频 -->
    <template v-else-if="field === 'multimedia'">
      <el-form label-width="220px" style="max-width: 640px">
        <el-form-item label="MP4 拖拽播放（Range）">
          <el-switch v-model="adv.multimedia.mp4Drag" disabled />
          <span class="text-muted small ms-2">节点默认支持 Range 分段请求</span>
        </el-form-item>
        <el-form-item label="FLV 直播流透传">
          <el-switch v-model="adv.multimedia.flvLive" />
          <span class="text-muted small ms-2">.flv 请求绕过缓存、禁用缓冲（直播拉流低延迟）</span>
        </el-form-item>
      </el-form>
    </template>

    <!-- 访客 IP -->
    <template v-else-if="field === 'remoteAddr'">
      <el-form label-width="220px" style="max-width: 680px">
        <el-form-item label="真实 IP 取值头">
          <el-select v-model="adv.remoteAddrHeader" clearable allow-create filterable placeholder="默认（XFF → X-Real-IP → 直连）" style="width: 320px">
            <el-option value="X-Forwarded-For" /><el-option value="X-Real-IP" /><el-option value="CF-Connecting-IP" /><el-option value="True-Client-IP" />
          </el-select>
        </el-form-item>
        <el-alert type="warning" :closable="false" title="仅在站点前还有可信代理层时设置；伪造该头会导致 CC/WAF 被绕过" />
      </el-form>
    </template>

    <!-- 自定义页面 -->
    <template v-else-if="field === 'pages'">
      <el-form label-width="120px">
        <el-form-item label="404 页面"><el-input v-model="adv.customPages.notFound" type="textarea" :rows="4" placeholder="404 时返回的 HTML（空=默认）" style="max-width: 720px" /></el-form-item>
        <el-form-item label="502/504 页面"><el-input v-model="adv.customPages.serverError" type="textarea" :rows="4" placeholder="源站错误时返回的 HTML" style="max-width: 720px" /></el-form-item>
        <el-form-item label="封禁页面"><el-input v-model="adv.customPages.blocked" type="textarea" :rows="4" placeholder="WAF/CC/流量超限时返回的 HTML" style="max-width: 720px" /></el-form-item>
      </el-form>
    </template>

    <!-- 流量限制 -->
    <template v-else-if="field === 'traffic'">
      <el-form label-width="220px" style="max-width: 680px">
        <el-form-item label="月流量上限（GB）">
          <el-input-number v-model="adv.trafficLimit.monthlyGB" :min="0" :max="1024 * 1024" />
          <span class="text-muted small ms-2">0 = 不限制；按自然月统计，主控每轮配置发布时判定</span>
        </el-form-item>
        <el-form-item label="超限动作">
          <el-select v-model="adv.trafficLimit.action" style="width: 220px">
            <el-option value="block" label="返回 503 封禁页" />
            <el-option value="mark" label="仅标记（不拦截）" />
          </el-select>
        </el-form-item>
      </el-form>
    </template>

    <el-button type="primary" :loading="saving" class="mt-3" @click="save">保存</el-button>
  </div>
</template>
