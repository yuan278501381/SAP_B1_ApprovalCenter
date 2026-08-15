<script setup lang="ts">
import { computed, ref, onMounted, watch } from 'vue'
import axios from 'axios'
import {
  Code2,
  Building2,
  FileText,
  Layers,
  Tag,
  Search,
  SlidersHorizontal,
  Eye,
  EyeOff,
  RotateCcw,
  Check,
  X,
  GripVertical,
  Lock,
  Unlock,
  Move,
  Pin,
  PinOff,
  Plus,
  Trash2,
  Save,
  ShieldCheck,
  ArrowRightLeft,
  Settings2,
  CheckCheck
} from 'lucide-vue-next'

const API_BASE = import.meta.env.VITE_API_BASE || '/api/v1'
const api = axios.create({ baseURL: API_BASE })

// 拦截器自动注入当前操作员与 TraceID
api.interceptors.request.use((config) => {
  const user = localStorage.getItem('sap_b1_approval_user') || 'manager'
  config.headers['X-Approval-User'] = user
  config.headers['X-Approval-User-Name'] = user
  if (!config.headers['X-Trace-Id']) {
    config.headers['X-Trace-Id'] = 'trace_fe_' + Math.random().toString(36).substring(2, 9)
  }
  return config
})

const props = withDefaults(
  defineProps<{
    rawJson: string
    objectCode?: string
    companyId?: string
  }>(),
  {
    companyId: 'DB_KCC'
  }
)

// 当前激活的内容 Tab (默认第一个子表或主表属性)
const activeDocTab = ref<string>('tab_table_0')

const metaData = ref<any>(null)
const loadingMeta = ref(false)
const searchUdf = ref('')
const showSystemFields = ref(false)

// 当前操作员与 Admin 权限判断
const currentUser = computed(() => localStorage.getItem('sap_b1_approval_user') || 'manager')
const isAdmin = computed(() => {
  const u = currentUser.value.toLowerCase()
  return u === 'admin' || u === 'manager'
})

// 主表直接拖拽模式
const isHeaderReorderMode = ref(false)

// 各子表直接拖拽排序模式映射 (tableKey -> boolean)
const tableReorderModes = ref<Record<string, boolean>>({})

// ===================== 世界级双栏穿梭定制抽屉 (Transfer Drawer) =====================
const showTransferDrawer = ref(false)
const activeTransferTab = ref<string>('header') // 'header' 或具体子表集合 Key
const transferSearchLeft = ref('')
const transferSearchRight = ref('')
const isSavingLayout = ref(false)
const drawerToast = ref<{ text: string; type: 'success' | 'error' } | null>(null)
const isCustomizedByMe = ref(false)

const showDrawerToast = (text: string, type: 'success' | 'error' = 'success') => {
  drawerToast.value = { text, type }
  setTimeout(() => {
    drawerToast.value = null
  }, 3500)
}

const parsedData = computed(() => {
  if (!props.rawJson) return {}
  try {
    return JSON.parse(props.rawJson)
  } catch {
    return {}
  }
})

// 单据表头币种提取与动态货币符号 (严格按表头币种)
const docCurrency = computed(() => {
  const cur = (parsedData.value?.U_DocCur || parsedData.value?.DocCur || parsedData.value?.DocCurrency || 'RMB').toString().trim().toUpperCase()
  return cur
})

const currencySymbol = computed(() => {
  const cur = docCurrency.value
  if (cur === 'USD') return '$'
  if (cur === 'EUR') return '€'
  if (cur === 'GBP') return '£'
  if (cur === 'JPY') return '¥'
  if (cur === 'RMB' || cur === 'CNY') return '¥'
  return cur + ' '
})

// 默认推荐固定在顶部概览卡片的核心高频字段
const DEFAULT_PINNED_FIELDS = [
  'U_DocDate', 'DocDate', 'U_DocDueDate', 'DocDueDate', 'Creator',
  'U_DocCur', 'DocCur', 'U_SoType', 'U_SlpCode', 'U_GroupNum', 'U_saleass',
  'U_DELIVER', 'U_PAGREQ', 'U_Comments', 'Comments'
]

// 用户自定义固定在顶部概览区的字段 Key 数组 (持久化存储)
const pinnedFieldKeys = ref<string[]>(
  JSON.parse(localStorage.getItem(`sap_b1_pinned_${props.objectCode || 'CHORDR'}`) || JSON.stringify(DEFAULT_PINNED_FIELDS))
)

const isFieldPinned = (key: string) => {
  const stripped = key.startsWith('U_') ? key.substring(2) : key
  return pinnedFieldKeys.value.includes(key) || pinnedFieldKeys.value.includes(stripped) || pinnedFieldKeys.value.includes('U_' + stripped)
}

const togglePinField = (key: string) => {
  const stripped = key.startsWith('U_') ? key.substring(2) : key
  const foundIdx = pinnedFieldKeys.value.findIndex(k => k === key || k === stripped || k === 'U_' + stripped)
  if (foundIdx > -1) {
    pinnedFieldKeys.value.splice(foundIdx, 1)
  } else {
    pinnedFieldKeys.value.push(key)
  }
  syncLocalLayoutCache()
}

const resetPinnedFields = () => {
  pinnedFieldKeys.value = [...DEFAULT_PINNED_FIELDS]
  syncLocalLayoutCache()
}

// 1. 主表自定义隐藏与排序状态 (持久化存储)
const userHiddenFields = ref<string[]>(
  JSON.parse(localStorage.getItem(`sap_b1_hidden_${props.objectCode || 'CHORDR'}_header`) || '[]')
)
const headerFieldOrder = ref<string[]>(
  JSON.parse(localStorage.getItem(`sap_b1_order_${props.objectCode || 'CHORDR'}_header`) || '[]')
)

// 2. 子表自定义隐藏与列排序映射 (持久化存储)
const collectionHiddenCols = ref<Record<string, string[]>>(
  JSON.parse(localStorage.getItem(`sap_b1_col_hidden_${props.objectCode || 'CHORDR'}`) || '{}')
)
const collectionColOrders = ref<Record<string, string[]>>(
  JSON.parse(localStorage.getItem(`sap_b1_col_order_${props.objectCode || 'CHORDR'}`) || '{}')
)

// 同步写入本地 LocalStorage
const syncLocalLayoutCache = () => {
  const obj = props.objectCode || 'CHORDR'
  localStorage.setItem(`sap_b1_pinned_${obj}`, JSON.stringify(pinnedFieldKeys.value))
  localStorage.setItem(`sap_b1_hidden_${obj}_header`, JSON.stringify(userHiddenFields.value))
  localStorage.setItem(`sap_b1_order_${obj}_header`, JSON.stringify(headerFieldOrder.value))
  localStorage.setItem(`sap_b1_col_hidden_${obj}`, JSON.stringify(collectionHiddenCols.value))
  localStorage.setItem(`sap_b1_col_order_${obj}`, JSON.stringify(collectionColOrders.value))
}

// 异步从服务器加载分层 UI 配置 (优先个人专属偏好，其次全公司默认)
const loadTieredLayoutFromServer = async () => {
  const obj = props.objectCode || parsedData.value?.Object || 'CHORDR'
  try {
    const res = await api.get('/ui-layouts', {
      params: {
        companyId: props.companyId || 'DB_KCC',
        objectCode: obj
      },
      headers: {
        'X-Approval-User': currentUser.value
      }
    })
    if (res.data?.success && res.data?.data) {
      isCustomizedByMe.value = !!res.data.data.isUserCustomized
      const layoutJsonStr = res.data.data.effectiveLayoutJson
      if (layoutJsonStr && layoutJsonStr !== '{}') {
        const parsed = JSON.parse(layoutJsonStr)
        if (parsed.pinnedKeys && Array.isArray(parsed.pinnedKeys)) {
          pinnedFieldKeys.value = parsed.pinnedKeys
        }
        if (parsed.hiddenHeaderKeys && Array.isArray(parsed.hiddenHeaderKeys)) {
          userHiddenFields.value = parsed.hiddenHeaderKeys
        }
        if (parsed.headerOrder && Array.isArray(parsed.headerOrder)) {
          headerFieldOrder.value = parsed.headerOrder
        }
        if (parsed.colHiddenMap && typeof parsed.colHiddenMap === 'object') {
          collectionHiddenCols.value = parsed.colHiddenMap
        }
        if (parsed.colOrderMap && typeof parsed.colOrderMap === 'object') {
          collectionColOrders.value = parsed.colOrderMap
        }
        syncLocalLayoutCache()
      }
    }
  } catch {}
}

