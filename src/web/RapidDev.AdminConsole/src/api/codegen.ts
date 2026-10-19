import { http } from '@/utils/request'

export const codegenApi = {
  dbTables: () => http.get('/tool/gen/db/tables'),
  dbColumns: (tableName: string) => http.get('/tool/gen/db/columns', { tableName }),
  import: (data: { tableName: string; moduleName: string; businessName: string }) => http.post('/tool/gen/import', data),
  list: (params: any) => http.get('/tool/gen/list', params),
  get: (id: number) => http.get(`/tool/gen/${id}`),
  preview: (id: number) => http.get<Record<string, string>>(`/tool/gen/preview/${id}`),
}
