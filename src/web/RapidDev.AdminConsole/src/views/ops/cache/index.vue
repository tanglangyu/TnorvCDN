<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { ref, onMounted } from 'vue'
import { opsApi } from '@/api/features'
import { TCard, TButton, TInput, TModal } from '@/components/t'
import { formatTime } from '@/utils/time'

const loading = ref(false)
const info = ref<any>(null)
const infoErr = ref('')
const keys = ref<any[]>([])
const keyPattern = ref('')
const keyCount = ref(50)
const clearPrefix = ref('')
const keyDetailData = ref<any>(null)
const showDetail = ref(false)
const detailLoading = ref(false)

const isGarnet = () => info.value?.isGarnet === true

async function loadInfo() {
  infoErr.value = ''
  try {
    const r: any = await opsApi.cacheInfo()
    info.value = r
    if (r?.error) infoErr.value = r.error
  } catch (e: any) { infoErr.value = e?.message || '获取失败' }
}

async function loadKeys() {
  loading.value = true
  try {
    const r: any = await opsApi.cacheKeys({ pattern: keyPattern.value || undefined, count: keyCount.value })
    // 后端返回 { keys: [...], count } 或 { error }
    if (r?.error) { keys.value = []; return }
    keys.value = Array.isArray(r?.keys) ? r.keys : []
  } catch { keys.value = [] } finally { loading.value = false }
}

async function refresh() {
  await Promise.all([loadInfo(), loadKeys()])
}

async function viewKey(key: string) {
  detailLoading.value = true
  showDetail.value = true
  try {
    keyDetailData.value = await opsApi.cacheKeyDetail(key)
  } catch { keyDetailData.value = { error: '获取失败' } } finally { detailLoading.value = false }
}

async function clearKeys() {
  if (!clearPrefix.value) return
  if (!confirm(`确定要删除所有以 "${clearPrefix.value}" 开头的键吗?`)) return
  try {
    const r: any = await opsApi.clearKeys(clearPrefix.value)
    ElMessage.error(`已删除 ${r?.deleted ?? 0} 个键`)
    await loadKeys()
  } catch (e: any) { ElMessage.error('删除失败: ' + (e?.message || '')) }
}

function formatTtl(sec: number | null) {
  if (sec == null || sec < 0) return '永不过期'
  if (sec < 60) return Math.round(sec) + 's'
  if (sec < 3600) return Math.floor(sec / 60) + 'm'
  if (sec < 86400) return Math.floor(sec / 3600) + 'h'
  return Math.floor(sec / 86400) + 'd'
}

onMounted(refresh)
</script>

