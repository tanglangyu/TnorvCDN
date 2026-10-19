<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { permissionApi, type PermissionModule } from '@/api/system'
import { TCard, TButton, TInput } from '@/components/t'

const loading = ref(false)
const modules = ref<PermissionModule[]>([])
const keyword = ref('')
const activeModule = ref<string>('')

const filteredModules = computed(() => {
  if (!keyword.value) return modules.value
  const kw = keyword.value.toLowerCase()
  return modules.value
    .map(m => ({
      ...m,
      points: m.points.filter(p =>
        p.code.toLowerCase().includes(kw) ||
        p.name.toLowerCase().includes(kw) ||
        p.description.toLowerCase().includes(kw)
      ),
    }))
    .filter(m => m.points.length > 0)
})

const totalCount = computed(() => modules.value.reduce((s, m) => s + m.points.length, 0))

async function load() {
  loading.value = true
  try {
    modules.value = await permissionApi.tree()
    if (modules.value.length && !activeModule.value) activeModule.value = modules.value[0].module
  } finally {
    loading.value = false
  }
}

function copyCode(code: string) {
  navigator.clipboard?.writeText(code)
}

onMounted(load)
</script>

<template>
  <TCard title="权限点管理">
    <template #actions>
      <div class="d-flex align-items-center gap-2">
        <span class="text-muted small">共 {{ totalCount }} 个权限点</span>
        <TInput v-model="keyword" placeholder="搜索权限点编码/名称" style="width: 240px" @keyup.enter="">
          <template #prefix><i class="ti ti-search"></i></template>
        </TInput>
        <TButton variant="ghost-secondary" @click="load"><i class="ti ti-refresh"></i></TButton>
      </div>
    </template>

    <div v-if="loading" class="text-center py-4 text-muted">加载中...</div>

    <div v-else class="row g-3">
      <div class="col-md-3">
        <div class="list-group">
          <button
            v-for="m in filteredModules"
            :key="m.module"
            class="list-group-item list-group-item-action d-flex justify-content-between align-items-center"
            :class="{ active: activeModule === m.module }"
            @click="activeModule = m.module"
          >
            <span><i class="ti ti-folder me-2"></i>{{ m.moduleName }}</span>
            <span class="badge bg-primary-lt">{{ m.points.length }}</span>
          </button>
        </div>
      </div>

      <div class="col-md-9">
        <template v-for="m in filteredModules" :key="m.module">
          <div v-show="activeModule === m.module">
            <h5 class="mb-3">
              <i class="ti ti-folder me-2"></i>{{ m.moduleName }}
              <span class="text-muted small">({{ m.points.length }} 个权限点)</span>
            </h5>
            <div class="table-responsive">
              <table class="table table-vcenter card-table">
                <thead>
                  <tr>
                    <th style="width: 40%">权限编码</th>
                    <th style="width: 25%">名称</th>
                    <th>描述</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="p in m.points" :key="p.code">
                    <td>
                      <span class="badge bg-azure-lt cursor-pointer" @click="copyCode(p.code)" title="点击复制">
                        <code>{{ p.code }}</code>
                        <i class="ti ti-copy ms-1"></i>
                      </span>
                    </td>
                    <td class="text-muted">{{ p.name }}</td>
                    <td class="text-muted small">{{ p.description }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </template>
      </div>
    </div>
  </TCard>
</template>

<style scoped>
.cursor-pointer { cursor: pointer; }
.list-group-item.active .badge { background: rgba(255,255,255,0.3) !important; color: #fff; }
</style>
