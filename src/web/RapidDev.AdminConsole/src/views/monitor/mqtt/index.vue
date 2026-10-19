<script setup lang="ts">
/**
 * MQTT 监控 -- 进程内 MQTTnet broker。
 * 状态总览(运行/客户端/消息吞吐/留存)+ 4 个 Tab:在线客户端(含详情展开/强制断开)、留存消息(删/清空)、主题订阅统计、消息流(实时+测试发布)。
 */
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { TCard, TButton, TInput } from '@/components/t'
import { mqttApi, type MqttBrokerStatus, type MqttClientInfo, type MqttRecentMessage, type MqttRetainedMessageView, type MqttTopicStat } from '@/api/monitor'
import { formatTime } from '@/utils/time'

const status = ref<MqttBrokerStatus | null>(null)
const loading = ref(false)
const autoRefresh = ref(true)
let timer: any = null
const activeTab = ref<'clients' | 'retained' | 'topics' | 'stream'>('clients')

async function loadStatus() { try { status.value = await mqttApi.status() } catch {} }

// ===== 客户端 =====
const clients = ref<MqttClientInfo[]>([])
const clientTotal = ref(0)
const clientKeyword = ref('')
const expandedClient = ref<string | null>(null)

async function loadClients() {
  try {
    const res: any = await mqttApi.clients({ keyword: clientKeyword.value, page: 1, size: 100 })
    clients.value = res.list || []
    clientTotal.value = res.total || 0
  } catch {}
}
async function kick(row: MqttClientInfo) {
  if (!confirm(`确定强制断开客户端「${row.clientId}」(${row.username || '-'})?`)) return
  try { await mqttApi.kick(row.clientId); loadClients() } catch {}
}
function toggleExpand(clientId: string) { expandedClient.value = expandedClient.value === clientId ? null : clientId }

// ===== 留存消息 =====
const retained = ref<MqttRetainedMessageView[]>([])
async function loadRetained() { try { retained.value = (await mqttApi.retained()) as MqttRetainedMessageView[] } catch {} }
async function deleteRetained(topic: string) {
  if (!confirm(`删除留存消息「${topic}」?`)) return
  try { await mqttApi.deleteRetained(topic); loadRetained(); loadStatus() } catch {}
}
async function clearRetained() {
  if (!confirm('清空全部留存消息?此操作不可恢复!')) return
  try { await mqttApi.clearRetained(); loadRetained(); loadStatus() } catch {}
}

// ===== 主题订阅 =====
const topics = ref<MqttTopicStat[]>([])
async function loadTopics() { try { topics.value = (await mqttApi.topics()) as MqttTopicStat[] } catch {} }

// ===== 消息流 + 测试发布 =====
const recent = ref<MqttRecentMessage[]>([])
const streamFilter = ref('')
const pubTopic = ref('test/a')
const pubPayload = ref('hello')
const pubRetained = ref(false)
const pubQos = ref(0)
const pubLoading = ref(false)
async function loadRecent() {
  try { recent.value = (await mqttApi.testRecent(streamFilter.value || undefined)) as MqttRecentMessage[] } catch {}
}
async function doPublish() {
  if (!pubTopic.value) return
  pubLoading.value = true
  try { await mqttApi.testPublish({ topic: pubTopic.value, payload: pubPayload.value, retained: pubRetained.value, qos: pubQos.value }) } finally { pubLoading.value = false }
}

// ===== 重启 Broker =====
const restartLoading = ref(false)
async function restartBroker() {
  if (!confirm('重启 MQTT broker?将断开所有在线客户端。')) return
  restartLoading.value = true
  try { await mqttApi.restart(); await loadAll() } finally { restartLoading.value = false }
}

// ===== 刷新调度 =====
async function loadActiveTab() {
  if (activeTab.value === 'clients') await loadClients()
  else if (activeTab.value === 'retained') await loadRetained()
  else if (activeTab.value === 'topics') await loadTopics()
  else await loadRecent()
}
async function loadAll() { await Promise.all([loadStatus(), loadActiveTab()]) }
function startTimer() { stopTimer(); if (autoRefresh.value) timer = setInterval(loadAll, 3000) }
function stopTimer() { if (timer) { clearInterval(timer); timer = null } }
function toggleAuto() { autoRefresh.value = !autoRefresh.value; startTimer() }
function switchTab(t: typeof activeTab.value) { activeTab.value = t; loadActiveTab() }

