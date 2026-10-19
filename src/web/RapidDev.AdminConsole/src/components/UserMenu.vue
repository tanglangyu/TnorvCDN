<script setup lang="ts">
/**
 * 用户头像下拉菜单 —— 不依赖 Bootstrap dropdown JS,纯 Vue 实现。
 */
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  /** 昵称,取首字符作为头像 */
  nickName?: string
  /** 登录名 */
  userName?: string
  /** 角色列表 */
  roles?: string[]
}>()

const emit = defineEmits<{
  (e: 'logout'): void
}>()

const open = ref(false)
const menu = ref<HTMLDivElement>()

function toggle() {
  open.value = !open.value
  if (open.value) setTimeout(() => document.addEventListener('click', closeOutside, { once: true }), 0)
}
function closeOutside(e: MouseEvent) {
  if (menu.value && !menu.value.contains(e.target as Node)) open.value = false
}

onMounted(() => {
  // 点击内部链接不关闭(等路由跳转自然关闭)
})

const avatarChar = computed(() => {
  const n = props.nickName || props.userName || 'U'
  return n.charAt(0).toUpperCase()
})
import { computed } from 'vue'
</script>

<template>
  <div class="user-menu" ref="menu">
    <button class="user-menu-btn" :class="{ 'is-active': open }" @click.stop="toggle" title="用户菜单">
      <span class="user-avatar">{{ avatarChar }}</span>
      <div class="user-info d-none d-xl-flex">
        <div class="user-name">{{ nickName || userName || '用户' }}</div>
        <div class="user-role">{{ (roles && roles.length > 0) ? roles.join(', ') : '管理员' }}</div>
      </div>
      <i class="ti ti-chevron-down user-arrow" :class="{ 'is-open': open }"></i>
    </button>

    <Teleport to="body">
      <div v-if="open" class="user-dropdown-backdrop" @click="open = false"></div>
      <transition name="fade-scale">
        <div v-if="open" class="user-dropdown">
          <RouterLink to="/profile" class="user-dropdown-item" @click="open = false">
            <i class="ti ti-user"></i><span>个人中心</span>
          </RouterLink>
          <div class="user-dropdown-divider"></div>
          <button class="user-dropdown-item is-danger" @click="open = false; emit('logout')">
            <i class="ti ti-logout"></i><span>退出登录</span>
          </button>
        </div>
      </transition>
    </Teleport>
  </div>
</template>

<style scoped>
.user-menu-btn {
  display: flex; align-items: center; gap: 8px;
  padding: 4px 8px; border: none; background: transparent;
  border-radius: 8px; cursor: pointer; color: var(--el-text-color-primary);
  transition: background 0.15s;
}
.user-menu-btn:hover, .user-menu-btn.is-active {
  background: var(--el-fill-color-light);
}

.user-avatar {
  width: 34px; height: 34px; border-radius: 50%;
  background: var(--el-color-primary); color: #fff;
  display: inline-flex; align-items: center; justify-content: center;
  font-weight: 700; font-size: 0.9rem; flex-shrink: 0;
}
.user-info { flex-direction: column; line-height: 1.3; }
.user-name { font-size: 0.85rem; font-weight: 600; max-width: 120px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.user-role { font-size: 0.7rem; color: var(--el-text-color-secondary); }

.user-arrow { font-size: 0.8rem; transition: transform 0.2s; color: var(--el-text-color-secondary); }
.user-arrow.is-open { transform: rotate(180deg); }

/* ===== 下拉面板 ===== */
.user-dropdown-backdrop {
  position: fixed; inset: 0; z-index: 1060;
}
.user-dropdown {
  position: fixed; top: 56px; right: 16px; z-index: 1065;
  min-width: 180px; background: var(--el-bg-color);
  border: 1px solid var(--el-border-color);
  border-radius: 10px; box-shadow: 0 8px 32px rgba(0,0,0,0.12);
  padding: 6px; overflow: hidden;
}
:global(html.dark) .user-dropdown {
  background: #1e293b; border-color: #334155;
  box-shadow: 0 8px 32px rgba(0,0,0,0.5);
}

.user-dropdown-item {
  display: flex; align-items: center; gap: 10px;
  width: 100%; padding: 8px 12px; border: none; background: transparent;
  border-radius: 6px; cursor: pointer; color: var(--el-text-color-primary);
  font-size: 0.85rem; text-decoration: none; text-align: left;
  transition: background 0.1s;
}
.user-dropdown-item:hover { background: var(--el-fill-color-light); }
.user-dropdown-item.is-danger { color: var(--el-color-danger); }
.user-dropdown-item.is-danger:hover { background: var(--el-color-danger-light-9); }
.user-dropdown-item i { font-size: 1.1rem; width: 20px; text-align: center; opacity: 0.8; }

.user-dropdown-divider {
  height: 1px; background: var(--el-border-color); margin: 4px 0;
}

/* transition */
.fade-scale-enter-active, .fade-scale-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}
.fade-scale-enter-from, .fade-scale-leave-to {
  opacity: 0; transform: translateY(-8px) scale(0.96);
}
</style>
