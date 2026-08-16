import { ref, watch, onMounted } from 'vue'
import api from '../config/request'

export function useDocData(
  objectCodeRef: any,
  companyIdRef: any,
  parsedDataRef: any,
  loadTieredLayoutFromServer: any,
  getFieldEffectiveDisplayMode: any,
  currencySymbolRef: any
) {
  const metaData = ref<any>(null)
  const loadingMeta = ref(false)

// 异步加载 SAP 真实元数据 (CUFD / UFD1 / RTable / OSLP / OCTG / OHEM)
const loadObjectMetadata = async () => {
  const obj = objectCodeRef.value || parsedDataRef.value?.Object || 'CHORDR'
  loadingMeta.value = true
  try {
    const res = await api.get(`/metadata/objects/${obj}`, {
      params: { companyId: companyIdRef.value || 'DB_KCC' }
    })
    metaData.value = res.data.data
  } catch {
    metaData.value = null
  } finally {
    loadingMeta.value = false
  }
}

let currentLoadedObject = ''

const initMetadataAndLayout = async (force = false) => {
  const obj = objectCodeRef.value || parsedDataRef.value?.Object || 'CHORDR'
  if (!force && currentLoadedObject === obj && metaData.value) {
    return
  }
  currentLoadedObject = obj
  await Promise.all([
    loadObjectMetadata(),
    loadTieredLayoutFromServer()
  ])
}

onMounted(() => {
  initMetadataAndLayout()
})

watch(() => objectCodeRef.value, () => {
  initMetadataAndLayout(true)
})

// 默认隐藏的 UDO 底层技术元数据与冗余审计字段
const DEFAULT_HIDDEN_FIELDS = [
  'odata.metadata', 'Period', 'Instance', 'Series', 'Handwrtten',
  'RequestStatus', 'Status', 'Canceled', 'Object', 'LogInst',
  'UserSign', 'UserSign2', 'Transfered', 'CreateDate', 'CreateTime',
  'UpdateDate', 'UpdateTime', 'DataSource', 'NaturalPer', 'DPPStatus',
  'DocTime', 'DocDate', 'DocDueDate', 'TaxDate', 'DocEntry', 'DocNum', 'EncryptIV',
  'U_PriceMode', 'PriceMode'
]

const childTechColumns = new Set(['DocEntry', 'EncryptIV', 'LogInst', 'Object', 'VisOrder'])

// 常见标准系统字段翻译字典（兜底基准）
const SYSTEM_FIELDS_DICT: Record<string, string> = {
  DocEntry: '单据内部标识 (DocEntry)',
  DocNum: '单据编号 (DocNum)',
  CardCode: '业务伙伴/客户代码',
  U_CardCode: '业务伙伴/客户代码',
  CardName: '业务伙伴/客户名称',
  U_CardName: '业务伙伴/客户名称',
  DocDate: '过账日期',
  U_DocDate: '过账日期',
  DocDueDate: '交货/到期日',
  U_DocDueDate: '交货/到期日',
  TaxDate: '单据日期',
  U_TaxDate: '单据日期',
  DocTotal: '单据总金额',
  U_DocTotal: '单据总金额',
  DocCur: '结算币种',
  U_DocCur: '结算币种',
  Comments: '单据备注',
  U_Comments: '单据备注',
  Creator: '制单人工号',
  UserSign: '操作员标识',
  CreateDate: '创建日期',
  CreateTime: '创建时间',
  UpdateDate: '更新日期',
  UpdateTime: '更新时间',
  Status: '单据状态',
  Canceled: '是否作废',
  Object: '业务对象代码',
  U_SoType: '销售订单类型',
  U_SlpCode: '销售员',
  U_GroupNum: '付款条件',
  U_saleass: '业务助理',
  U_PAGREQ: '纸箱要求',
  U_Close: '关闭行',
  LineCls: '关闭行'
}

// 格式化字段中文标签
const getFieldLabel = (key: string, childTableId?: string): string => {
  if (childTableId && metaData.value?.childTableFields) {
    const childMap = metaData.value.childTableFields[childTableId]
    if (childMap) {
      if (childMap[key]?.description) return childMap[key].description
      const stripped = key.startsWith('U_') ? key.substring(2) : key
      if (childMap[stripped]?.description) return childMap[stripped].description
    }
  }

  if (metaData.value?.headerFields) {
    if (metaData.value.headerFields[key]?.description) return metaData.value.headerFields[key].description
    const stripped = key.startsWith('U_') ? key.substring(2) : key
    if (metaData.value.headerFields[stripped]?.description) return metaData.value.headerFields[stripped].description
  }

  if (SYSTEM_FIELDS_DICT[key]) return SYSTEM_FIELDS_DICT[key]
  const stripped = key.startsWith('U_') ? key.substring(2) : key
  if (SYSTEM_FIELDS_DICT[stripped]) return SYSTEM_FIELDS_DICT[stripped]

  return key
}

// 格式化字段值
const formatFieldValue = (key: string, val: any, childTableId?: string): { display: string; isTranslated: boolean; rawVal: any } => {
  if (val === null || val === undefined || val === '') return { display: '-', isTranslated: false, rawVal: val }

  const strVal = String(val).trim()
  let validMap: Record<string, string> | null = null

  if (childTableId && metaData.value?.childTableFields) {
    const childMap = metaData.value.childTableFields[childTableId]
    const stripped = key.startsWith('U_') ? key.substring(2) : key
    validMap = childMap?.[key]?.validValues || childMap?.[stripped]?.validValues || null
  }
  
  if (!validMap && metaData.value?.headerFields) {
    const stripped = key.startsWith('U_') ? key.substring(2) : key
    validMap = metaData.value.headerFields?.[key]?.validValues || metaData.value.headerFields?.[stripped]?.validValues || null
  }

  if (!validMap && metaData.value?.childTableFields) {
    const stripped = key.startsWith('U_') ? key.substring(2) : key
    for (const cMap of Object.values(metaData.value.childTableFields) as any[]) {
      if (cMap?.[key]?.validValues && Object.keys(cMap[key].validValues).length > 0) {
        validMap = cMap[key].validValues
        break
      }
      if (cMap?.[stripped]?.validValues && Object.keys(cMap[stripped].validValues).length > 0) {
        validMap = cMap[stripped].validValues
        break
      }
    }
  }

  // 4. 若仍未匹配，针对 ExpnsCode / SlpCode / GroupNum / VatGroup 等包含性命名字段进行全元数据字典回退匹配
  if (!validMap && metaData.value) {
    const cleanKey = key.startsWith('U_') ? key.substring(2) : key
    const allFields = [
      ...Object.entries(metaData.value.headerFields || {}),
      ...Object.values(metaData.value.childTableFields || {}).flatMap(m => Object.entries(m || {}))
    ]
    for (const [fName, fMeta] of allFields) {
      if (fMeta?.validValues && Object.keys(fMeta.validValues).length > 0) {
        const cleanFName = fName.startsWith('U_') ? fName.substring(2) : fName
        if (
          (cleanKey.includes('ExpnsCode') && cleanFName.includes('ExpnsCode')) ||
          (cleanKey.includes('SlpCode') && cleanFName.includes('SlpCode')) ||
          (cleanKey.includes('GroupNum') && cleanFName.includes('GroupNum')) ||
          (cleanKey.includes('VatGroup') && cleanFName.includes('VatGroup'))
        ) {
          validMap = fMeta.validValues
          break
        }
      }
    }
  }

  const effMode = getFieldEffectiveDisplayMode(key)

  if (validMap && validMap[strVal]) {
    const desc = validMap[strVal]
    let display = `${desc} (${strVal})`
    if (effMode === 'NameOnly') {
      display = desc
    } else if (effMode === 'CodeOnly') {
      display = strVal
    } else {
      display = `${desc} (${strVal})`
    }
    return {
      display,
      isTranslated: true,
      rawVal: val
    }
  }

  if (key === 'U_Close' || key === 'LineCls') {
    const isY = strVal.toUpperCase() === 'Y'
    const desc = isY ? '是' : '否'
    const code = isY ? 'Y' : 'N'
    let display = `${desc} (${code})`
    if (effMode === 'NameOnly') {
      display = desc
    } else if (effMode === 'CodeOnly') {
      display = code
    } else {
      display = `${desc} (${code})`
    }
    return { display, isTranslated: true, rawVal: val }
  }

  const lKey = key.toLowerCase()
  if (lKey.includes('total') || lKey.includes('price') || lKey.includes('vat') || lKey.includes('basicp') || lKey.includes('amount') || lKey === 'doctotal') {
    const num = parseFloat(strVal)
    if (!isNaN(num)) {
      return {
        display: currencySymbolRef.value + ' ' + num.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 4 }),
        isTranslated: false,
        rawVal: val
      }
    }
  }

  if (lKey.includes('rate') || lKey.includes('percent')) {
    const num = parseFloat(strVal)
    if (!isNaN(num)) {
      return { display: String(num), isTranslated: false, rawVal: val }
    }
  }

  if (lKey.includes('date') && typeof strVal === 'string' && strVal.includes('T')) {
    return { display: strVal.split('T')[0], isTranslated: false, rawVal: val }
  }

  return { display: String(val), isTranslated: false, rawVal: val }
}



  return {
    metaData,
    loadingMeta,
    initMetadataAndLayout,
    DEFAULT_HIDDEN_FIELDS,
    childTechColumns,
    SYSTEM_FIELDS_DICT,
    getFieldLabel,
    formatFieldValue
  }
}