// ===== 格式化 =====
function fmtBytes(n: number): string {
  if (n < 1024) return n + ' B'
  if (n < 1048576) return (n / 1024).toFixed(1) + ' KB'
  return (n / 1048576).toFixed(1) + ' MB'
}
function fmtUptime(sec: number): string {
  if (sec < 60) return `${sec}s`
  if (sec < 3600) return `${Math.floor(sec / 60)}m ${sec % 60}s`
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60)
  return `${h}h ${m}m`
}
function qosText(q: number) { return q === 0 ? 'QoS0' : q === 1 ? 'QoS1' : q === 2 ? 'QoS2' : `QoS${q}` }
function qosClass(q: number) { return q === 0 ? 'bg-secondary-lt' : q === 1 ? 'bg-info-lt' : 'bg-warning-lt' }

const testPanelEnabled = computed(() => status.value?.enabled !== false)
const clientCount = computed(() => status.value?.clientCount ?? 0)

onMounted(() => { loadAll(); startTimer() })
onUnmounted(stopTimer)
</script>

<template>
  <TCard title="MQTT 监控">
    <template #actions>
      <TButton size="sm" variant="ghost-warning" :loading="restartLoading" @click="restartBroker"><i class="ti ti-refresh me-1"></i>重启 Broker</TButton>
      <TButton size="sm" variant="ghost-secondary" :loading="loading" @click="loadAll"><i class="ti ti-refresh"></i></TButton>
      <TButton size="sm" :variant="autoRefresh ? 'ghost-success' : 'ghost-secondary'" @click="toggleAuto">
        <i class="ti" :class="autoRefresh ? 'ti-player-pause' : 'ti-player-play'"></i>{{ autoRefresh ? '暂停' : '自动' }}
      </TButton>
    </template>

    <!-- 状态总览:8 卡片 -->
    <div class="row g-2 mb-3">
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">运行状态</div>
          <div class="mt-1">
            <span v-if="status?.isRunning" class="badge bg-success-lt"><i class="ti ti-point-filled me-1"></i>运行中</span>
            <span v-else-if="status && !status.enabled" class="badge bg-secondary-lt">已禁用</span>
            <span v-else class="badge bg-danger-lt">未运行</span>
          </div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">在线客户端</div>
          <div class="mt-1 fs-4 fw-bold">{{ clientCount }}</div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">累计消息</div>
          <div class="mt-1 fs-4 fw-bold">{{ status?.totalMessagesPublished ?? 0 }}</div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">消息速率</div>
          <div class="mt-1 fs-4 fw-bold">{{ status?.messagesPerMinute ?? 0 }} <span class="text-muted small">/分</span></div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">累计流量</div>
          <div class="mt-1 fs-5 fw-bold">{{ fmtBytes(status?.totalBytesPublished ?? 0) }}</div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">留存消息</div>
          <div class="mt-1 fs-4 fw-bold">{{ status?.retainedMessageCount ?? 0 }}</div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">累计连接</div>
          <div class="mt-1 fs-5 fw-bold">{{ status?.totalConnectCount ?? 0 }}</div>
        </div>
      </div>
      <div class="col-6 col-md-3 col-xl-2">
        <div class="card card-body p-2">
          <div class="text-muted small">运行时长</div>
          <div class="mt-1 fs-5 fw-bold">{{ fmtUptime(status?.uptimeSeconds ?? 0) }}</div>
        </div>
      </div>
    </div>
    <div class="text-muted small mb-3">
      <i class="ti ti-info-circle me-1"></i>
      进程内 MQTTnet broker,{{ status?.version }} · TCP {{ status?.tcpPort ?? 1883 }}
      <span v-if="status?.enableTls">· TLS {{ status?.tlsPort ?? 8883 }}</span>
      <span v-if="status?.isRunning && status?.enabled === false" class="text-warning ms-1">· 配置已禁用但 broker 仍在运行,需重启生效</span>
      <span v-if="status?.startedAt">· 启动于 {{ formatTime(status.startedAt) }}</span>
    </div>

    <!-- Tab 导航 -->
    <ul class="nav nav-tabs mb-3">
      <li class="nav-item"><button class="nav-link" :class="{ active: activeTab === 'clients' }" @click="switchTab('clients')"><i class="ti ti-devices me-1"></i>在线客户端 <span class="badge bg-primary-lt ms-1">{{ clientTotal }}</span></button></li>
      <li class="nav-item"><button class="nav-link" :class="{ active: activeTab === 'retained' }" @click="switchTab('retained')"><i class="ti ti-bookmark me-1"></i>留存消息 <span class="badge bg-secondary-lt ms-1">{{ status?.retainedMessageCount ?? 0 }}</span></button></li>
      <li class="nav-item"><button class="nav-link" :class="{ active: activeTab === 'topics' }" @click="switchTab('topics')"><i class="ti ti-hash me-1"></i>主题订阅</button></li>
      <li class="nav-item"><button class="nav-link" :class="{ active: activeTab === 'stream' }" @click="switchTab('stream')"><i class="ti ti-broadcast me-1"></i>消息流</button></li>
    </ul>

    <!-- Tab: 在线客户端 -->
    <div v-show="activeTab === 'clients'">
      <div class="d-flex align-items-center gap-2 mb-2">
        <div style="width: 240px"><TInput v-model="clientKeyword" placeholder="clientId/用户名/IP" @change="loadClients" /></div>
        <TButton size="sm" variant="outline-primary" @click="loadClients"><i class="ti ti-search"></i></TButton>
        <span class="text-muted small ms-auto">共 {{ clientTotal }} 条,点击行展开详情</span>
      </div>
      <div class="table-responsive">
        <table class="table table-vcenter card-table">
          <thead>
            <tr><th>ClientId</th><th>用户名</th><th>协议</th><th>IP</th><th>订阅数</th><th>已发送</th><th>连接时间</th><th>最后活跃</th><th>操作</th></tr>
          </thead>
          <tbody>
            <tr v-if="clients.length === 0"><td colspan="9" class="text-center text-muted py-4">暂无在线客户端</td></tr>
            <template v-for="c in clients" :key="c.clientId">
              <tr style="cursor: pointer" @click="toggleExpand(c.clientId)">
                <td class="font-monospace small"><i class="ti me-1" :class="expandedClient === c.clientId ? 'ti-chevron-down' : 'ti-chevron-right'"></i>{{ c.clientId }}</td>
                <td>{{ c.username || '-' }}</td>
                <td><span class="badge" :class="c.protocol === 'WS' ? 'bg-info-lt' : 'bg-primary-lt'">{{ c.protocol }}</span></td>
                <td>{{ c.remoteIp || '-' }}</td>
                <td><span class="badge bg-azure-lt">{{ c.subscribedTopics.length }}</span></td>
                <td>{{ c.messagesSent }}</td>
                <td>{{ formatTime(c.connectedAt) }}</td>
                <td>{{ formatTime(c.lastActiveAt) }}</td>
                <td><TButton variant="ghost-danger" size="sm" @click.stop="kick(c)"><i class="ti ti-plug-off"></i>断开</TButton></td>
              </tr>
              <tr v-if="expandedClient === c.clientId">
                <td colspan="9" class="bg-light">
                  <div class="row g-3 p-2">
                    <div class="col-md-6">
                      <h6 class="small text-muted mb-2">订阅主题 ({{ c.subscribedTopics.length }})</h6>
                      <div v-if="c.subscribedTopics.length === 0" class="text-muted small">无订阅</div>
                      <div v-else>
                        <span v-for="t in c.subscribedTopics" :key="t" class="badge bg-azure-lt font-monospace me-1 mb-1">{{ t }}</span>
                      </div>
                    </div>
                    <div class="col-md-6">
                      <table class="table table-sm table-borderless mb-0">
                        <tbody>
                          <tr><td class="text-muted small" style="width:120px">ClientId</td><td class="font-monospace small">{{ c.clientId }}</td></tr>
                          <tr><td class="text-muted small">用户名</td><td>{{ c.username || '-' }}</td></tr>
                          <tr><td class="text-muted small">协议 / IP</td><td>{{ c.protocol }} / {{ c.remoteIp || '-' }}</td></tr>
                          <tr><td class="text-muted small">已发送消息</td><td>{{ c.messagesSent }}</td></tr>
                          <tr><td class="text-muted small">已收到消息</td><td>{{ c.messagesReceived }}</td></tr>
                          <tr><td class="text-muted small">连接时间</td><td>{{ formatTime(c.connectedAt) }}</td></tr>
                          <tr><td class="text-muted small">最后活跃</td><td>{{ formatTime(c.lastActiveAt) }}</td></tr>
                        </tbody>
                      </table>
                    </div>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Tab: 留存消息 -->
    <div v-show="activeTab === 'retained'">
      <div class="d-flex align-items-center mb-2">
        <span class="text-muted small">留存消息为新订阅者连上时自动下发的最新一条。删除单条会发空 payload 覆盖。</span>
        <TButton v-if="retained.length" size="sm" variant="ghost-danger" class="ms-auto" @click="clearRetained"><i class="ti ti-trash me-1"></i>清空全部</TButton>
      </div>
      <div class="table-responsive">
        <table class="table table-vcenter card-table">
          <thead><tr><th>Topic</th><th>QoS</th><th>大小</th><th>Payload</th><th>操作</th></tr></thead>
          <tbody>
            <tr v-if="retained.length === 0"><td colspan="5" class="text-center text-muted py-4">暂无留存消息</td></tr>
            <tr v-for="r in retained" :key="r.topic">
              <td class="font-monospace small">{{ r.topic }}</td>
              <td><span class="badge" :class="qosClass(r.qos)">{{ qosText(r.qos) }}</span></td>
              <td>{{ fmtBytes(r.payloadBytes) }}</td>
              <td class="font-monospace small text-muted" style="max-width: 400px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap" :title="r.payload">{{ r.payload || '(空)' }}</td>
              <td><TButton variant="ghost-danger" size="sm" @click="deleteRetained(r.topic)"><i class="ti ti-trash"></i></TButton></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Tab: 主题订阅统计 -->
    <div v-show="activeTab === 'topics'">
      <div class="text-muted small mb-2">聚合自在线客户端订阅 + 留存主题 + 最近消息计数。</div>
      <div class="table-responsive">
        <table class="table table-vcenter card-table">
          <thead><tr><th>Topic</th><th>订阅者数</th><th>留存</th><th>最近消息</th></tr></thead>
          <tbody>
            <tr v-if="topics.length === 0"><td colspan="4" class="text-center text-muted py-4">暂无主题</td></tr>
            <tr v-for="t in topics" :key="t.topic">
              <td class="font-monospace small">{{ t.topic }}</td>
              <td><span class="badge" :class="t.subscriberCount > 0 ? 'bg-success-lt' : 'bg-secondary-lt'">{{ t.subscriberCount }}</span></td>
              <td><span v-if="t.isRetained" class="badge bg-warning-lt">留存</span><span v-else class="text-muted">-</span></td>
              <td>{{ t.recentMessageCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Tab: 消息流 + 测试发布 -->
    <div v-show="activeTab === 'stream'">
      <div class="row g-3" v-if="testPanelEnabled">
        <div class="col-md-5">
          <h6 class="mb-2"><i class="ti ti-send me-1"></i>测试发布</h6>
          <div class="mb-2"><TInput v-model="pubTopic" placeholder="主题 如 test/a" /></div>
          <div class="mb-2"><textarea v-model="pubPayload" class="form-control" rows="4" placeholder="消息体"></textarea></div>
          <div class="d-flex align-items-center gap-3 mb-2">
            <label class="form-check form-switch m-0"><input class="form-check-input me-1" type="checkbox" v-model="pubRetained" /><span class="small">Retained</span></label>
            <div class="d-flex align-items-center gap-1">
              <span class="small text-muted">QoS</span>
              <select v-model.number="pubQos" class="form-select form-select-sm" style="width: 80px">
                <option :value="0">0</option><option :value="1">1</option><option :value="2">2</option>
              </select>
            </div>
          </div>
          <TButton size="sm" variant="primary" :loading="pubLoading" @click="doPublish"><i class="ti ti-send me-1"></i>发布</TButton>
        </div>
        <div class="col-md-7">
          <div class="d-flex align-items-center mb-2">
            <h6 class="mb-0"><i class="ti ti-broadcast me-1"></i>实时消息流</h6>
            <div class="ms-auto" style="width: 220px"><TInput v-model="streamFilter" placeholder="按主题过滤" @change="loadRecent" /></div>
          </div>
          <div class="border rounded p-2" style="height: 320px; overflow: auto">
            <div v-if="recent.length === 0" class="text-muted small text-center py-3">暂无消息</div>
            <div v-for="(m, i) in recent" :key="i" class="mb-2 pb-2 border-bottom">
              <div class="d-flex justify-content-between align-items-center">
                <div class="d-flex gap-1 align-items-center">
                  <span class="badge bg-azure-lt font-monospace small">{{ m.topic }}</span>
                  <span class="badge" :class="qosClass(m.qos)">{{ qosText(m.qos) }}</span>
                  <span v-if="m.retained" class="badge bg-warning-lt">retained</span>
                </div>
                <span class="text-muted small">{{ formatTime(m.time) }}</span>
              </div>
              <div class="small mt-1 font-monospace" style="word-break: break-all">{{ m.payload || '(空)' }}</div>
              <div class="text-muted small">from <span class="font-monospace">{{ m.fromClientId || 'backend' }}</span></div>
            </div>
          </div>
        </div>
      </div>
      <div v-else class="text-muted text-center py-4">测试面板已禁用</div>
    </div>
  </TCard>
</template>