// 异步加载 SAP 真实元数据 (CUFD / UFD1 / RTable / OSLP / OCTG / OHEM)
const loadObjectMetadata = async () => {
  const obj = props.objectCode || parsedData.value?.Object || 'CHORDR'
  loadingMeta.value = true
  try {
    const res = await api.get(`/metadata/objects/${obj}`, {
      params: { companyId: props.companyId || 'DB_KCC' }
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
  const obj = props.objectCode || parsedData.value?.Object || 'CHORDR'
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

watch(() => props.objectCode, () => {
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
  } else if (metaData.value?.headerFields) {
    const stripped = key.startsWith('U_') ? key.substring(2) : key
    validMap = metaData.value.headerFields?.[key]?.validValues || metaData.value.headerFields?.[stripped]?.validValues || null
  }

  if (validMap && validMap[strVal]) {
    return {
      display: `${validMap[strVal]} (${strVal})`,
      isTranslated: true,
      rawVal: val
    }
  }

  if (key === 'U_Close' || key === 'LineCls') {
    if (strVal.toUpperCase() === 'Y') return { display: '是 (Y)', isTranslated: true, rawVal: val }
    if (strVal.toUpperCase() === 'N') return { display: '否 (N)', isTranslated: true, rawVal: val }
  }

  const lKey = key.toLowerCase()
  if (lKey.includes('total') || lKey.includes('price') || lKey.includes('vat') || lKey.includes('basicp') || lKey.includes('amount') || lKey === 'doctotal') {
    const num = parseFloat(strVal)
    if (!isNaN(num)) {
      return {
        display: currencySymbol.value + ' ' + num.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 4 }),
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

// 提取顶部概览卡片动态钉选字段列表
const topPinnedFields = computed(() => {
  const data = parsedData.value
  const result: { key: string; label: string; formatted: { display: string; isTranslated: boolean; rawVal: any } }[] = []
  const renderedKeys = new Set<string>()
  const baseExcluded = new Set(['U_CardName', 'CardName', 'U_CardCode', 'CardCode', 'DocTotal', 'U_DocTotal', 'DocNum', 'DocEntry', 'U_Comments', 'Comments'])

  pinnedFieldKeys.value.forEach(k => {
    if (baseExcluded.has(k)) return
    
    let actualVal = data[k]
    let actualKey = k
    if (actualVal === undefined) {
      const alt = k.startsWith('U_') ? k.substring(2) : ('U_' + k)
      if (data[alt] !== undefined) {
        actualVal = data[alt]
        actualKey = alt
      }
    }

    if (actualVal !== undefined && actualVal !== null && actualVal !== '' && !renderedKeys.has(actualKey)) {
      renderedKeys.add(actualKey)
      result.push({
        key: actualKey,
        label: getFieldLabel(actualKey),
        formatted: formatFieldValue(actualKey, actualVal)
      })
    }
  })

  return result
})

// 提取全部子表集合与预计算单元格 (0 运行时开销，杜绝内联函数重绘)
const processedCollections = computed(() => {
  const data = parsedData.value
  const result: {
    key: string
    label: string
    tableId: string
    allColumns: string[]
    visibleColumns: string[]
    columnLabels: Record<string, string>
    processedRows: Array<{
      rIdx: number
      cells: Record<string, { display: string; isTranslated: boolean; isNum: boolean; isItemCode: boolean; isClosed: boolean }>
    }>
  }[] = []

  for (const [k, v] of Object.entries(data)) {
    if (Array.isArray(v) && v.length > 0) {
      let tableId = '@CH_ORDR_1'
      let label = '型号明细子表 (Line Items)'

      if (k.includes('1Collection') || k === 'DocumentLines') {
        tableId = '@CH_ORDR_1'
        label = '型号明细表 (Line Items)'
      } else if (k.includes('3Collection')) {
        tableId = '@CH_ORDR_3'
        label = '工序费用表 (Operations & Expenses)'
      } else if (k.includes('2Collection')) {
        tableId = '@CH_ORDR_2'
        label = '子表集合二'
      } else {
        label = `子表集合 (${k})`
      }

      const allColsSet = new Set<string>()
      v.forEach((row) => {
        if (typeof row === 'object' && row !== null) {
          Object.keys(row).forEach((col) => allColsSet.add(col))
        }
      })

      const preferred = ['LineId', 'U_Close', 'LineCls', 'U_ItemCode', 'ItemCode', 'U_ItemName', 'ItemName', 'U_Quantity', 'Quantity', 'U_PriceAfVat', 'U_PriceBfDisc', 'Price', 'U_LineTotal', 'U_GTotal', 'LineTotal', 'U_length', 'U_basicp', 'U_season', 'U_Memo']
      const defaultSortedCols = Array.from(allColsSet).sort((a, b) => {
        const idxA = preferred.indexOf(a)
        const idxB = preferred.indexOf(b)
        if (idxA !== -1 && idxB !== -1) return idxA - idxB
        if (idxA !== -1) return -1
        if (idxB !== -1) return 1
        return a.localeCompare(b)
      })

      const userColOrder = collectionColOrders.value[k]
      let effectiveCols = defaultSortedCols
      if (userColOrder && userColOrder.length > 0) {
        const orderMap = new Map(userColOrder.map((col, idx) => [col, idx]))
        effectiveCols = [...defaultSortedCols].sort((a, b) => {
          const posA = orderMap.has(a) ? orderMap.get(a)! : 9999
          const posB = orderMap.has(b) ? orderMap.get(b)! : 9999
          return posA - posB
        })
      }

      const hiddenList = collectionHiddenCols.value[k] || []
      const visibleCols = effectiveCols.filter(col => {
        if (hiddenList.includes(col)) return false
        if (!showSystemFields.value && childTechColumns.has(col)) return false
        return true
      })

      const colLabels: Record<string, string> = {}
      effectiveCols.forEach(cKey => {
        colLabels[cKey] = getFieldLabel(cKey, tableId)
      })

      // 预计算全部行与单元格
      const processedRows = v.map((row: any, rIdx: number) => {
        const cells: Record<string, { display: string; isTranslated: boolean; isNum: boolean; isItemCode: boolean; isClosed: boolean }> = {}
        visibleCols.forEach(col => {
          const raw = row[col]
          const formatted = formatFieldValue(col, raw, tableId)
          const lCol = col.toLowerCase()
          const isNum = lCol.includes('total') || lCol.includes('price') || lCol.includes('quantity') || lCol.includes('vat')
          const isItemCode = col.includes('ItemCode')
          const isClosed = (col === 'U_Close' || col === 'LineCls') && (String(raw).toUpperCase() === 'Y')
          cells[col] = {
            display: formatted.display,
            isTranslated: formatted.isTranslated,
            isNum,
            isItemCode,
            isClosed
          }
        })
        return { rIdx, cells }
      })

      result.push({
        key: k,
        label,
        tableId,
        allColumns: effectiveCols,
        visibleColumns: visibleCols,
        columnLabels: colLabels,
        processedRows
      })
    }
  }

  return result
})

// 提取主表全部属性
const allHeaderFieldsList = computed(() => {
  const data = parsedData.value
  const fieldsMap = new Map<string, { key: string; label: string; isSystem: boolean; formatted: { display: string; isTranslated: boolean; rawVal: any } }>()

  const excludeKeys = new Set(['EncryptIV'])
  for (const k of Object.keys(data)) {
    if (k.endsWith('Collection') || k.endsWith('Lines') || Array.isArray(data[k])) {
      excludeKeys.add(k)
    }
  }

  for (const [k, v] of Object.entries(data)) {
    if (!excludeKeys.has(k) && !Array.isArray(v) && typeof v !== 'object') {
      const label = getFieldLabel(k)
      const formatted = formatFieldValue(k, v)
      const isSystem = DEFAULT_HIDDEN_FIELDS.includes(k)

      fieldsMap.set(k, {
        key: k,
        label,
        isSystem,
        formatted
      })
    }
  }

  const keys = Array.from(fieldsMap.keys())
  if (headerFieldOrder.value && headerFieldOrder.value.length > 0) {
    const orderMap = new Map(headerFieldOrder.value.map((k, idx) => [k, idx]))
    keys.sort((a, b) => {
      const posA = orderMap.has(a) ? orderMap.get(a)! : 9999
      const posB = orderMap.has(b) ? orderMap.get(b)! : 9999
      return posA - posB
    })
  }

  return keys.map(k => fieldsMap.get(k)!).filter(Boolean)
})

// 主表网格实际显示的字段
const headerUdfFields = computed(() => {
  return allHeaderFieldsList.value.filter(f => {
    if (userHiddenFields.value.includes(f.key)) return false
    if (!showSystemFields.value && f.isSystem) return false
    if (searchUdf.value.trim()) {
      const q = searchUdf.value.trim().toLowerCase()
      if (!f.key.toLowerCase().includes(q) && !f.label.toLowerCase().includes(q) && !f.formatted.display.toLowerCase().includes(q)) {
        return false
      }
    }
    return true
  })
})

// 拖拽 Key 追踪
const draggingColKey = ref<string | null>(null)
const draggingTableKey = ref<string | null>(null)
const draggingHeaderKey = ref<string | null>(null)
const draggingPinnedKey = ref<string | null>(null)
const isDragOverTopSummary = ref(false)

// 表格表头直接拖拽排序控制
const toggleTableReorderMode = (tableKey: string) => {
  tableReorderModes.value[tableKey] = !tableReorderModes.value[tableKey]
}

const onDirectColDragStart = (tableKey: string, colKey: string, e: DragEvent) => {
  if (!tableReorderModes.value[tableKey]) return
  draggingColKey.value = colKey
  draggingTableKey.value = tableKey
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', JSON.stringify({ type: 'table-col', tableKey, colKey }))
  }
}

const onDirectColDragOver = (tableKey: string, e: DragEvent) => {
  if (!tableReorderModes.value[tableKey] || draggingTableKey.value !== tableKey) return
  e.preventDefault()
  if (e.dataTransfer) {
    e.dataTransfer.dropEffect = 'move'
  }
}

const onDirectColDrop = (tableKey: string, targetColKey: string) => {
  if (!tableReorderModes.value[tableKey] || !draggingColKey.value || draggingColKey.value === targetColKey) {
    draggingColKey.value = null
    draggingTableKey.value = null
    return
  }

  const currentCols = collectionColOrders.value[tableKey] || processedCollections.value.find(c => c.key === tableKey)?.allColumns || []
  const cols = [...currentCols]
  const fromIdx = cols.indexOf(draggingColKey.value)
  const toIdx = cols.indexOf(targetColKey)

  if (fromIdx > -1 && toIdx > -1) {
    cols.splice(fromIdx, 1)
    cols.splice(toIdx, 0, draggingColKey.value)
    collectionColOrders.value = {
      ...collectionColOrders.value,
      [tableKey]: cols
    }
    syncLocalLayoutCache()
  }

  draggingColKey.value = null
  draggingTableKey.value = null
}

const onDirectColDragEnd = () => {
  draggingColKey.value = null
  draggingTableKey.value = null
}

// 主表属性卡片拖拽逻辑
const onHeaderCardDragStart = (key: string, e: DragEvent) => {
  if (!isHeaderReorderMode.value) return
  draggingHeaderKey.value = key
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', JSON.stringify({ type: 'header-field', key }))
  }
}

const onHeaderCardDragOver = (e: DragEvent) => {
  if (!isHeaderReorderMode.value || !draggingHeaderKey.value) return
  e.preventDefault()
  if (e.dataTransfer) {
    e.dataTransfer.dropEffect = 'move'
  }
}

const onHeaderCardDrop = (targetKey: string) => {
  if (!isHeaderReorderMode.value || !draggingHeaderKey.value || draggingHeaderKey.value === targetKey) {
    draggingHeaderKey.value = null
    return
  }

  const currentOrder = headerFieldOrder.value.length > 0
    ? [...headerFieldOrder.value]
    : allHeaderFieldsList.value.map(f => f.key)

  const fromIdx = currentOrder.indexOf(draggingHeaderKey.value)
  const toIdx = currentOrder.indexOf(targetKey)

  if (fromIdx > -1 && toIdx > -1) {
    currentOrder.splice(fromIdx, 1)
    currentOrder.splice(toIdx, 0, draggingHeaderKey.value)
    headerFieldOrder.value = currentOrder
    syncLocalLayoutCache()
  }

  draggingHeaderKey.value = null
}

const onHeaderCardDragEnd = () => {
  draggingHeaderKey.value = null
}

// 顶部概览拖拽置顶逻辑
const onTopSummaryDragOver = (e: DragEvent) => {
  if (!isHeaderReorderMode.value) return
  e.preventDefault()
  isDragOverTopSummary.value = true
}

const onTopSummaryDragLeave = () => {
  isDragOverTopSummary.value = false
}

const onTopSummaryDrop = (_e: DragEvent) => {
  isDragOverTopSummary.value = false
  if (!isHeaderReorderMode.value || !draggingHeaderKey.value) return
  const key = draggingHeaderKey.value
  if (!isFieldPinned(key)) {
    togglePinField(key)
  }
  draggingHeaderKey.value = null
}

// 顶部置顶标签上下/左右拖拽调序
const onPinnedDragStart = (key: string, e: DragEvent) => {
  if (!isHeaderReorderMode.value) return
  draggingPinnedKey.value = key
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', JSON.stringify({ type: 'pinned-tag', key }))
  }
}

const onPinnedDragOver = (e: DragEvent) => {
  if (!isHeaderReorderMode.value || !draggingPinnedKey.value) return
  e.preventDefault()
}

const onPinnedDrop = (targetKey: string) => {
  if (!isHeaderReorderMode.value || !draggingPinnedKey.value || draggingPinnedKey.value === targetKey) {
    draggingPinnedKey.value = null
    return
  }

  const list = [...pinnedFieldKeys.value]
  const fromIdx = list.indexOf(draggingPinnedKey.value)
  const toIdx = list.indexOf(targetKey)

  if (fromIdx > -1 && toIdx > -1) {
    list.splice(fromIdx, 1)
    list.splice(toIdx, 0, draggingPinnedKey.value)
    pinnedFieldKeys.value = list
    syncLocalLayoutCache()
  }

  draggingPinnedKey.value = null
}

const onPinnedDragEnd = () => {
  draggingPinnedKey.value = null
}

// ===================== 服务器分层配置保存与重置 =====================
const getLayoutPayloadJson = () => {
  return JSON.stringify({
    pinnedKeys: pinnedFieldKeys.value,
    hiddenHeaderKeys: userHiddenFields.value,
    headerOrder: headerFieldOrder.value,
    colHiddenMap: collectionHiddenCols.value,
    colOrderMap: collectionColOrders.value
  })
}

const saveUserLayoutToServer = async () => {
  const obj = props.objectCode || parsedData.value?.Object || 'CHORDR'
  isSavingLayout.value = true
  try {
    await api.post('/ui-layouts', {
      companyId: props.companyId || 'DB_KCC',
      objectCode: obj,
      layoutJson: getLayoutPayloadJson()
    }, {
      headers: { 'X-Approval-User': currentUser.value }
    })
    syncLocalLayoutCache()
    isCustomizedByMe.value = true
    showDrawerToast('个人专属偏好已成功保存并同步至服务器！', 'success')
  } catch (err: any) {
    showDrawerToast(err.response?.data?.message || '保存个人偏好失败', 'error')
  } finally {
    isSavingLayout.value = false
  }
}

const saveGlobalDefaultLayoutToServer = async () => {
  if (!isAdmin.value) return
  const obj = props.objectCode || parsedData.value?.Object || 'CHORDR'
  isSavingLayout.value = true
  try {
    await api.post('/ui-layouts/global', {
      companyId: props.companyId || 'DB_KCC',
      objectCode: obj,
      layoutJson: getLayoutPayloadJson()
    }, {
      headers: { 'X-Approval-User': currentUser.value }
    })
    syncLocalLayoutCache()
    showDrawerToast('全公司全局默认配置已成功发布并同步至服务器！后续全员默认继承。', 'success')
  } catch (err: any) {
    showDrawerToast(err.response?.data?.message || '发布全局配置失败', 'error')
  } finally {
    isSavingLayout.value = false
  }
}

const resetToCompanyDefaultLayout = async () => {
  const obj = props.objectCode || parsedData.value?.Object || 'CHORDR'
  isSavingLayout.value = true
  try {
    await api.delete('/ui-layouts', {
      params: {
        companyId: props.companyId || 'DB_KCC',
        objectCode: obj
      },
      headers: { 'X-Approval-User': currentUser.value }
    })
    await loadTieredLayoutFromServer()
    showDrawerToast('已恢复为全公司默认配置！', 'success')
  } catch (err: any) {
    showDrawerToast('重置失败', 'error')
  } finally {
    isSavingLayout.value = false
  }
}

// ===================== 双栏穿梭抽屉数据计算与操作 =====================
const transferLeftItems = computed(() => {
  const q = transferSearchLeft.value.trim().toLowerCase()
  if (activeTransferTab.value === 'header') {
    return allHeaderFieldsList.value.map(f => {
      const isAdded = !userHiddenFields.value.includes(f.key)
      return {
        key: f.key,
        label: f.label,
        isSystem: f.isSystem,
        isAdded,
        sampleVal: f.formatted.display
      }
    }).filter(item => {
      if (!q) return true
      return item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q) || item.sampleVal?.toLowerCase().includes(q)
    })
  } else {
    const tableKey = activeTransferTab.value
    const coll = processedCollections.value.find(c => c.key === tableKey)
    if (!coll) return []
    const hiddenList = collectionHiddenCols.value[tableKey] || []
    return coll.allColumns.map(colKey => {
      const isAdded = !hiddenList.includes(colKey)
      return {
        key: colKey,
        label: coll.columnLabels[colKey] || colKey,
        isSystem: childTechColumns.has(colKey),
        isAdded,
        sampleVal: coll.processedRows[0]?.cells[colKey]?.display || '-'
      }
    }).filter(item => {
      if (!q) return true
      return item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q)
    })
  }
})

const transferRightItems = computed(() => {
  const q = transferSearchRight.value.trim().toLowerCase()
  if (activeTransferTab.value === 'header') {
    return allHeaderFieldsList.value
      .filter(f => !userHiddenFields.value.includes(f.key))
      .map(f => ({
        key: f.key,
        label: f.label,
        isPinned: isFieldPinned(f.key),
        sampleVal: f.formatted.display
      }))
      .filter(item => {
        if (!q) return true
        return item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q)
      })
  } else {
    const tableKey = activeTransferTab.value
    const coll = processedCollections.value.find(c => c.key === tableKey)
    if (!coll) return []
    const hiddenList = collectionHiddenCols.value[tableKey] || []
    return coll.allColumns
      .filter(colKey => !hiddenList.includes(colKey))
      .map(colKey => ({
        key: colKey,
        label: coll.columnLabels[colKey] || colKey,
        isPinned: false,
        sampleVal: coll.processedRows[0]?.cells[colKey]?.display || '-'
      }))
      .filter(item => {
        if (!q) return true
        return item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q)
      })
  }
})

