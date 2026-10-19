<script setup lang="ts">
/**
 * 加密模式 Switch 开关 —— 两档切换(明文 / AES)。
 *
 * 改进:切换是 async,切换完成前显示 loading 并禁用按钮,防止时序竞态。
 */
import { useCryptoStore } from '@/store/crypto'
import { computed } from 'vue'

const cryptoStore = useCryptoStore()

const modes = [
  { key: 'off' as const, label: '明文', icon: 'ti-world' },
  { key: 'aes' as const, label: 'AES',  icon: 'ti-lock' },
]

const activeIndex = computed(() => {
  const idx = modes.findIndex(m => m.key === cryptoStore.mode)
  return idx >= 0 ? idx : 0
})

async function select(m: typeof modes[number]) {
  if (cryptoStore.locked || cryptoStore.mode === m.key) return
  await cryptoStore.switchMode(m.key)
}
</script>

<template>
  <div class="crypto-switch" :title="`加密模式: ${cryptoStore.mode.toUpperCase()}`">
    <div class="crypto-slider" :class="{ loading: cryptoStore.locked }" :style="{ transform: `translateX(${activeIndex * 100}%)` }"></div>
    <button
      v-for="(m, i) in modes" :key="m.key"
      class="crypto-option"
      :class="{ active: cryptoStore.mode === m.key }"
      :disabled="cryptoStore.locked"
      @click="select(m)"
    >
      <i class="ti" :class="cryptoStore.locked && cryptoStore.mode !== m.key ? 'ti-loader-2 spin' : m.icon"></i>
      <span class="crypto-label">{{ m.label }}</span>
    </button>
  </div>
</template>

<style scoped>
.crypto-switch {
  position: relative;
  display: inline-flex;
  background: var(--el-fill-color-light);
  border: 1px solid var(--el-border-color);
  border-radius: 20px;
  padding: 2px;
  margin-right: 6px;
  overflow: hidden;
}

.crypto-slider {
  position: absolute;
  top: 2px;
  left: 2px;
  width: calc(50% - 2px);
  height: calc(100% - 4px);
  background: var(--el-color-primary);
  border-radius: 18px;
  transition: transform 0.25s cubic-bezier(0.4, 0, 0.2, 1), background 0.2s;
  z-index: 0;
}
.crypto-slider.loading { opacity: 0.6; }
.crypto-switch:has(.crypto-option.active:nth-child(2)) .crypto-slider { background: var(--el-color-success); }

.crypto-option {
  position: relative;
  z-index: 1;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 5px 12px;
  border: none;
  background: transparent;
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
  font-weight: 600;
  cursor: pointer;
  transition: color 0.2s;
  white-space: nowrap;
  border-radius: 18px;
}
.crypto-option.active { color: #fff; }
.crypto-option:hover:not(.active):not(:disabled) { color: var(--el-text-color-primary); }
.crypto-option:disabled { opacity: 0.5; cursor: not-allowed; }
.crypto-option i { font-size: 0.85rem; }

.crypto-label { display: none; }
@media (min-width: 576px) { .crypto-label { display: inline; } }

.spin { animation: spin 0.8s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
</style>
