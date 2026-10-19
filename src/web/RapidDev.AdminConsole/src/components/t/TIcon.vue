<script setup lang="ts">
/** Tabler 图标组件(用 CSS class,配合 @tabler/icons-webfont 即可)。
 * 用法:<TIcon name="home" /> → <i class="ti ti-home"></i>
 */
defineProps<{ name: string; size?: number }>()
</script>
<template>
  <i class="ti" :class="`ti-${name}`" :style="size ? { fontSize: size + 'px' } : undefined"></i>
</template>
