<script lang="ts">
import { defineComponent, h, computed } from 'vue'
import { useUserStore } from '@/store/user'

export default defineComponent({
  name: 'PermContainer',
  props: {
    perm: { type: [String, Array] as any, required: true },
  },
  setup(props, { slots }) {
    const userStore = useUserStore()
    const visible = computed(() => {
      if (typeof props.perm === 'string') return userStore.hasPermission(props.perm)
      if (Array.isArray(props.perm)) return userStore.hasAnyPermission(props.perm)
      return false
    })
    return () => (visible.value ? h('div', { class: 'perm-container' }, slots.default?.()) : null)
  },
})
</script>
