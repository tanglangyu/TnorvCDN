<script setup lang="ts">
/** IP 名单 —— 站点级黑白名单（CIDR，支持备注 #） */
import { ref, watch, computed } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const white = ref('')
const black = ref('')
const parse = (v: string) => v.split(/\n|,/).map((x) => x.trim()).filter(Boolean).map((line) => {
  const m = line.match(/^(\S+)\s*#\s*(.*)$/)
  return m ? { cidr: m[1], note: m[2] } : { cidr: line, note: '' }
})
const render = (list: any[]) => (list || []).map((r) => (r.note ? r.cidr + ' # ' + r.note : r.cidr)).join('\n')
watch(() => props.server, (s) => {
  if (!s) return
  white.value = render(s.security?.ipWhitelist)
  black.value = render(s.security?.ipBlacklist)
}, { immediate: true })

const saving = ref(false)
const save = async () => {
  const sec = JSON.parse(JSON.stringify(props.server.security || {}))
  sec.ipWhitelist = parse(white.value)
  sec.ipBlacklist = parse(black.value)
  saving.value = true
  try {
    await cdnApi.put('/admin/servers/' + props.serverId + '/security', sec)
    ElMessage.success('IP 名单已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">IP 名单</h5>
    <el-form label-width="120px">
      <el-form-item label="白名单">
        <el-input v-model="white" type="textarea" :rows="6" placeholder="每行一个 IP/CIDR，支持备注：1.2.3.0/24 # 办公网" style="max-width: 560px" />
        <div class="text-muted small w-100">白名单命中直接放行（跳过全部防护）</div>
      </el-form-item>
      <el-form-item label="黑名单">
        <el-input v-model="black" type="textarea" :rows="6" placeholder="每行一个 IP/CIDR" style="max-width: 560px" />
      </el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
