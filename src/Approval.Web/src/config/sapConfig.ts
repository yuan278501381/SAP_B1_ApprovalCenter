/**
 * SAP Business One 单据对象、色彩主题与子表官方术语配置
 */
export interface SapObjectStyle {
  bg: string
  border: string
  color: string
  numColor: string
}

export interface SapObjectMeta {
  name: string
  style: SapObjectStyle
}

// SAP 单据对象字典与主题色彩配置
export const sapObjectMap: Record<string, SapObjectMeta> = {
  CHORDR: {
    name: '型号订单',
    style: { bg: '#eff6ff', border: '#bfdbfe', color: '#1d4ed8', numColor: '#3b82f6' }
  },
  CHOQUT: {
    name: '型号报价单',
    style: { bg: '#f5f3ff', border: '#ddd6fe', color: '#6d28d9', numColor: '#8b5cf6' }
  },
  ORDR: {
    name: '销售订单',
    style: { bg: '#eff6ff', border: '#bfdbfe', color: '#1d4ed8', numColor: '#3b82f6' }
  },
  '17': {
    name: '销售订单',
    style: { bg: '#eff6ff', border: '#bfdbfe', color: '#1d4ed8', numColor: '#3b82f6' }
  },
  OQUT: {
    name: '销售报价单',
    style: { bg: '#f5f3ff', border: '#ddd6fe', color: '#6d28d9', numColor: '#8b5cf6' }
  },
  '23': {
    name: '销售报价单',
    style: { bg: '#f5f3ff', border: '#ddd6fe', color: '#6d28d9', numColor: '#8b5cf6' }
  },
  OPOR: {
    name: '采购订单',
    style: { bg: '#f0fdf4', border: '#bbf7d0', color: '#15803d', numColor: '#22c55e' }
  },
  '22': {
    name: '采购订单',
    style: { bg: '#f0fdf4', border: '#bbf7d0', color: '#15803d', numColor: '#22c55e' }
  },
  OWTR: {
    name: '库存转储请求',
    style: { bg: '#fffbeb', border: '#fde68a', color: '#b45309', numColor: '#f59e0b' }
  },
  '1250000001': {
    name: '库存转储请求',
    style: { bg: '#fffbeb', border: '#fde68a', color: '#b45309', numColor: '#f59e0b' }
  },
  OWOR: {
    name: '生产订单',
    style: { bg: '#fff7ed', border: '#fed7aa', color: '#c2410c', numColor: '#f97316' }
  },
  '202': {
    name: '生产订单',
    style: { bg: '#fff7ed', border: '#fed7aa', color: '#c2410c', numColor: '#f97316' }
  },
  ODRF: {
    name: '单据草稿',
    style: { bg: '#f8fafc', border: '#cbd5e1', color: '#475569', numColor: '#64748b' }
  },
  '112': {
    name: '单据草稿',
    style: { bg: '#f8fafc', border: '#cbd5e1', color: '#475569', numColor: '#64748b' }
  },
  OBTD: {
    name: '日记账凭证批',
    style: { bg: '#fdf4ff', border: '#f5d0fe', color: '#a21caf', numColor: '#c026d3' }
  },
  '28': {
    name: '日记账凭证批',
    style: { bg: '#fdf4ff', border: '#f5d0fe', color: '#a21caf', numColor: '#c026d3' }
  },
  OJDT: {
    name: '日记账分录',
    style: { bg: '#fdf4ff', border: '#f5d0fe', color: '#a21caf', numColor: '#c026d3' }
  },
  '30': {
    name: '日记账分录',
    style: { bg: '#fdf4ff', border: '#f5d0fe', color: '#a21caf', numColor: '#c026d3' }
  }
}

// 兜底单据对象样式
export const fallbackSapObjectStyle: SapObjectStyle = {
  bg: '#f1f5f9',
  border: '#cbd5e1',
  color: '#475569',
  numColor: '#64748b'
}

// SAP 标准单据子表官方术语字典 (标准化命名规范)
export const sapChildTableOfficialNames: Record<string, string> = {
  // 营销与标准单据行表 -> 内容
  RDR1: '内容',
  QUT1: '内容',
  POR1: '内容',
  WTR1: '内容',
  DRF1: '内容',
  DocumentLines: '内容',
  Lines: '内容',
  CH_ORDR_1: '内容',
  '@CH_ORDR_1': '内容',
  CH_OQUT_1: '内容',
  '@CH_OQUT_1': '内容',

  // 生产订单子表 -> 组件
  WOR1: '组件',
  ProductionOrderLines: '组件',
  Components: '组件',

  // 运费与附加费用 -> 附加费用
  RDR3: '附加费用',
  QUT3: '附加费用',
  POR3: '附加费用',
  DRF3: '附加费用',
  DocumentAdditionalExpenses: '附加费用',
  Expenses: '附加费用',
  CH_ORDR_3: '附加费用',
  '@CH_ORDR_3': '附加费用',
  CH_OQUT_3: '附加费用',
  '@CH_OQUT_3': '附加费用',

  // 财务凭证分录行 -> 分录行
  BTD1: '分录行',
  JDT1: '分录行',
  JournalVoucherLines: '分录行',
  JournalEntryLines: '分录行'
}
