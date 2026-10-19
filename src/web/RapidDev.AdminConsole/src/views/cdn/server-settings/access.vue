<script setup lang="ts">
/** 访问鉴权 —— Basic Auth 用户 + URL 时间戳签名（A-D 方案） */
import { ref, watch } from 'vue'
import { cdnApi } from '@/api/cdn'
import { ElMessage } from 'element-plus'

const props = defineProps<{ serverId: number; server: any }>()
const emit = defineEmits<{ (e: 'saved'): void }>()

const basic = ref<any>({ enabled: false, users: [] })
const urlAuth = ref<any>({ enabled: false, type: 'a', key: '', expirySeconds: 1800 })
watch(() => props.server, (s) => {
  if (!s) return
  const adv = s.advanced || {}
  basic.value = { enabled: !!adv.basicAuth?.enabled, users: JSON.parse(JSON.stringify(adv.basicAuth?.users || [])) }
  basic.value.users = basic.value.users.map((u: any) => ({ username: u.username, plain: '' }))
  const u = JSON.parse(JSON.stringify(s.security?.urlAuth || {}))
  urlAuth.value = { enabled: !!u.enabled, type: u.type || 'a', key: u.key || '', expirySeconds: u.expirySeconds || 1800 }
}, { immediate: true })

const addUser = () => basic.value.users.push({ username: '', plain: '' })
const rmUser = (i: number) => basic.value.users.splice(i, 1)

const saving = ref(false)
const save = async () => {
  saving.value = true
  try {
    const adv = JSON.parse(JSON.stringify(props.server.advanced || {}))
    adv.basicAuth = { enabled: basic.value.enabled, users: basic.value.users.filter((u: any) => u.username && u.plain) }
    await cdnApi.put('/admin/servers/' + props.serverId + '/advanced', adv)
    const sec = JSON.parse(JSON.stringify(props.server.security || {}))
    sec.urlAuth = urlAuth.value
    await cdnApi.put('/admin/servers/' + props.serverId + '/security', sec)
    ElMessage.success('访问鉴权配置已保存并下发')
    emit('saved')
  } catch (e: any) { ElMessage.error(e?.response?.data || e?.message || '保存失败') }
  finally { saving.value = false }
}
</script>

<template>
  <div>
    <h5 class="mb-3">访问鉴权</h5>
    <div class="font-weight-medium mb-2">Basic Auth（输入用户名密码访问）</div>
    <el-form label-width="120px" style="max-width: 640px">
      <el-form-item label="启用"><el-switch v-model="basic.enabled" /></el-form-item>
    </el-form>
    <el-table :data="basic.users" size="small" stripe class="mb-2" style="max-width: 640px">
      <el-table-column label="用户名" min-width="160"><template #default="{ row }"><el-input v-model="row.username" size="small" /></template></el-table-column>
      <el-table-column label="密码" min-width="160"><template #default="{ row }"><el-input v-model="row.plain" size="small" type="password" show-password placeholder="留空保持原密码" /></template></el-table-column>
      <el-table-column label="操作" width="80"><template #default="{ $index }"><el-button size="small" type="danger" text @click="rmUser($index)">删除</el-button></template></el-table-column>
    </el-table>
    <el-button size="small" class="mb-3" @click="addUser">+ 添加用户</el-button>

    <div class="font-weight-medium mb-2 mt-4">URL 防盗链（时间戳签名）</div>
    <el-form label-width="120px" style="max-width: 640px">
      <el-form-item label="启用"><el-switch v-model="urlAuth.enabled" /></el-form-item>
      <el-form-item label="签名方案">
        <el-select v-model="urlAuth.type" style="width: 200px">
          <el-option value="a" label="A 方案（md5(ts+key+uri)）" />
          <el-option value="b" label="B 方案" />
          <el-option value="c" label="C 方案" />
          <el-option value="d" label="D 方案" />
        </el-select>
      </el-form-item>
      <el-form-item label="签名密钥"><el-input v-model="urlAuth.key" style="width: 320px" /></el-form-item>
      <el-form-item label="有效期（秒）"><el-input-number v-model="urlAuth.expirySeconds" :min="60" :max="86400" /></el-form-item>
    </el-form>
    <el-button type="primary" :loading="saving" @click="save">保存</el-button>
  </div>
</template>