<template>
  <div class="row g-3">
    <div class="col-12">
      <h3 class="page-title mb-3">缓存管理</h3>
    </div>

    <!-- Garnet/Redis 概览 -->
    <div class="col-md-4">
      <TCard title="缓存服务">
        <div v-if="info">
          <div class="d-flex align-items-center mb-3">
            <span class="badge me-2" :class="isGarnet() ? 'bg-info-lt' : 'bg-success-lt'">
              {{ isGarnet() ? 'Garnet(内嵌)' : 'Redis' }}
            </span>
            <span v-if="info.server?.redis_version" class="text-muted small">v{{ info.server.redis_version }}</span>
          </div>
          <table class="table table-sm m-0">
            <tbody>
              <tr><td class="text-muted" style="width: 100px">连接延迟</td><td>{{ info.pingMs >= 0 ? info.pingMs?.toFixed(2) + ' ms' : '不可用' }}</td></tr>
              <tr><td class="text-muted">键总数</td><td>{{ info.dbSize ?? 0 }}</td></tr>
              <tr v-if="info.server?.redis_mode"><td class="text-muted">模式</td><td>{{ info.server.redis_mode }}</td></tr>
              <tr v-if="info.server?.uptime_in_days != null"><td class="text-muted">已运行</td><td>{{ info.server.uptime_in_days }} 天</td></tr>
              <tr v-if="info.server?.os"><td class="text-muted">系统</td><td>{{ info.server.os }}</td></tr>
            </tbody>
          </table>
        </div>
        <div v-else class="text-muted small py-2">{{ infoErr || '加载中...' }}</div>
      </TCard>
    </div>

    <!-- 批量清除 -->
    <div class="col-md-4">
      <TCard title="批量清除">
        <div class="mb-2">
            <TInput v-model="clearPrefix" placeholder="如: rapiddev:" />
        </div>
        <div class="text-muted small mb-3">按前缀匹配删除缓存键,谨慎操作。</div>
        <TButton variant="danger" class="w-100" @click="clearKeys">
          <i class="ti ti-trash me-1"></i>按前缀删除
        </TButton>
      </TCard>
    </div>

    <!-- 操作提示 -->
    <div class="col-md-4">
      <TCard title="使用说明">
        <ul class="small text-muted mb-0 ps-3">
          <li>本系统使用 Garnet(Redis 兼容)作为缓存,部署在内嵌进程内。</li>
          <li>键列表通过 SCAN 扫描,仅展示前 N 个,可按名称筛选。</li>
          <li>批量清除仅删除指定前缀的键,不影响其他业务。</li>
          <li>会话、Nonce、密钥池等关键键已加 <code>rapiddev:</code> 前缀。</li>
        </ul>
      </TCard>
    </div>

    <!-- 键列表 -->
    <div class="col-12">
      <TCard>
        <template #title>
          <span><i class="ti ti-list me-2"></i>键列表</span>
        </template>
        <template #actions>
          <div class="d-flex gap-2 flex-wrap">
            <TInput v-model="keyPattern" placeholder="搜索键名" style="width: 200px" @keyup.enter="loadKeys" />
            <select v-model="keyCount" class="form-select form-select-sm" style="width: 90px" @change="loadKeys">
              <option :value="10">10</option>
              <option :value="50">50</option>
              <option :value="100">100</option>
            </select>
            <TButton variant="outline-secondary" size="sm" @click="loadKeys" :disabled="loading">
              <i class="ti ti-search"></i>
            </TButton>
            <TButton variant="outline-secondary" size="sm" @click="refresh" :disabled="loading">
              <i class="ti ti-refresh"></i>
            </TButton>
          </div>
        </template>
        <div class="table-responsive">
          <table class="table table-sm table-vcenter card-table">
            <thead><tr><th>键</th><th style="width: 120px">类型</th><th style="width: 110px">TTL</th><th style="width: 80px">操作</th></tr></thead>
            <tbody>
              <tr v-if="loading"><td colspan="4" class="text-center text-muted py-3">加载中...</td></tr>
              <tr v-else-if="!keys.length"><td colspan="4" class="text-center text-muted py-3">暂无键</td></tr>
              <tr v-for="k in keys" :key="k.key">
                <td class="text-truncate" style="max-width: 360px;" :title="k.key"><code class="small">{{ k.key }}</code></td>
                <td><span class="badge bg-secondary-lt">{{ k.type }}</span></td>
                <td class="small">{{ formatTtl(k.ttl) }}</td>
                <td><button class="btn btn-sm btn-ghost-secondary" @click="viewKey(k.key)"><i class="ti ti-eye"></i></button></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="card-footer text-muted small">共 {{ keys.length }} 个键(最多展示 {{ keyCount }})</div>
      </TCard>
    </div>
  </div>

  <!-- 键详情弹窗 -->
  <TModal v-model="showDetail" title="键详情">
    <div v-if="detailLoading" class="text-center py-3 text-muted">加载中...</div>
    <div v-else-if="keyDetailData?.error" class="text-danger">{{ keyDetailData.error }}</div>
    <div v-else-if="keyDetailData">
      <dl class="row mb-0">
        <dt class="col-3 text-muted">键</dt><dd class="col-9"><code style="word-break: break-all;">{{ keyDetailData.key }}</code></dd>
        <dt class="col-3 text-muted">类型</dt><dd class="col-9">{{ keyDetailData.type }}</dd>
        <dt class="col-3 text-muted">TTL</dt><dd class="col-9">{{ formatTtl(keyDetailData.ttlSeconds) }}</dd>
        <dt v-if="keyDetailData.value" class="col-3 text-muted">值</dt>
        <dd v-if="keyDetailData.value" class="col-9"><code class="small" style="word-break: break-all; white-space: pre-wrap;">{{ keyDetailData.value }}</code></dd>
      </dl>
    </div>
    <template #footer>
      <TButton variant="ghost-secondary" @click="showDetail = false">关闭</TButton>
    </template>
  </TModal>
</template>