const transferAddItem = (key: string) => {
  if (activeTransferTab.value === 'header') {
    userHiddenFields.value = userHiddenFields.value.filter(k => k !== key)
  } else {
    const tableKey = activeTransferTab.value
    const currentHidden = collectionHiddenCols.value[tableKey] || []
    collectionHiddenCols.value = {
      ...collectionHiddenCols.value,
      [tableKey]: currentHidden.filter(k => k !== key)
    }
  }
  syncLocalLayoutCache()
}

const transferRemoveItem = (key: string) => {
  if (activeTransferTab.value === 'header') {
    if (!userHiddenFields.value.includes(key)) {
      userHiddenFields.value.push(key)
    }
  } else {
    const tableKey = activeTransferTab.value
    const currentHidden = collectionHiddenCols.value[tableKey] || []
    if (!currentHidden.includes(key)) {
      collectionHiddenCols.value = {
        ...collectionHiddenCols.value,
        [tableKey]: [...currentHidden, key]
      }
    }
  }
  syncLocalLayoutCache()
}

const transferAddAll = () => {
  if (activeTransferTab.value === 'header') {
    userHiddenFields.value = []
  } else {
    const tableKey = activeTransferTab.value
    collectionHiddenCols.value = {
      ...collectionHiddenCols.value,
      [tableKey]: []
    }
  }
  syncLocalLayoutCache()
}

