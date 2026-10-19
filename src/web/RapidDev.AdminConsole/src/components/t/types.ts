/** 组件库通用类型 */

export type ButtonVariant =
  | 'primary'
  | 'secondary'
  | 'success'
  | 'danger'
  | 'warning'
  | 'info'
  | 'light'
  | 'dark'
  | 'link'
  | 'outline-primary'
  | 'outline-secondary'
  | 'outline-success'
  | 'outline-danger'
  | 'outline-warning'
  | 'outline-info'
  | 'ghost-primary'
  | 'ghost-secondary'
  | 'ghost-success'
  | 'ghost-danger'
  | 'ghost-warning'
  | 'ghost-info'

export type ButtonSize = 'sm' | 'md' | 'lg'

export interface TableColumn {
  /** 字段名 */
  prop: string
  /** 列标题 */
  label: string
  /** 列宽 */
  width?: number | string
  /** 最小宽度 */
  minWidth?: number | string
  /** 是否固定列(left/right) */
  fixed?: 'left' | 'right'
  /** 是否可排序 */
  sortable?: boolean
  /** 对齐方式 */
  align?: 'left' | 'center' | 'right'
  /** 自定义渲染插槽名(用于 #body-xxx) */
  slot?: string
  /** 是否显示 */
  show?: boolean
  /** 格式化函数 */
  formatter?: (row: any, col: TableColumn, value: any) => string
}

export interface FormItemSchema {
  /** 字段名 */
  prop: string
  /** 后端属性名(PascalCase)。不填则用 prop。用于解决前端驼峰/后端 PascalCase 双轨 */
  field?: string
  /** 标签 */
  label: string
  /** 控件类型 */
  type?: 'input' | 'textarea' | 'select' | 'switch' | 'radio' | 'checkbox' | 'date' | 'datetime' | 'number'
  /** 占位符 */
  placeholder?: string
  /** 选项(select/radio/checkbox) */
  options?: { label: string; value: any }[]
  /** 是否必填 */
  required?: boolean
  /** 校验规则 */
  rules?: ((value: any) => true | string)[]
  /** 默认值 */
  defaultValue?: any
  /** 占用栅格列数(1-12) */
  span?: number
  /** 是否禁用 */
  disabled?: boolean
  /** 透传给控件的额外属性 */
  props?: Record<string, any>
}

export interface PageQuery {
  pageIndex: number
  pageSize: number
  [key: string]: any
}
