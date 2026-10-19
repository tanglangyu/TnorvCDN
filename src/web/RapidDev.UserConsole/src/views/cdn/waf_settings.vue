<script setup lang="ts">
/** 我的防护（U4 防护矩阵）—— 本人各站点 CC/5秒盾/防盗链/IP黑名单 状态矩阵 + 行内快捷开关 */
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
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

const toggling = ref('')

const loadData = async () => {
  const r = (await cdnApi.get('/user/waf/matrix')) as any
  rows.value = r?.data ?? r ?? []
  return rows.value
}

// 行内快捷开关：读改写整个 Security（PUT 整体替换语义，只改目标字段）
async function toggle(row: any, field: 'cc' | 'jsShield' | 'referer') {
  toggling.value = row.serverId + field
  try {
    const sr = (await cdnApi.get(`/user/servers/${row.serverId}/settings`)) as any
    const sec = JSON.parse(JSON.stringify((sr?.data ?? sr ?? {}).security || {}))
    if (field === 'cc') sec.cc = { ...(sec.cc || {}), enabled: !row.ccEnabled }
    else if (field === 'jsShield') sec.jsShield = { ...(sec.jsShield || {}), enabled: !row.jsShieldEnabled }
    else sec.referers = { ...(sec.referers || {}), enabled: !row.refererEnabled }
    await cdnApi.put(`/user/servers/${row.serverId}/security`, sec)
    ElMessage.success('已保存并下发到节点')
    gate.value?.refresh?.()
  } catch (e: any) {
    ElMessage.error(e?.response?.data?.message || e?.response?.data || '操作失败')
  } finally { toggling.value = '' }
}

const goConfig = (row: any) => window.open(`/servers/${row.serverId}/security`, '_self')
</script>

<template>
  <div>
    <div class="page-header mb-4"><h2 class="page-title">我的防护</h2><div class="text-muted">WAF安全 · 各站点防护状态矩阵，行内快捷开关实时下发</div></div>
    <CdnGate ref="gate" :load-data="loadData">
      <template #default="{ refresh }">
        <el-card>
          <div class="mb-3 d-flex gap-2">
            <el-button size="small" @click="refresh()">刷新</el-button>
            <span class="text-muted small align-self-center">点击开关直接修改站点安全配置并推送节点；详细规则请进入「站点配置 → 安全防护」</span>
          </div>
          <el-input v-model="filterQ" placeholder="关键词过滤（全字段）" size="small" clearable style="width: 240px" class="me-2" /><el-table :data="viewRows" stripe>
            <el-table-column prop="name" label="站点" min-width="200" show-overflow-tooltip />
            <el-table-column label="CC防护" width="140" align="center">
              <template #default="{ row }">
                <el-switch :model-value="row.ccEnabled" size="small" :loading="toggling === row.serverId + 'cc'" @change="toggle(row, 'cc')" />
                <div class="text-muted" style="font-size: 11px" v-if="row.ccEnabled">{{ row.ccThreshold }}/分</div>
              </template>
            </el-table-column>
            <el-table-column label="5秒盾" width="120" align="center">
              <template #default="{ row }">
                <el-switch :model-value="row.jsShieldEnabled" size="small" :loading="toggling === row.serverId + 'jsShield'" @change="toggle(row, 'jsShield')" />
              </template>
            </el-table-column>
            <el-table-column label="防盗链" width="120" align="center">
              <template #default="{ row }">
                <el-switch :model-value="row.refererEnabled" size="small" :loading="toggling === row.serverId + 'referer'" @change="toggle(row, 'referer')" />
              </template>
            </el-table-column>
            <el-table-column label="IP黑名单" width="100" align="center">
              <template #default="{ row }">
                <el-tag size="small" :type="row.ipBlacklist > 0 ? 'danger' : 'info'">{{ row.ipBlacklist }} 条</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="UA规则" width="100" align="center">
              <template #default="{ row }">
                <el-tag size="small" :type="row.uaRules > 0 ? 'warning' : 'info'">{{ row.uaRules }} 条</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="100" fixed="right">
              <template #default="{ row }">
                <el-button size="small" text type="primary" @click="goConfig(row)">详细配置</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-pagination class="mt-3" background layout="total, sizes, prev, pager, next"
            :total="pager.total" :page-size="pager.pageSize" :current-page="pager.page" :page-sizes="[20, 50, 100]"
            @current-change="(p: number) => { pager.page = p }"
            @size-change="(s2: number) => { pager.pageSize = s2; pager.page = 1 }" />
          <el-empty v-if="!viewRows.length" description="暂无数据" />
          <el-empty v-if="!rows.length" description="暂无启用中的站点" />
        </el-card>
      </template>
    </CdnGate>
  </div>
</template>