const transferRemoveAll = () => {
  if (activeTransferTab.value === 'header') {
    userHiddenFields.value = allHeaderFieldsList.value.map(f => f.key)
  } else {
    const tableKey = activeTransferTab.value
    const coll = processedCollections.value.find(c => c.key === tableKey)
    if (coll) {
      collectionHiddenCols.value = {
        ...collectionHiddenCols.value,
        [tableKey]: [...coll.allColumns]
      }
    }
  }
  syncLocalLayoutCache()
}

// 抽屉右栏拖拽调序
const draggingDrawerKey = ref<string | null>(null)

const onDrawerDragStart = (key: string, e: DragEvent) => {
  draggingDrawerKey.value = key
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', key)
  }
}

const onDrawerDragOver = (e: DragEvent) => {
  if (!draggingDrawerKey.value) return
  e.preventDefault()
}

const onDrawerDrop = (targetKey: string) => {
  if (!draggingDrawerKey.value || draggingDrawerKey.value === targetKey) {
    draggingDrawerKey.value = null
    return
  }

  if (activeTransferTab.value === 'header') {
    const currentOrder = headerFieldOrder.value.length > 0
      ? [...headerFieldOrder.value]
      : allHeaderFieldsList.value.map(f => f.key)
    const fromIdx = currentOrder.indexOf(draggingDrawerKey.value)
    const toIdx = currentOrder.indexOf(targetKey)
    if (fromIdx > -1 && toIdx > -1) {
      currentOrder.splice(fromIdx, 1)
      currentOrder.splice(toIdx, 0, draggingDrawerKey.value)
      headerFieldOrder.value = currentOrder
      syncLocalLayoutCache()
    }
  } else {
    const tableKey = activeTransferTab.value
    const coll = processedCollections.value.find(c => c.key === tableKey)
    const currentOrder = collectionColOrders.value[tableKey] || coll?.allColumns || []
    const cols = [...currentOrder]
    const fromIdx = cols.indexOf(draggingDrawerKey.value)
    const toIdx = cols.indexOf(targetKey)
    if (fromIdx > -1 && toIdx > -1) {
      cols.splice(fromIdx, 1)
      cols.splice(toIdx, 0, draggingDrawerKey.value)
      collectionColOrders.value = {
        ...collectionColOrders.value,
        [tableKey]: cols
      }
      syncLocalLayoutCache()
    }
  }

  draggingDrawerKey.value = null
}

const onDrawerDragEnd = () => {
  draggingDrawerKey.value = null
}

const openTransferDrawer = (tabKey: string = 'header') => {
  activeTransferTab.value = tabKey
  transferSearchLeft.value = ''
  transferSearchRight.value = ''
  showTransferDrawer.value = true
}
</script>

