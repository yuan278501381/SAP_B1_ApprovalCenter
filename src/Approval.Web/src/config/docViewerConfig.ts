/**
 * 单据详情查看器 (DocDataViewer) 专项目配置
 */

// 默认推荐固定在顶部概览卡片的核心高频字段
export const defaultPinnedFields: string[] = [
  'U_DocDate', 'DocDate', 'U_DocDueDate', 'DocDueDate', 'Creator',
  'U_DocCur', 'DocCur', 'U_SoType', 'U_SlpCode', 'U_GroupNum', 'U_saleass',
  'U_DELIVER', 'U_PAGREQ', 'U_Comments', 'Comments'
]

// 默认推荐纳入多重备注与说明专属区的字段 Key
export const defaultMemoFields: string[] = [
  'Comments', 'U_Comments', 'U_Remark', 'U_PackMemo', 'U_DELIVER', 'U_PAGREQ', 'U_saleass'
]

// 全球主流货币符号字典 (按币种代码精准映射)
export const currencySymbols: Record<string, string> = {
  USD: '$',
  EUR: '€',
  GBP: '£',
  JPY: '¥',
  RMB: '¥',
  CNY: '¥',
  HKD: 'HK$',
  TWD: 'NT$',
  AUD: 'A$',
  CAD: 'C$',
  SGD: 'S$'
}

// 格式化货币符号辅助函数
export const getCurrencySymbol = (curCode: string = 'RMB'): string => {
  const normalized = curCode.trim().toUpperCase()
  return currencySymbols[normalized] || `${normalized} `
}
