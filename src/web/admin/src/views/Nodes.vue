<template>
  <el-card>
    <template #header>
      <div class="card-head">
        <span>边缘节点</span>
        <el-button type="primary" @click="openCreate"><el-icon><Plus /></el-icon>添加节点</el-button>
      </div>
    </template>
    <el-table :data="nodes" v-loading="loading">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="name" label="名称" />
      <el-table-column prop="hostname" label="主机名" />
      <el-table-column label="IP">
        <template #default="{ row }">{{ (row.publicIps || []).join(', ') || '-' }}</template>
      </el-table-column>
      <el-table-column prop="version" label="版本" width="90" />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.isOnline ? 'success' : 'danger'" size="small">{{ row.isOnline ? '在线' : '离线' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="最后心跳" width="170">
        <template #default="{ row }">{{ fmtTime(row.lastHeartbeat) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button v-if="row.installToken" size="small" type="warning" @click="showInstall(row)">安装令牌</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="createDialog" title="添加节点" width="480px">
      <el-form label-width="90px">
        <el-form-item label="节点名称"><el-input v-model="newNode.name" placeholder="node-beijing-1" /></el-form-item>
        <el-form-item label="集群"><el-input-number v-model="newNode.clusterId" :min="1" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialog = false">取消</el-button>
        <el-button type="primary" @click="create">创建并获取安装令牌</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="installDialog" title="节点安装" width="680px">
      <el-alert type="info" :closable="false" style="margin-bottom:12px"
        title="在目标服务器上执行以下命令完成节点安装（自动注册，installToken 一次性）" />
      <el-input type="textarea" :rows="8" readonly :model-value="installScript" />
      <template #footer>
        <el-button @click="copyScript">复制命令</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import api from '../api'

const nodes = ref([])
const loading = ref(false)
const createDialog = ref(false)
const installDialog = ref(false)
const newNode = ref({ name: '', clusterId: 1 })
const installScript = ref('')

const fmtTime = (t) => t ? new Date(t).toLocaleString() : '-'

async function load() {
  loading.value = true
  try { nodes.value = await api.get('/admin/nodes') } finally { loading.value = false }
}
onMounted(load)

function openCreate() { newNode.value = { name: '', clusterId: 1 }; createDialog.value = true }

async function create() {
  if (!newNode.value.name) return ElMessage.warning('请填写名称')
  const res = await api.post('/admin/nodes', newNode.value)
  createDialog.value = false
  installScript.value = `# 1. 下载节点程序（以服务器上的发布产物为准）
# 2. 写入配置
mkdir -p /opt/tnorv/configs
cat > /opt/tnorv/configs/tnorv-node.json <<'EOF'
{
  "masterUrl": "${location.origin}",
  "installToken": "${res.installToken}",
  "cacheDir": "/opt/tnorv/cache",
  "memoryCacheMB": 128,
  "logBufferMax": 10000,
  "adminHttpAddr": "127.0.0.1:8081"
}
EOF
# 3. 启动（需 root，监听 80/443）
cd /opt/tnorv && nohup ./tnorv-node -c configs/tnorv-node.json > /var/log/tnorv-node.log 2>&1 &`
  installDialog.value = true
  load()
}

function copyScript() {
  navigator.clipboard.writeText(installScript.value)
  ElMessage.success('已复制')
}
</script>

<style scoped>
.card-head { display: flex; justify-content: space-between; align-items: center; }
</style>