<template>
  <div class="doc-viewer-container">
    <!-- 1. 顶部 Hero 看板 (常驻醒目总览) -->
    <div
      class="summary-card"
      :class="[
        isHeaderReorderMode ? 'summary-card-reorder' : '',
        isDragOverTopSummary ? 'summary-card-drag-over' : ''
      ]"
      @dragover="onTopSummaryDragOver"
      @dragleave="onTopSummaryDragLeave"
      @drop="onTopSummaryDrop"
    >
      <div class="summary-left">
        <!-- 客户名称 -->
        <div class="summary-item">
          <span class="s-label">客户 / 业务伙伴</span>
          <div class="s-val-highlight">
            <Building2 class="w-4 h-4 text-blue-500 mr-1.5" />
            <strong>{{ parsedData.U_CardName || parsedData.CardName || '-' }}</strong>
            <span v-if="parsedData.U_CardCode || parsedData.CardCode" class="sub-code">
              ({{ parsedData.U_CardCode || parsedData.CardCode }})
            </span>
          </div>
        </div>

        <!-- 动态固定在顶部的核心字段网格 -->
        <div class="summary-grid">
          <div
            v-for="pf in topPinnedFields"
            :key="pf.key"
            class="summary-sub-item pinned-tag"
            :class="[
              isHeaderReorderMode ? 'pinned-tag-draggable' : '',
              draggingPinnedKey === pf.key ? 'dragging-source' : ''
            ]"
            :draggable="isHeaderReorderMode"
            @dragstart="onPinnedDragStart(pf.key, $event)"
            @dragover="onPinnedDragOver"
            @drop.stop="onPinnedDrop(pf.key)"
            @dragend="onPinnedDragEnd"
          >
            <GripVertical v-if="isHeaderReorderMode" class="w-3 h-3 text-slate-400 cursor-move mr-0.5" />
            <span class="sub-label">{{ pf.label }}:</span>
            <span
              v-if="pf.formatted.isTranslated"
              class="sub-val badge-trans"
            >
              {{ pf.formatted.display }}
            </span>
            <span v-else class="sub-val font-semibold">
              {{ pf.formatted.display }}
            </span>

            <button
              v-if="isHeaderReorderMode"
              class="btn-unpin"
              @click.stop="togglePinField(pf.key)"
              title="取消置顶"
            >
              <X class="w-3 h-3 text-slate-400 hover:text-rose-600" />
            </button>
          </div>

          <div v-if="isHeaderReorderMode" class="drop-zone-placeholder">
            <Plus class="w-3.5 h-3.5 text-blue-600" />
            <span>拖拽下方卡片至此处即可固定到顶部</span>
          </div>
        </div>

        <!-- 单据备注 -->
        <div v-if="parsedData.U_Comments || parsedData.Comments" class="summary-comment">
          <FileText class="w-3.5 h-3.5 text-slate-400 shrink-0" />
          <span><strong>单据备注：</strong>{{ parsedData.U_Comments || parsedData.Comments }}</span>
        </div>
      </div>

      <!-- 单据金额与单号 -->
      <div class="summary-right">
        <span class="s-label">单据总金额 ({{ docCurrency }})</span>
        <div class="total-amount">
          {{ formatFieldValue('DocTotal', parsedData.U_DocTotal ?? parsedData.DocTotal ?? 0).display }}
        </div>
        <div v-if="parsedData.DocNum || parsedData.DocEntry" class="docnum-tag">
          单号: {{ parsedData.DocNum || parsedData.DocEntry }}
        </div>
        <button
          v-if="isHeaderReorderMode"
          class="btn-reset-pinned mt-2"
          @click="resetPinnedFields"
          title="恢复默认顶部字段"
        >
          <RotateCcw class="w-3 h-3 mr-1" />
          <span>恢复默认看板</span>
        </button>
      </div>
    </div>

    <!-- 2. 世界级 Tab 容器化导航栏 (Segmented Views) -->
    <div class="doc-tab-bar">
      <!-- 各子表 Tab -->
      <button
        v-for="(c, cIdx) in processedCollections"
        :key="c.key"
        :class="['doc-tab-btn', activeDocTab === ('tab_table_' + cIdx) ? 'active' : '']"
        @click="activeDocTab = 'tab_table_' + cIdx"
      >
        <Layers class="w-3.5 h-3.5 mr-1" />
        <span>{{ c.label }}</span>
        <span class="tab-count-badge">{{ c.processedRows.length }} 行</span>
      </button>

      <!-- 主表业务属性 Tab -->
      <button
        :class="['doc-tab-btn', activeDocTab === 'tab_header' ? 'active' : '']"
        @click="activeDocTab = 'tab_header'"
      >
        <Tag class="w-3.5 h-3.5 mr-1" />
        <span>主表全部属性</span>
        <span class="tab-count-badge">{{ headerUdfFields.length }} 项</span>
      </button>

      <!-- 原始 JSON 快照 Tab -->
      <button
        :class="['doc-tab-btn', activeDocTab === 'tab_json' ? 'active' : '']"
        @click="activeDocTab = 'tab_json'"
      >
        <Code2 class="w-3.5 h-3.5 mr-1" />
        <span>原始签名快照 (JSON)</span>
      </button>
    </div>

    <!-- 3. Tab 主体内容区 (按需渲染单个视窗，零重排零开销) -->
    <div class="doc-tab-content">
      <!-- 渲染选中的子表格 -->
      <template v-for="(c, cIdx) in processedCollections" :key="c.key">
        <div v-if="activeDocTab === ('tab_table_' + cIdx)" class="collection-block">
          <div class="collection-header">
            <div class="c-title">
              <Layers class="w-4 h-4 text-purple-600" />
              <h4>{{ c.label }}</h4>
              <span class="text-xs text-slate-400 font-mono">({{ c.tableId }})</span>
            </div>
            
            <div class="c-actions">
              <span class="badge badge-info mr-2">{{ c.processedRows.length }} 行明细记录</span>

              <button
                :class="['btn-reorder-toggle', tableReorderModes[c.key] ? 'active-reorder' : '']"
                @click="toggleTableReorderMode(c.key)"
                :title="tableReorderModes[c.key] ? '点击锁定并保存列顺序' : '点击开启直接在表格中拖拽列头排序模式'"
              >
                <component :is="tableReorderModes[c.key] ? Lock : Unlock" class="w-3.5 h-3.5" />
                <span>{{ tableReorderModes[c.key] ? '完成并锁定列顺序' : '开启表头拖拽调序' }}</span>
              </button>

              <button
                class="btn-col-config"
                @click="openTransferDrawer(c.key)"
                title="设置本表格的显示列与顺序"
              >
                <SlidersHorizontal class="w-3.5 h-3.5 text-purple-600" />
                <span>列定制</span>
              </button>
            </div>
          </div>

          <div v-if="tableReorderModes[c.key]" class="drag-active-banner">
            <Move class="w-4 h-4 text-purple-700 animate-pulse shrink-0" />
            <span>
              <strong>已开启表头拖拽模式：</strong> 请直接用鼠标按住下方<strong>任意列头左右拖拽</strong>调整顺序，完毕后点击右上角【完成并锁定列顺序】。
            </span>
          </div>

          <div class="table-responsive">
            <table class="data-table" :class="[tableReorderModes[c.key] ? 'table-reordering' : '']">
              <thead>
                <tr>
                  <th
                    v-for="col in c.visibleColumns"
                    :key="col"
                    :title="tableReorderModes[c.key] ? '按住拖拽移动此列' : col"
                    :draggable="!!tableReorderModes[c.key]"
                    @dragstart="onDirectColDragStart(c.key, col, $event)"
                    @dragover="onDirectColDragOver(c.key, $event)"
                    @drop="onDirectColDrop(c.key, col)"
                    @dragend="onDirectColDragEnd"
                    :class="[
                      tableReorderModes[c.key] ? 'draggable-th-active' : 'draggable-th-idle',
                      draggingColKey === col && draggingTableKey === c.key ? 'dragging-source' : ''
                    ]"
                  >
                    <div class="th-content">
                      <GripVertical
                        v-if="tableReorderModes[c.key]"
                        class="w-3.5 h-3.5 text-purple-600 cursor-move mr-1.5 shrink-0"
                      />
                      <span>{{ c.columnLabels[col] || col }}</span>
                    </div>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="r in c.processedRows" :key="r.rIdx">
                  <td
                    v-for="col in c.visibleColumns"
                    :key="col"
                    :class="[
                      r.cells[col]?.isNum ? 'align-right' : '',
                      r.cells[col]?.isItemCode ? 'font-mono font-bold text-blue-600' : '',
                      r.cells[col]?.isClosed ? 'bg-amber-50 text-amber-800 font-bold' : ''
                    ]"
                  >
                    <span
                      v-if="r.cells[col]?.isTranslated"
                      class="text-emerald-700 font-semibold"
                    >
                      {{ r.cells[col]?.display }}
                    </span>
                    <span v-else>
                      {{ r.cells[col]?.display }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 渲染主表属性 Tab -->
      <div v-if="activeDocTab === 'tab_header'" class="ext-fields-block">
        <div class="ext-header">
          <div class="ext-title">
            <Tag class="w-3.5 h-3.5 text-blue-600" />
            <h5>主表业务字段与自定义属性 (当前显示: {{ headerUdfFields.length }} 项)</h5>
          </div>

          <div class="ext-actions">
            <button
              :class="['btn-filter', isHeaderReorderMode ? 'active' : '']"
              @click="isHeaderReorderMode = !isHeaderReorderMode"
              :title="isHeaderReorderMode ? '点击锁定并保存字段顺序与置顶设置' : '点击开启在页面上直接拖拽字段至顶部看板或调序'"
            >
              <component :is="isHeaderReorderMode ? Lock : Move" class="w-3 h-3" />
              <span>{{ isHeaderReorderMode ? '完成并锁定布局' : '页面拖拽与置顶' }}</span>
            </button>

            <button
              :class="['btn-filter', showSystemFields ? 'active' : '']"
              @click="showSystemFields = !showSystemFields"
              :title="showSystemFields ? '点击隐藏底层技术字段' : '点击展开全部底层技术元数据'"
            >
              <component :is="showSystemFields ? EyeOff : Eye" class="w-3 h-3" />
              <span>{{ showSystemFields ? '隐藏技术字段' : '显示底层技术字段' }}</span>
            </button>

            <button class="btn-filter" @click="openTransferDrawer('header')" title="打开双栏穿梭定制抽屉">
              <SlidersHorizontal class="w-3 h-3 text-slate-600" />
              <span>字段定制 (双栏穿梭)</span>
            </button>

            <div class="search-box">
              <Search class="w-3 h-3 text-slate-400" />
              <input v-model="searchUdf" placeholder="搜索字段名/描述/取值..." class="search-input" />
            </div>
          </div>
        </div>

        <div class="fields-grid" :class="[isHeaderReorderMode ? 'grid-reordering' : '']">
          <div
            v-for="f in headerUdfFields"
            :key="f.key"
            class="field-cell"
            :class="[
              isHeaderReorderMode ? 'field-cell-draggable' : '',
              isFieldPinned(f.key) ? 'field-cell-pinned' : '',
              draggingHeaderKey === f.key ? 'dragging-source' : ''
            ]"
            :draggable="isHeaderReorderMode"
            @dragstart="onHeaderCardDragStart(f.key, $event)"
            @dragover="onHeaderCardDragOver"
            @drop="onHeaderCardDrop(f.key)"
            @dragend="onHeaderCardDragEnd"
          >
            <div class="f-label-row">
              <div class="f-label-left">
                <GripVertical v-if="isHeaderReorderMode" class="w-3.5 h-3.5 text-blue-500 cursor-move mr-1 shrink-0" />
                <span class="f-label" :title="f.key">{{ f.label }}</span>
              </div>
              
              <div class="f-label-right">
                <span v-if="f.key !== f.label" class="f-key-code">{{ f.key }}</span>
                <button
                  class="btn-pin-toggle"
                  :class="[isFieldPinned(f.key) ? 'pinned' : '']"
                  @click.stop="togglePinField(f.key)"
                  :title="isFieldPinned(f.key) ? '已置顶在顶部概览卡片' : '点击置顶固定到顶部概览卡片'"
                >
                  <component :is="isFieldPinned(f.key) ? Pin : PinOff" class="w-3 h-3" />
                </button>
              </div>
            </div>
            <div class="f-val-wrap">
              <span v-if="f.formatted.isTranslated" class="f-val text-emerald-700 font-semibold">
                {{ f.formatted.display }}
              </span>
              <span v-else class="f-val">
                {{ f.formatted.display }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- 渲染原始 JSON 快照 Tab -->
      <div v-if="activeDocTab === 'tab_json'" class="json-preview-container">
        <pre class="json-preview">{{ JSON.stringify(parsedData, null, 2) }}</pre>
      </div>
    </div>

    <!-- 4. 世界级双栏穿梭定制抽屉 (Two-Column Transfer Drawer) -->
    <div v-if="showTransferDrawer" class="drawer-backdrop" @click.self="showTransferDrawer = false">
      <div class="drawer-panel">
        <div class="drawer-header">
          <div class="drawer-title-wrap">
            <div class="drawer-title">
              <Settings2 class="w-5 h-5 text-blue-600 mr-2" />
              <span>单据字段与列个性化定制中心</span>
            </div>
            <div class="drawer-subtitle">
              <span>当前对象：<strong>{{ objectCode || 'CHORDR' }}</strong></span>
              <span v-if="isCustomizedByMe" class="badge-user-pref">已启用个人专属偏好</span>
              <span v-else class="badge-global-pref">继承全公司默认配置</span>
            </div>
          </div>

          <button class="btn-close-drawer" @click="showTransferDrawer = false">
            <X class="w-5 h-5" />
          </button>
        </div>

        <div class="drawer-tabs">
          <button
            :class="['drawer-tab', activeTransferTab === 'header' ? 'active' : '']"
            @click="activeTransferTab = 'header'"
          >
            <Tag class="w-4 h-4 mr-1.5" />
            <span>主表业务属性 ({{ allHeaderFieldsList.length }} 项)</span>
          </button>
          <button
            v-for="c in processedCollections"
            :key="c.key"
            :class="['drawer-tab', activeTransferTab === c.key ? 'active' : '']"
            @click="activeTransferTab = c.key"
          >
            <Layers class="w-4 h-4 mr-1.5" />
            <span>{{ c.label }} ({{ c.allColumns.length }} 列)</span>
          </button>
        </div>

        <div class="drawer-body">
          <div class="transfer-container">
            <!-- 左栏：待选字段素材库 -->
            <div class="transfer-pane transfer-left">
              <div class="pane-header">
                <div class="pane-title">
                  <Search class="w-4 h-4 text-slate-500 mr-1.5" />
                  <span>待选字段素材库 ({{ transferLeftItems.length }})</span>
                </div>
                <button class="btn-link-action" @click="transferAddAll" title="一键全部显示">
                  全部添加
                </button>
              </div>

              <div class="pane-search">
                <Search class="w-3.5 h-3.5 text-slate-400 mr-2" />
                <input
                  v-model="transferSearchLeft"
                  placeholder="搜索待选字段/代码/取值..."
                  class="pane-search-input"
                />
              </div>

              <div class="transfer-list">
                <div
                  v-for="item in transferLeftItems"
                  :key="item.key"
                  class="transfer-item transfer-item-left"
                  :class="[item.isAdded ? 'item-added' : '']"
                  @click="!item.isAdded && transferAddItem(item.key)"
                >
                  <div class="item-info">
                    <div class="item-title-row">
                      <span class="item-label">{{ item.label }}</span>
                      <span class="item-key font-mono">{{ item.key }}</span>
                      <span v-if="item.isSystem" class="tech-tag">技术字段</span>
                    </div>
                    <div v-if="item.sampleVal && item.sampleVal !== '-'" class="item-sample">
                      样例: {{ item.sampleVal }}
                    </div>
                  </div>

                  <button
                    v-if="!item.isAdded"
                    class="btn-add-item"
                    @click.stop="transferAddItem(item.key)"
                    title="添加到右侧显示列表"
                  >
                    <Plus class="w-4 h-4 text-blue-600" />
                  </button>
                  <span v-else class="badge-added">
                    <Check class="w-3 h-3 mr-0.5" /> 已显示
                  </span>
                </div>
                <div v-if="transferLeftItems.length === 0" class="empty-list">
                  未匹配到相关字段
                </div>
              </div>
            </div>

            <!-- 中间穿梭指示分割 -->
            <div class="transfer-divider">
              <ArrowRightLeft class="w-5 h-5 text-slate-400" />
            </div>

            <!-- 右栏：当前显示字段与排列顺序 (支持上下拖拽调序) -->
            <div class="transfer-pane transfer-right">
              <div class="pane-header">
                <div class="pane-title">
                  <CheckCheck class="w-4 h-4 text-emerald-600 mr-1.5" />
                  <span>当前显示字段与顺序 ({{ transferRightItems.length }})</span>
                </div>
                <button class="btn-link-action text-rose-600" @click="transferRemoveAll" title="一键全部隐藏">
                  全部清空
                </button>
              </div>

              <div class="pane-search">
                <Search class="w-3.5 h-3.5 text-slate-400 mr-2" />
                <input
                  v-model="transferSearchRight"
                  placeholder="过滤已显示字段..."
                  class="pane-search-input"
                />
              </div>

              <div class="reorder-tip-bar">
                <GripVertical class="w-3.5 h-3.5 text-blue-600 mr-1" />
                <span>按住左侧抓手 <strong>上下拖拽</strong> 即可调序在页面中的先后显示顺序</span>
              </div>

              <div class="transfer-list">
                <div
                  v-for="(item, idx) in transferRightItems"
                  :key="item.key"
                  class="transfer-item transfer-item-right"
                  :class="[draggingDrawerKey === item.key ? 'dragging-source' : '']"
                  draggable="true"
                  @dragstart="onDrawerDragStart(item.key, $event)"
                  @dragover="onDrawerDragOver"
                  @drop="onDrawerDrop(item.key)"
                  @dragend="onDrawerDragEnd"
                >
                  <div class="reorder-grip cursor-move">
                    <GripVertical class="w-4 h-4 text-slate-400" />
                    <span class="order-seq font-mono">{{ idx + 1 }}</span>
                  </div>

                  <div class="item-info">
                    <div class="item-title-row">
                      <span class="item-label font-bold">{{ item.label }}</span>
                      <span class="item-key font-mono">{{ item.key }}</span>
                      <span v-if="item.isPinned" class="pinned-tag-mini">已置顶</span>
                    </div>
                  </div>

                  <div class="item-actions">
                    <button
                      class="btn-delete-item"
                      @click="transferRemoveItem(item.key)"
                      title="从当前显示中移除"
                    >
                      <Trash2 class="w-4 h-4 text-slate-400 hover:text-rose-600" />
                    </button>
                  </div>
                </div>
                <div v-if="transferRightItems.length === 0" class="empty-list">
                  当前未选择任何显示字段
                </div>
              </div>
            </div>
          </div>

          <div v-if="drawerToast" :class="['drawer-toast', drawerToast.type]">
            {{ drawerToast.text }}
          </div>
        </div>

        <div class="drawer-footer">
          <div class="footer-left">
            <button
              class="btn btn-secondary btn-sm"
              :disabled="isSavingLayout"
              @click="resetToCompanyDefaultLayout"
              title="清除个人专属偏好，恢复为全公司统一的全局配置"
            >
              <RotateCcw class="w-3.5 h-3.5 mr-1" />
              <span>恢复全公司默认</span>
            </button>
          </div>

          <div class="footer-right">
            <button
              class="btn btn-primary btn-sm"
              :disabled="isSavingLayout"
              @click="saveUserLayoutToServer"
              title="将当前布局保存为我的个人专属配置 (保存到服务器)"
            >
              <Save class="w-3.5 h-3.5 mr-1" />
              <span>{{ isSavingLayout ? '正在保存...' : '保存为我的个人偏好' }}</span>
            </button>

            <button
              v-if="isAdmin"
              class="btn btn-admin-global btn-sm"
              :disabled="isSavingLayout"
              @click="saveGlobalDefaultLayoutToServer"
              title="将当前字段与顺序发布为全公司的全局默认模板 (所有新用户默认生效)"
            >
              <ShieldCheck class="w-3.5 h-3.5 mr-1 text-amber-300" />
              <span>保存为全公司默认配置 (Admin)</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.doc-viewer-container {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

/* 顶部概览 Hero 卡片 */
.summary-card {
  display: flex;
  justify-content: space-between;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 14px;
  gap: 16px;
}

.summary-card-reorder {
  border: 2px dashed #3b82f6;
  background: #eff6ff;
}

.summary-card-drag-over {
  border-color: #10b981;
  background: #ecfdf5;
}

.summary-left {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.summary-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.s-label {
  font-size: 11px;
  color: #64748b;
}

.s-val-highlight {
  display: flex;
  align-items: center;
  font-size: 14.5px;
  color: #0f172a;
}

.sub-code {
  font-size: 11.5px;
  color: #64748b;
  margin-left: 6px;
  font-family: monospace;
}

.summary-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.summary-sub-item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
}

.sub-label {
  color: #64748b;
}

.sub-val {
  color: #1e293b;
}

.badge-trans {
  background: #ecfdf5;
  color: #047857;
  font-weight: 600;
  padding: 0 4px;
  border-radius: 2px;
}

.summary-comment {
  display: flex;
  align-items: center;
  gap: 6px;
  background: #fffbeb;
  border: 1px solid #fef3c7;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 11px;
  color: #92400e;
}

.summary-right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  justify-content: center;
  min-width: 140px;
}

.total-amount {
  font-size: 18px;
  font-weight: 800;
  color: #059669;
  font-family: monospace;
}

.docnum-tag {
  font-size: 10.5px;
  color: #64748b;
  font-family: monospace;
}

/* Tab 选项卡导航条 */
.doc-tab-bar {
  display: flex;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 3px;
  gap: 4px;
}

.doc-tab-btn {
  display: flex;
  align-items: center;
  padding: 6px 12px;
  background: transparent;
  border: none;
  border-radius: 4px;
  font-size: 11.5px;
  font-weight: 600;
  color: #475569;
  cursor: pointer;
  transition: all 0.15s;
}

.doc-tab-btn:hover {
  background: #f1f5f9;
  color: #1e293b;
}

.doc-tab-btn.active {
  background: #2563eb;
  color: #ffffff;
  box-shadow: 0 1px 2px rgba(37, 99, 235, 0.2);
}

.tab-count-badge {
  margin-left: 6px;
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 10px;
  background: rgba(0, 0, 0, 0.08);
}

.doc-tab-btn.active .tab-count-badge {
  background: rgba(255, 255, 255, 0.25);
  color: #ffffff;
}

/* Tab 内容区 */
.doc-tab-content {
  display: flex;
  flex-direction: column;
}

/* 子表格区域 */
.collection-block {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  overflow: hidden;
}

.collection-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
}

