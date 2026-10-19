<script setup lang="ts">
/**
 * IP 归属地查询工具 -- 聚合 ip2region/纯真本地库 + Bilibili 在线兜底。
 * 单 IP 查询展示全数据源对比(并行查各源,互不短路);批量查询走短路优先级。
 * 开"含在线库"时才走 Bilibili(限并发)。
 */
import { ref, reactive } from 'vue'
import { TCard, TButton, TInput } from '@/components/t'
import { ipApi } from '@/api/system'

const single = reactive({ ip: '', includeOnline: false, loading: false, list: [] as any[] })
async function locateSingle() {
  if (!single.ip.trim()) return
  single.loading = true
  try {
    single.list = await ipApi.locateAll(single.ip.trim(), single.includeOnline)
  } catch (e: any) {
    single.list = [{ error: e?.message || '查询失败', source: 'none' }]
  } finally { single.loading = false }
}
async function locateMyIp() {
  single.loading = true
  single.ip = ''
  try {
    single.list = await ipApi.myIp(single.includeOnline)
    if (single.list.length) single.ip = single.list[0].ip || ''
  } catch (e: any) {
    single.list = [{ error: e?.message || '查询失败', source: 'none' }]
  } finally { single.loading = false }
}

const batch = reactive({ text: '', includeOnline: false, loading: false, list: [] as any[] })
async function locateBatch() {
  const ips = batch.text.split(/[\s,;\n]+/).map(s => s.trim()).filter(Boolean)
  if (!ips.length) return
  batch.loading = true
  try {
    const map: any = await ipApi.locateBatch(ips, batch.includeOnline)
    batch.list = ips.map(ip => ({ ip, ...(map[ip] || { ip, source: 'none' }) }))
  } finally { batch.loading = false }
}

function sourceBadge(s?: string) {
  return { ip2region: 'bg-success-lt', qqwry: 'bg-info-lt', bilibili: 'bg-warning-lt', lan: 'bg-secondary-lt', none: 'bg-danger-lt' }[s || ''] || 'bg-secondary-lt'
}
function sourceText(s?: string) {
  return { ip2region: 'ip2region', qqwry: '纯真', bilibili: 'Bilibili', lan: '本地/内网', none: '未命中' }[s || ''] || s || '-'
}
</script>

<template>
  <TCard>
    <template #title><i class="ti ti-world me-2"></i>IP 归属地查询</template>
    <template #actions>
      <div class="d-flex align-items-center" style="height: 100%">
        <label class="form-check form-switch m-0">
          <input class="form-check-input" type="checkbox" v-model="single.includeOnline" @change="batch.includeOnline = single.includeOnline" />
          <span class="form-check-label small ms-2">含在线库(Bilibili 兜底)</span>
        </label>
      </div>
    </template>

    <div class="row g-3">
      <div class="col-md-6">
        <h3 class="card-title mb-3">单 IP 查询(多源对比)</h3>
        <div class="input-group">
          <TInput v-model="single.ip" placeholder="输入 IP,如 8.8.8.8" @keyup.enter="locateSingle" />
          <TButton variant="primary" :loading="single.loading" @click="locateSingle"><i class="ti ti-search me-1"></i>查询</TButton>
          <TButton variant="outline-secondary" :loading="single.loading" @click="locateMyIp"><i class="ti ti-current-location me-1"></i>本机 IP</TButton>
        </div>
        <div v-if="single.list.length" class="mt-3">
          <div v-for="(r, i) in single.list" :key="i" class="mb-2">
            <div v-if="r.error" class="alert alert-warning mb-0 py-2">{{ r.error }}</div>
            <div v-else class="border rounded p-2">
              <div class="d-flex align-items-center justify-content-between mb-1">
                <span class="badge" :class="sourceBadge(r.source)">{{ sourceText(r.source) }}</span>
                <span class="small text-muted">{{ r.ip }}</span>
              </div>
              <table class="table table-sm table-borderless mb-0">
                <tbody>
                  <tr><td class="text-muted" style="width:80px">国家</td><td>{{ r.country || '-' }}</td></tr>
                  <tr><td class="text-muted">省份</td><td>{{ r.province || '-' }}</td></tr>
                  <tr><td class="text-muted">城市</td><td>{{ r.city || '-' }}</td></tr>
                  <tr v-if="r.area"><td class="text-muted">地区</td><td>{{ r.area }}</td></tr>
                  <tr><td class="text-muted">运营商</td><td>{{ r.isp || '-' }}</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <div class="col-md-6">
        <h3 class="card-title mb-3">批量查询(短路优先级)</h3>
        <textarea class="form-control mb-2" v-model="batch.text" rows="5" placeholder="每行一个 IP,或用逗号/空格/分号分隔"></textarea>
        <TButton variant="outline-primary" :loading="batch.loading" @click="locateBatch"><i class="ti ti-search me-1"></i>批量查询</TButton>
        <div v-if="batch.list.length" class="table-responsive mt-3">
          <table class="table table-sm">
            <thead><tr><th>IP</th><th>国家</th><th>省</th><th>市</th><th>运营商</th><th>数据源</th></tr></thead>
            <tbody>
              <tr v-for="r in batch.list" :key="r.ip">
                <td>{{ r.ip }}</td>
                <td>{{ r.country || '-' }}</td>
                <td>{{ r.province || '-' }}</td>
                <td>{{ r.city || '-' }}</td>
                <td>{{ r.isp || '-' }}</td>
                <td><span class="badge" :class="sourceBadge(r.source)">{{ sourceText(r.source) }}</span></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </TCard>
</template>
