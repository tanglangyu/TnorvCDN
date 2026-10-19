<script setup lang="ts">
/** 源站 —— 主/备源站、调度算法、回源 Host、按路径分源站 */
import { ref, watch } from 'vue'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'save', patch: any): void }>()

const form = ref<any>({})
watch(() => props.server, (s) => {
  if (!s) return
  const o = JSON.parse(JSON.stringify(s.origin || {}))
  o.primaries = o.primaries || []
  o.backups = o.backups || []
  o.pathRules = o.pathRules || []
  o.scheduleAlgorithm = o.scheduleAlgorithm || 'round-robin'
  form.value = o
}, { immediate: true })

const addPrim = () => form.value.primaries.push({ scheme: 'http', host: '', port: 80, weight: 1, enabled: true })
const addBackup = () => form.value.backups.push({ scheme: 'http', host: '', port: 80, weight: 1, enabled: true })
const addPathRule = () => form.value.pathRules.push({ pathPrefix: '/api/', primaries: [{ scheme: 'http', host: '', port: 80, weight: 1, enabled: true }], backups: [], hostOverride: '' })
const rm = (arr: any[], i: number) => arr.splice(i, 1)

const saving = ref(false)
const save = async () => {
  const valid = form.value.primaries.filter((o: any) => o.host)
  if (!valid.length) return
  form.value.primaries = valid
  form.value.backups = form.value.backups.filter((o: any) => o.host)
  form.value.pathRules = form.value.pathRules.filter((r: any) => r.pathPrefix && (r.primaries || []).some((o: any) => o.host))
  saving.value = true
  emit('save', { origin: form.value })
  saving.value = false
}
</script>

<template>
  <div>
    <h5 class="mb-3">源站配置</h5>
    <el-form label-width="120px" style="max-width: 900px">
      <el-form-item label="调度算法">
        <el-select v-model="form.scheduleAlgorithm" style="width: 200px">
          <el-option value="round-robin" label="轮询" />
          <el-option value="random" label="随机" />
          <el-option value="ip-hash" label="IP 哈希" />
        </el-select>
      </el-form-item>
      <el-form-item label="回源 Host">
        <el-input v-model="form.hostOverride" placeholder="空=跟随访问域名" style="width: 320px" />
      </el-form-item>
    </el-form>

    <div class="mb-2 font-weight-medium">主源站</div>
    <el-table :data="form.primaries" size="small" stripe class="mb-2" style="max-width: 900px">
      <el-table-column label="协议" width="100">
        <template #default="{ row }"><el-select v-model="row.scheme" size="small"><el-option value="http" /><el-option value="https" /><el-option value="s3" /></el-select></template>
      </el-table-column>
      <el-table-column label="地址" min-width="180"><template #default="{ row }"><el-input v-model="row.host" size="small" placeholder="IP / 域名 / endpoint" /></template></el-table-column>
      <el-table-column label="端口" width="100"><template #default="{ row }"><el-input-number v-model="row.port" size="small" :min="1" :max="65535" controls-position="right" /></template></el-table-column>
      <el-table-column label="权重" width="100"><template #default="{ row }"><el-input-number v-model="row.weight" size="small" :min="1" :max="100" controls-position="right" /></template></el-table-column>
      <el-table-column label="操作" width="80"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rm(form.primaries, $index)">删除</el-button></template></el-table-column>
    </el-table>
    <el-button size="small" @click="addPrim">+ 添加主源站</el-button>

    <div class="mb-2 mt-4 font-weight-medium">备用源站（主源站故障时切换）</div>
    <el-table :data="form.backups" size="small" stripe class="mb-2" style="max-width: 900px">
      <el-table-column label="协议" width="100"><template #default="{ row }"><el-select v-model="row.scheme" size="small"><el-option value="http" /><el-option value="https" /></el-select></template></el-table-column>
      <el-table-column label="地址" min-width="180"><template #default="{ row }"><el-input v-model="row.host" size="small" /></template></el-table-column>
      <el-table-column label="端口" width="100"><template #default="{ row }"><el-input-number v-model="row.port" size="small" :min="1" :max="65535" controls-position="right" /></template></el-table-column>
      <el-table-column label="操作" width="80"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rm(form.backups, $index)">删除</el-button></template></el-table-column>
    </el-table>
    <el-button size="small" @click="addBackup">+ 添加备用源站</el-button>

    <div class="mb-2 mt-4 font-weight-medium">路由规则（按路径分源站，先匹配优先）</div>
    <el-table :data="form.pathRules" size="small" stripe class="mb-2" style="max-width: 900px">
      <el-table-column label="路径前缀" width="160"><template #default="{ row }"><el-input v-model="row.pathPrefix" size="small" placeholder="/api/" /></template></el-table-column>
      <el-table-column label="源站地址" min-width="200">
        <template #default="{ row }"><el-input v-model="row.primaries[0].host" size="small" placeholder="host" /></template>
      </el-table-column>
      <el-table-column label="端口" width="100"><template #default="{ row }"><el-input-number v-model="row.primaries[0].port" size="small" :min="1" :max="65535" controls-position="right" /></template></el-table-column>
      <el-table-column label="操作" width="80"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rm(form.pathRules, $index)">删除</el-button></template></el-table-column>
    </el-table>
    <el-button size="small" @click="addPathRule">+ 添加路由规则</el-button>

    <div class="mt-4"><el-button type="primary" :loading="saving" @click="save">保存</el-button></div>
  </div>
</template>