.c-title {
  display: flex;
  align-items: center;
  gap: 6px;
}

.c-title h4 {
  margin: 0;
  font-size: 12.5px;
  font-weight: 700;
  color: #1e293b;
}

.c-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-reorder-toggle, .btn-col-config, .btn-filter {
  display: flex;
  align-items: center;
  gap: 4px;
  border: 1px solid #e2e8f0;
  background: #ffffff;
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 11px;
  color: #475569;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-reorder-toggle:hover, .btn-col-config:hover, .btn-filter:hover {
  background: #f1f5f9;
}

.btn-reorder-toggle.active-reorder, .btn-filter.active {
  background: #eff6ff;
  border-color: #bfdbfe;
  color: #2563eb;
  font-weight: 600;
}

.drag-active-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  background: #faf5ff;
  border-bottom: 1px solid #e9d5ff;
  padding: 6px 12px;
  font-size: 11px;
  color: #6b21a8;
}

.table-responsive {
  max-height: 480px;
  overflow: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 11px;
  text-align: left;
}

.data-table th {
  position: sticky;
  top: 0;
  background: #f8fafc;
  border-bottom: 1px solid #cbd5e1;
  padding: 6px 10px;
  font-weight: 700;
  color: #334155;
  white-space: nowrap;
  z-index: 2;
}

