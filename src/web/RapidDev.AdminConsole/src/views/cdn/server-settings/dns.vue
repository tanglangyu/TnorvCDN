<script setup lang="ts">
/** DNS —— CNAME 接入指引 + 实时解析检测 */
import { ref, onMounted } from 'vue'
import { cdnApi } from '@/api/cdn'

const props = defineProps<{ serverId: number; server: any }>()
const checking = ref(false)
const result = ref<any>(null)

const check = async () => {
  checking.value = true
  try { result.value = await cdnApi.get('/admin/servers/' + props.serverId + '/dns-check') }
  finally { checking.value = false }
}
onMounted(check)
</script>

<template>
  <div>
    <h5 class="mb-3">DNS 解析</h5>
    <el-alert v-if="result?.ok" :type="result.pointsToCluster ? 'success' : 'warning'" :closable="false" class="mb-3"
      :title="result.pointsToCluster ? '域名已正确解析到集群' : '域名尚未指向集群入口'" />
    <el-descriptions v-if="result?.ok" :column="1" border class="mb-3" style="max-width: 720px">
      <el-descriptions-item label="站点域名">{{ result.domain }}</el-descriptions-item>
      <el-descriptions-item label="集群入口">{{ result.clusterDns || '（集群未配置入口域名）' }}</el-descriptions-item>
      <el-descriptions-item label="当前解析记录">
        <el-tag v-for="(r, i) in result.records" :key="i" size="small" class="me-1">{{ r.type }} {{ r.value }}</el-tag>
        <span v-if="!result.records.length" class="text-muted">无记录</span>
      </el-descriptions-item>
    </el-descriptions>
    <el-alert v-else-if="result" type="danger" :closable="false" class="mb-3" :title="result.error || '检测失败'" />
    <div class="text-muted small mb-3">
      请在域名服务商处添加 CNAME 记录指向集群入口域名；解析生效后系统自动签发 HTTPS 证书。
    </div>
    <el-button type="primary" :loading="checking" @click="check">重新检测</el-button>
  </div>
</template>
