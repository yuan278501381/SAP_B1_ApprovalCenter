/**
 * 全局应用级与环境配置
 */
export const appConfig = {
  // 默认 SAP 公司账套数据库代码
  defaultCompanyId: 'DB_KCC',

  // 默认当前操作员 (本地开发/无 SAP 会话时的兜底操作员)
  defaultUser: 'admin',

  // API 请求基础超时时间 (毫秒)
  apiTimeoutMs: 30000,

  // 待办轮询与数据自动刷新间隔 (毫秒)
  pollingIntervalMs: 15000,

  // 快捷发起审批支持的对象列表配置
  quickLaunchObjects: [
    { code: 'CHORDR', name: '型号订单', defaultKey: '1001' },
    { code: 'CHOQUT', name: '型号报价单', defaultKey: '1001' },
    { code: 'ORDR', name: '标准销售订单', defaultKey: '1001' },
    { code: 'ODRF', name: '单据草稿', defaultKey: '1001' },
    { code: 'OBTD', name: '日记账凭证批', defaultKey: '1001' }
  ]
} as const
