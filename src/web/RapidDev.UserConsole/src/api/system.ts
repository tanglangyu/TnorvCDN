import { http } from '@/utils/request'
import type { SysUser, SysRole, SysMenu, SysDept, SysPost, SysDictType, SysDictData, SysConfig, SysNotice, SysTenant, SysOperLog, SysLoginLog } from '@/types'

// ===== 用户 =====
export const userApi = {
  list: (params: any) => http.get('/system/user/list', params),
  get: (id: number) => http.get<SysUser>(`/system/user/${id}`),
  add: (data: any) => http.post('/system/user', data),
  edit: (id: number, data: any) => http.put(`/system/user/${id}`, data),
  remove: (id: number) => http.delete(`/system/user/${id}`),
  resetPwd: (id: number, password: string) => http.put(`/system/user/${id}/resetPwd`, { password }),
  changeStatus: (id: number, status: number) => http.put(`/system/user/${id}/status`, { status }),
  getTags: (id: number) => http.get<{ tagId?: number; text?: string }[]>(`/system/user/${id}/tags`),
  updateTags: (id: number, tags: { tagId?: number; text?: string }[]) => http.put(`/system/user/${id}/tags`, { tags }),
  resetIdCard: (id: number) => http.put(`/system/user/${id}/idcard-reset`),
}

// ===== 身份证二要素验证(创蓝) =====
export const idCardApi = {
  verify: (name: string, idNum: string, userId?: number) => http.post('/idcard/verify', { name, idNum, userId }),
  logList: (params: any) => http.get('/idcard/log/list', params),
  logExport: (params: any) => http.get('/idcard/log/export', params, { responseType: 'blob' }),
}

// ===== IP 归属地查询(聚合 ip2region/纯真/Bilibili) =====
export const ipApi = {
  locate: (ip: string, includeOnline = false) => http.get('/tool/ip/location', { ip, includeOnline }),
  locateAll: (ip: string, includeOnline = false) => http.get('/tool/ip/location/all', { ip, includeOnline }),
  myIp: (includeOnline = false) => http.get('/tool/ip/myip', { includeOnline }),
  locateBatch: (ips: string[], includeOnline = false) => http.post('/tool/ip/locations', { ips, includeOnline }),
}

// ===== 角色 =====
export const roleApi = {
  list: (params: any) => http.get('/system/role/list', params),
  all: () => http.get<SysRole[]>('/system/role/all'),
  get: (id: number) => http.get<SysRole>(`/system/role/${id}`),
  add: (data: SysRole, menuIds: number[], deptIds: number[]) => http.post('/system/role', { role: data, menuIds, deptIds }),
  edit: (id: number, data: SysRole, menuIds: number[], deptIds: number[]) => http.put(`/system/role/${id}`, { role: data, menuIds, deptIds }),
  remove: (id: number) => http.delete(`/system/role/${id}`),
  menuIds: (id: number) => http.get<number[]>(`/system/role/${id}/menuIds`),
  deptIds: (id: number) => http.get<number[]>(`/system/role/${id}/deptIds`),
  permissions: (id: number) => http.get<{ menuIds: number[]; deptIds: number[]; permissions: string[] }>(`/system/role/${id}/permissions`),
}

// ===== 权限点(系统所有权限点,供预览和角色分配) =====
export interface PermissionPoint {
  module: string
  name: string
  code: string
  description: string
}
export interface PermissionModule {
  module: string
  moduleName: string
  points: PermissionPoint[]
}
export const permissionApi = {
  tree: () => http.get<PermissionModule[]>('/system/permission/points'),
  flat: () => http.get<PermissionPoint[]>('/system/permission/points/flat'),
}

