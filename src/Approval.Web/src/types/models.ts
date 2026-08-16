/** SAP 元数据字段信息 */
export interface FieldMetaInfo {
  fieldName: string
  description: string
  dataType: string
  validValues: Record<string, string>
}

/** 对象元数据查询结果 */
export interface ObjectMetadataResult {
  objectCode: string
  tableName: string
  objectDescription: string
  headerFields: Record<string, FieldMetaInfo>
  childTableFields: Record<string, Record<string, FieldMetaInfo>>
  childTableDescriptions: Record<string, string>
}

/** 工作流实例 */
export interface WorkflowInstance {
  id: string
  objectCode: string
  docEntry: string
  status: 'Pending' | 'Approved' | 'Rejected' | 'Cancelled' | 'Superceded'
  createdAt: string
  dataSha256: string
}

/** 工作流任务 */
export interface WorkflowTask {
  id: string
  instanceId: string
  assignee: string
  status: 'Pending' | 'Approved' | 'Rejected' | 'Returned'
  decidedAt?: string
  comment?: string
}

/** API 通用响应包装 */
export interface ApiResponse<T = unknown> {
  success: boolean
  message?: string
  data: T
}

/** 待办任务列表项 */
export interface TaskListItem {
  taskId: string
  instanceId: string
  objectCode: string
  docEntry: string
  title: string
  status: string
  createdAt: string
  assignee: string
  creatorName: string
}
