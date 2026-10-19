// ===== 通用 API 类型 =====

/** 统一返回结果 */
export interface Result<T = any> {
  code: number
  message: string
  data: T
}

/** 分页结果 */
export interface PagedResult<T> {
  list: T[]
  total: number
  pageIndex: number
  pageSize: number
  totalPages: number
}

/** 分页查询基础参数 */
export interface PagedQuery {
  pageIndex?: number
  pageSize?: number
  orderBy?: string
  orderDirection?: 'asc' | 'desc'
  [key: string]: any
}

/** 过滤操作符 */
export type FilterOp =
  | 'eq' | 'neq' | 'like' | 'll' | 'rl'
  | 'gt' | 'gte' | 'lt' | 'lte'
  | 'in' | 'notin' | 'null' | 'notnull' | 'between'

/** 单条过滤规则 */
export interface FilterRule {
  field: string
  op: FilterOp
  value?: any
  value2?: any
}

/** 单条排序规则 */
export interface SortRule {
  field: string
  direction: 'asc' | 'desc'
}

/** 高级查询参数(继承分页) */
export interface AdvancedQuery extends PagedQuery {
  filters?: FilterRule[]
  sorts?: SortRule[]
}

// ===== 业务实体 =====

export interface SysUser {
  id: number
  userName: string
  nickName: string
  phone?: string
  email?: string
  gender: number
  avatar?: string
  deptId?: number
  status: number
  userType?: number  // 1=客户(默认) 2=内部
  remark?: string
  roleIds?: number[]
  postIds?: number[]
}

export interface SysRole {
  id: number
  roleName: string
  roleKey: string
  dataScope: number
  sort: number
  status: number
  remark?: string
}

export interface SysMenu {
  id: number
  parentId: number
  menuName: string
  menuType: number // 0目录 1菜单 2按钮
  path?: string
  component?: string
  query?: string
  permission?: string
  icon?: string
  orderNum: number
  visible: number
  isFrame: number
  isCache: number
  isAffix: number
  isLink: number
  status: number
  remark?: string
}

export interface MenuNode {
  id: number
  parentId: number
  name: string
  path?: string
  component?: string
  query?: string
  permission?: string
  icon?: string
  orderNum: number
  visible: number
  isFrame: number
  isCache: number
  isAffix: number
  menuType: number
  children: MenuNode[]
}

export interface SysDept {
  id: number
  parentId: number
  deptName: string
  sort: number
  leader?: string
  phone?: string
  email?: string
  status: number
}

export interface SysPost {
  id: number
  postCode: string
  postName: string
  sort: number
  status: number
}

export interface SysDictType {
  id: number
  dictName: string
  dictType: string
  status: number
  remark?: string
}

export interface SysDictData {
  id: number
  dictType: string
  dictLabel: string
  dictValue: string
  cssClass?: string
  listClass?: string
  status: number
  sort: number
  isDefault: boolean
  remark?: string
}

export interface SysConfig {
  id: number
  configName: string
  configKey: string
  configValue?: string
  configType: number
  remark?: string
}

export interface SysTenant {
  id: number
  name: string
  code: string
  contact?: string
  phone?: string
  expireAt?: string
  accountLimit: number
  status: number
  remark?: string
}

export interface SysNotice {
  id: number
  noticeTitle: string
  noticeType: number
  noticeContent?: string
  status: number
  remark?: string
}

export interface LoginResult {
  accessToken: string
  refreshToken: string
  expiresAt: number
  sessionId?: string
}

export interface UserInfo {
  userId: number
  userName: string
  nickName: string
  avatar?: string
  tenantId: number
  roles: string[]
  permissions: string[]
}

export interface SysJob {
  id: number
  jobName: string
  jobGroup: string
  invokeTarget: string
  cronExpression: string
  misfirePolicy: number
  concurrent: number
  status: number
  methodParams?: string
  remark?: string
}

export interface SysJobLog {
  id: number
  jobName?: string
  invokeTarget?: string
  status: number
  errorMsg?: string
  costTime: number
  startTime: string
  endTime: string
}

export interface RateLimitPolicy {
  id: number
  policyName: string
  routePattern: string
  httpMethods: string
  partitionBy: string
  algorithm: string
  permitLimit: number
  tokensPerPeriod: number
  periodSeconds: number
  queueLimit: number
  status: number
  remark?: string
}

export interface SysOperLog {
  id: number
  module?: string
  title?: string
  businessType: number
  method?: string
  requestMethod?: string
  operUrl?: string
  operIp?: string
  operLocation?: string
  operUserId?: number
  operUserName?: string
  operParam?: string
  jsonResult?: string
  status: number
  errorMsg?: string
  costTime: number
  operTime: string
}

export interface SysLoginLog {
  id: number
  userName?: string
  loginIp?: string
  loginLocation?: string
  browser?: string
  os?: string
  device?: string
  message?: string
  status: number
  loginTime: string
}

export interface GenTable {
  id: number
  tableName: string
  tableComment?: string
  className: string
  moduleName: string
  businessName: string
  genType: string
  genMenu: number
  permissionPrefix: string
  functionAuthor?: string
  columns?: GenTableColumn[]
  createdAt?: string
  updatedAt?: string
}

export interface GenTableColumn {
  id: number
  tableId: number
  columnName: string
  columnComment?: string
  columnType: string
  csharpType: string
  csharpField: string
  isPk: boolean
  isIncrement: boolean
  isRequired: boolean
  isInsert: boolean
  isEdit: boolean
  isList: boolean
  isQuery: boolean
  isSort: boolean
  htmlType: string
  sort: number
  dictType?: string
}

export interface SysHttpLog {
  id: number
  traceId?: string
  method: string
  path: string
  queryString?: string
  statusCode: number
  costTimeMs: number
  clientIp?: string
  clientIps?: string
  userAgent?: string
  userId?: number
  userName?: string
  requestHeaders?: string
  requestBody?: string
  responseHeaders?: string
  responseBody?: string
  exception?: string
  requestTime: string
  tenantId: number
}

export interface SysFile {
  id: number
  originalName: string
  storedName: string
  contentType: string
  fileSize: number
  storagePath: string
  md5?: string
  accessCount: number
  maxAccessCount: number
  signatureEnabled: boolean
  status: number
  createdAt?: string
  createdBy?: number
}

export interface FileSignatureResult {
  signature: string
  nonce: string
  url: string
}

export interface SysNotification {
  id: number
  userId: number
  title: string
  content?: string
  type: number
  relatedId?: string
  isRead: boolean
  readAt?: string
  createdAt: string
}

export interface SessionInfo {
  sessionId: string
  userId: number
  userName: string
  jti: string
  device?: string
  ip?: string
  loginAt: string
  lastActiveAt: string
  expireAt: string
  isActive: boolean
}