// ===== 菜单 =====
export const menuApi = {
  list: (name?: string) => http.get<SysMenu[]>('/system/menu/list', { menuName: name }),
  tree: () => http.get<SysMenu[]>('/system/menu/tree'),
  add: (data: SysMenu) => http.post('/system/menu', data),
  edit: (id: number, data: SysMenu) => http.put(`/system/menu/${id}`, data),
  remove: (id: number) => http.delete(`/system/menu/${id}`),
}

// ===== 部门 =====
export const deptApi = {
  list: (name?: string) => http.get<SysDept[]>('/system/dept/list', { name }),
  all: () => http.get<SysDept[]>('/system/dept/all'),
  add: (data: SysDept) => http.post('/system/dept', data),
  edit: (id: number, data: SysDept) => http.put(`/system/dept/${id}`, data),
  remove: (id: number) => http.delete(`/system/dept/${id}`),
}

// ===== 岗位 =====
export const postApi = {
  list: (params: any) => http.get('/system/post/list', params),
  all: () => http.get<SysPost[]>('/system/post/all'),
  add: (data: SysPost) => http.post('/system/post', data),
  edit: (id: number, data: SysPost) => http.put(`/system/post/${id}`, data),
  remove: (id: number) => http.delete(`/system/post/${id}`),
}

// ===== 字典 =====
export const dictApi = {
  typeList: (params: any) => http.get('/system/dict/type/list', params),
  addType: (data: SysDictType) => http.post('/system/dict/type', data),
  editType: (id: number, data: SysDictType) => http.put(`/system/dict/type/${id}`, data),
  removeType: (id: number) => http.delete(`/system/dict/type/${id}`),
  dataList: (dictType: string) => http.get<SysDictData[]>('/system/dict/data/list', { dictType }),
  addData: (data: SysDictData) => http.post('/system/dict/data', data),
  editData: (id: number, data: SysDictData) => http.put(`/system/dict/data/${id}`, data),
  removeData: (id: number) => http.delete(`/system/dict/data/${id}`),
}

// ===== 参数配置 =====
export const configApi = {
  list: (params: any) => http.get('/system/config/list', params),
  getByKey: (key: string) => http.get(`/system/config/key/${key}`),
  add: (data: SysConfig) => http.post('/system/config', data),
  edit: (id: number, data: SysConfig) => http.put(`/system/config/${id}`, data),
  remove: (id: number) => http.delete(`/system/config/${id}`),
}

// ===== 通知公告 =====
export const noticeApi = {
  list: (params: any) => http.get('/system/notice/list', params),
  get: (id: number) => http.get<SysNotice>(`/system/notice/${id}`),
  add: (data: SysNotice) => http.post('/system/notice', data),
  edit: (id: number, data: SysNotice) => http.put(`/system/notice/${id}`, data),
  remove: (id: number) => http.delete(`/system/notice/${id}`),
}

// ===== 租户 =====
export const tenantApi = {
  list: (params: any) => http.get('/system/tenant/list', params),
  get: (id: number) => http.get<SysTenant>(`/system/tenant/${id}`),
  add: (data: SysTenant) => http.post('/system/tenant', data),
  edit: (id: number, data: SysTenant) => http.put(`/system/tenant/${id}`, data),
  remove: (id: number) => http.delete(`/system/tenant/${id}`),
}

// ===== 日志 =====
export const logApi = {
  operList: (params: any) => http.get('/system/log/oper/list', params),
  loginList: (params: any) => http.get('/system/log/login/list', params),
  cleanOper: () => http.delete('/system/log/oper/clean'),
  cleanLogin: () => http.delete('/system/log/login/clean'),
  exportOper: (params: any) => http.get('/system/log/oper/export', params, { responseType: 'blob' }),
  exportLogin: (params: any) => http.get('/system/log/login/export', params, { responseType: 'blob' }),
  httpList: (params: any) => http.get('/system/httplog/list', params),
  cleanHttp: () => http.delete('/system/httplog/clean'),
  exportHttp: (params: any) => http.get('/system/httplog/export', params, { responseType: 'blob' }),
}