.data-table td {
  padding: 6px 10px;
  border-bottom: 1px solid #f1f5f9;
  color: #1e293b;
  white-space: nowrap;
}

.data-table tbody tr:hover {
  background: #f8fafc;
}

.align-right {
  text-align: right;
  font-family: monospace;
}

/* 主表 UDF 属性网格 */
.ext-fields-block {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 12px;
}

.ext-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.ext-title {
  display: flex;
  align-items: center;
  gap: 6px;
}

.ext-title h5 {
  margin: 0;
  font-size: 12.5px;
  font-weight: 700;
  color: #1e293b;
}

.ext-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.search-box {
  display: flex;
  align-items: center;
  background: #f8fafc;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  padding: 2px 6px;
}

.search-input {
  border: none;
  background: transparent;
  outline: none;
  font-size: 11px;
  margin-left: 4px;
  width: 140px;
}

.fields-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 8px;
  max-height: 480px;
  overflow-y: auto;
  padding: 2px;
}

.field-cell {
  border: 1px solid #e2e8f0;
  border-radius: 4px;
  padding: 6px 8px;
  background: #ffffff;
  display: flex;
  flex-direction: column;
  gap: 2px;
  transition: all 0.15s;
}

.field-cell:hover {
  border-color: #93c5fd;
  background: #f8fafc;
}

