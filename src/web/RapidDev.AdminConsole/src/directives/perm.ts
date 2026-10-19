import type { Directive, DirectiveBinding } from 'vue'
import { useUserStore } from '@/store/user'

/**
 * v-perm 权限指令:隐藏无权限的元素。
 *
 * 用法:
 *   <button v-perm="'system:user:add'">新增</button>
 *   <button v-perm="['system:user:edit', 'system:user:delete']">编辑/删除</button>
 *
 * 无权限时元素从 DOM 中移除(display:none 也可以但指令更彻底)。
 */
export const perm: Directive = {
  mounted(el: HTMLElement, binding: DirectiveBinding) {
    check(el, binding)
  },
  updated(el: HTMLElement, binding: DirectiveBinding) {
    check(el, binding)
  },
}

function check(el: HTMLElement, binding: DirectiveBinding) {
  const userStore = useUserStore()
  const value = binding.value

  let has = false
  if (typeof value === 'string') {
    has = userStore.hasPermission(value)
  } else if (Array.isArray(value)) {
    has = userStore.hasAnyPermission(value)
  }

  if (!has) {
    // 直接移除 DOM 元素
    if (el.parentNode) el.parentNode.removeChild(el)
    else el.style.display = 'none'
  }
}

// 默认导出方便 app.directive('perm', permDirective)
export default perm