.f-label-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.f-label-left {
  display: flex;
  align-items: center;
  overflow: hidden;
}

.f-label {
  font-size: 10.5px;
  color: #64748b;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.f-label-right {
  display: flex;
  align-items: center;
  gap: 4px;
}

.f-key-code {
  font-size: 9px;
  color: #94a3b8;
  font-family: monospace;
}

.btn-pin-toggle {
  border: none;
  background: transparent;
  padding: 1px;
  cursor: pointer;
  color: #94a3b8;
}

.btn-pin-toggle.pinned {
  color: #2563eb;
}

.f-val {
  font-size: 12px;
  color: #0f172a;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* 原始 JSON 预览 */
.json-preview-container {
  background: #0f172a;
  border-radius: 6px;
  padding: 12px;
  max-height: 480px;
  overflow: auto;
}

.json-preview {
  margin: 0;
  font-family: monospace;
  font-size: 11px;
  color: #38bdf8;
  line-height: 1.4;
}

/* 双栏穿梭抽屉样式 */
.drawer-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.45);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
}

.drawer-panel {
  width: 720px;
  height: 100vh;
  background: #ffffff;
  box-shadow: -10px 0 30px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid #e2e8f0;
}

.drawer-title {
  display: flex;
  align-items: center;
  font-size: 15px;
  font-weight: 700;
  color: #0f172a;
}

.drawer-subtitle {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11.5px;
  color: #64748b;
  margin-top: 2px;
}

.badge-user-pref {
  background: #eff6ff;
  color: #2563eb;
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 10.5px;
  font-weight: 600;
}

.badge-global-pref {
  background: #f1f5f9;
  color: #475569;
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 10.5px;
}

.btn-close-drawer {
  border: none;
  background: transparent;
  cursor: pointer;
  color: #64748b;
  padding: 4px;
}

.btn-close-drawer:hover {
  color: #0f172a;
}

.drawer-tabs {
  display: flex;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
  padding: 4px 18px 0 18px;
  gap: 8px;
}

.drawer-tab {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  border: none;
  background: transparent;
  font-size: 12px;
  color: #64748b;
  border-bottom: 2px solid transparent;
  cursor: pointer;
}

.drawer-tab.active {
  color: #2563eb;
  font-weight: 700;
  border-bottom-color: #2563eb;
}

.drawer-body {
  flex: 1;
  padding: 14px 18px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.transfer-container {
  flex: 1;
  display: flex;
  gap: 12px;
  overflow: hidden;
}

.transfer-pane {
  flex: 1;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.pane-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
}

.pane-title {
  display: flex;
  align-items: center;
  font-size: 12px;
  font-weight: 700;
  color: #1e293b;
}

.btn-link-action {
  border: none;
  background: transparent;
  color: #2563eb;
  font-size: 11px;
  font-weight: 600;
  cursor: pointer;
}

.pane-search {
  display: flex;
  align-items: center;
  padding: 6px 10px;
  border-bottom: 1px solid #f1f5f9;
}

.pane-search-input {
  width: 100%;
  border: none;
  outline: none;
  font-size: 11.5px;
}

.reorder-tip-bar {
  display: flex;
  align-items: center;
  background: #eff6ff;
  padding: 4px 10px;
  font-size: 10.5px;
  color: #1d4ed8;
  border-bottom: 1px solid #dbeafe;
}

.transfer-list {
  flex: 1;
  overflow-y: auto;
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.transfer-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 8px;
  border: 1px solid #e2e8f0;
  border-radius: 4px;
  background: #ffffff;
  font-size: 11.5px;
  transition: all 0.15s;
}

.transfer-item-left {
  cursor: pointer;
}

.transfer-item-left:hover:not(.item-added) {
  border-color: #93c5fd;
  background: #f8fafc;
}

.transfer-item-left.item-added {
  background: #f8fafc;
  opacity: 0.6;
  cursor: default;
}

.item-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 1px;
  overflow: hidden;
}

.item-title-row {
  display: flex;
  align-items: center;
  gap: 6px;
}

.item-label {
  color: #0f172a;
}

.item-key {
  font-size: 10px;
  color: #94a3b8;
}

.tech-tag {
  font-size: 9px;
  background: #f1f5f9;
  color: #64748b;
  padding: 0 4px;
  border-radius: 2px;
}

.item-sample {
  font-size: 10px;
  color: #64748b;
}

.btn-add-item, .btn-delete-item {
  border: none;
  background: transparent;
  cursor: pointer;
  padding: 2px;
}

.badge-added {
  display: flex;
  align-items: center;
  font-size: 10.5px;
  color: #10b981;
  font-weight: 600;
}

.reorder-grip {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-right: 6px;
}

.order-seq {
  font-size: 10px;
  color: #64748b;
  min-width: 14px;
}

.transfer-divider {
  display: flex;
  align-items: center;
  justify-content: center;
}

.drawer-toast {
  padding: 6px 12px;
  border-radius: 4px;
  font-size: 11.5px;
  font-weight: 600;
  margin-top: 8px;
}

.drawer-toast.success {
  background: #ecfdf5;
  color: #065f46;
  border: 1px solid #a7f3d0;
}

.drawer-toast.error {
  background: #fef2f2;
  color: #991b1b;
  border: 1px solid #fecaca;
}

.drawer-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 18px;
  border-top: 1px solid #e2e8f0;
  background: #f8fafc;
}

.footer-right {
  display: flex;
  gap: 8px;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 6px 12px;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-primary {
  background: #2563eb;
  color: #ffffff;
  border: none;
}

.btn-primary:hover:not(:disabled) {
  background: #1d4ed8;
}

.btn-secondary {
  background: #ffffff;
  color: #334155;
  border: 1px solid #cbd5e1;
}

.btn-secondary:hover:not(:disabled) {
  background: #f1f5f9;
}

.btn-admin-global {
  background: #0f172a;
  color: #ffffff;
  border: 1px solid #334155;
}

.btn-admin-global:hover:not(:disabled) {
  background: #1e293b;
}

.btn-sm {
  padding: 5px 10px;
  font-size: 11.5px;
}
</style>
