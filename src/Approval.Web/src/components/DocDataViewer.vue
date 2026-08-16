<script setup lang="ts">
import { computed, ref } from 'vue'
import ChildTableView from './doc/ChildTableView.vue'
import { useDocData } from '../composables/useDocData'
import api from '../config/request'
import {
  appConfig,
  defaultPinnedFields,
  defaultMemoFields,
  getCurrencySymbol
} from '../config'
import {
  Code2,
  Building2,
  FileText,
  Layers,
  Tag,
  Search,
  SlidersHorizontal,
  RotateCcw,
  Check,
  X,
  GripVertical,
  Pin,
  PinOff,
  Plus,
  Trash2,
  Save,
  ShieldCheck,
  ArrowRightLeft,
  CheckCheck
} from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    rawJson: string
    objectCode?: string
    companyId?: string
  }>(),
  {
    companyId: appConfig.defaultCompanyId
  }
)

// 当前激活的内容 Tab (默认第一个子表或主表属性)
const activeDocTab = ref<string>('tab_table_0')

// const metaData = ref<any>(null)
// const loadingMeta = ref(false)
const searchUdf = ref('')
const showSystemFields = ref(false)

// 当前操作员与 Admin 权限判断
const currentUser = computed(() => localStorage.getItem('sap_b1_approval_user') || 'manager')
const isAdmin = computed(() => {
  const u = currentUser.value.toLowerCase()
  return u === 'admin' || u === 'manager'
})

// 主表直接拖拽模式


// 各子表直接拖拽排序模式映射 (tableKey -> boolean)


// ===================== 世界级双栏穿梭定制抽屉 (Transfer Drawer) =====================
const showTransferDrawer = ref(false)
const activeTransferTab = ref<string>('header') // 'header' 或具体子表集合 Key
const transferSearchLeft = ref('')
const transferSearchRight = ref('')
const isSavingLayout = ref(false)
const drawerToast = ref<{ text: string; type: 'success' | 'error' } | null>(null)
const isCustomizedByMe = ref(false)

// 表格行内过滤与密度控制 (紧凑 compact | 标准 normal | 宽松 comfortable)
const tableSearchQuery = ref('')
const tableDensity = ref<'compact' | 'normal' | 'comfortable'>((localStorage.getItem('sap_b1_table_density') as any) || 'normal')

const setTableDensity = (density: 'compact' | 'normal' | 'comfortable') => {
  tableDensity.value = density
  localStorage.setItem('sap_b1_table_density', density)
}


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


// 多币种与本币/外币金额智能提取与折算汇总
const docAmountsSummary = computed(() => {
  const data = parsedData.value || {}
  const cur = (data.U_DocCur || data.DocCur || data.DocCurrency || 'RMB').toString().trim().toUpperCase()
  const isForeign = cur !== 'RMB' && cur !== 'CNY'

  // 本币金额 (DocTotal / U_DocTotal)
  const localAmount = parseFloat(data.U_DocTotal ?? data.DocTotal ?? 0)
  // 外币金额 (DocTotalFC / U_DocTotalFC)
  const fcAmount = parseFloat(data.U_DocTotalFC ?? data.DocTotalFC ?? data.DocTotalFc ?? 0)
  // 单据汇率 (DocRate / U_DocRate)
  const rate = parseFloat(data.U_DocRate ?? data.DocRate ?? 0)

  return {
    cur,
    isForeign,
    localAmount: !isNaN(localAmount) ? localAmount : 0,
    fcAmount: !isNaN(fcAmount) && fcAmount > 0 ? fcAmount : (isForeign ? localAmount : 0),
    rate: !isNaN(rate) && rate > 0 ? rate : null
  }
})

const currencySymbol = computed(() => {
  return getCurrencySymbol(docCurrency.value)
})

// 用户自定义固定在顶部概览区的字段 Key 数组 (持久化存储)
const pinnedFieldKeys = ref<string[]>(
  JSON.parse(localStorage.getItem(`sap_b1_pinned_${props.objectCode || 'CHORDR'}`) || JSON.stringify(defaultPinnedFields))
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

// 用户自定义归集在备注专区的字段 Key 数组 (持久化存储)
const memoFieldKeys = ref<string[]>(
  JSON.parse(localStorage.getItem(`sap_b1_memo_${props.objectCode || 'CHORDR'}`) || JSON.stringify(defaultMemoFields))
)

const isMemoZoneExpanded = ref(true)

const toggleMemoZone = () => {
  isMemoZoneExpanded.value = !isMemoZoneExpanded.value
}

const isFieldInMemo = (key: string) => {
  const stripped = key.startsWith('U_') ? key.substring(2) : key
  return memoFieldKeys.value.includes(key) || memoFieldKeys.value.includes(stripped) || memoFieldKeys.value.includes('U_' + stripped)
}

const toggleMemoField = (key: string) => {
  const stripped = key.startsWith('U_') ? key.substring(2) : key
  const foundIdx = memoFieldKeys.value.findIndex(k => k === key || k === stripped || k === 'U_' + stripped)
  if (foundIdx > -1) {
    memoFieldKeys.value.splice(foundIdx, 1)
  } else {
    memoFieldKeys.value.push(key)
  }
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

// 3. 关联字典与代码呈现模式: 'NameAndCode' (默认: 描述+代码) | 'NameOnly' (仅描述) | 'CodeOnly' (仅代码)
const fieldDisplayMode = ref<'NameAndCode' | 'NameOnly' | 'CodeOnly'>(
  (localStorage.getItem(`sap_b1_disp_mode_${props.objectCode || 'CHORDR'}`) as any) || 'NameAndCode'
)


// 4. 每个独立字段/列的自定义呈现模式覆盖 (Per-field display override)
const fieldDisplayOverrides = ref<Record<string, 'Inherit' | 'NameAndCode' | 'NameOnly' | 'CodeOnly'>>(
  JSON.parse(localStorage.getItem(`sap_b1_field_disp_${props.objectCode || 'CHORDR'}`) || '{}')
)

const setFieldDisplayOverride = (key: string, mode: 'Inherit' | 'NameAndCode' | 'NameOnly' | 'CodeOnly') => {
  if (mode === 'Inherit') {
    delete fieldDisplayOverrides.value[key]
  } else {
    fieldDisplayOverrides.value[key] = mode
  }
  syncLocalLayoutCache()
}

const getFieldEffectiveDisplayMode = (key: string): 'NameAndCode' | 'NameOnly' | 'CodeOnly' => {
  const stripped = key.startsWith('U_') ? key.substring(2) : key
  const override = fieldDisplayOverrides.value[key] || fieldDisplayOverrides.value[stripped] || fieldDisplayOverrides.value['U_' + stripped]
  if (override && override !== 'Inherit') {
    return override
  }
  return fieldDisplayMode.value
}

const setDisplayMode = (mode: 'NameAndCode' | 'NameOnly' | 'CodeOnly') => {
  fieldDisplayMode.value = mode
  syncLocalLayoutCache()
}

// 同步写入本地 LocalStorage
const syncLocalLayoutCache = () => {
  const obj = props.objectCode || 'CHORDR'
  localStorage.setItem(`sap_b1_pinned_${obj}`, JSON.stringify(pinnedFieldKeys.value))
  localStorage.setItem(`sap_b1_memo_${obj}`, JSON.stringify(memoFieldKeys.value))
  localStorage.setItem(`sap_b1_hidden_${obj}_header`, JSON.stringify(userHiddenFields.value))
  localStorage.setItem(`sap_b1_order_${obj}_header`, JSON.stringify(headerFieldOrder.value))
  localStorage.setItem(`sap_b1_col_hidden_${obj}`, JSON.stringify(collectionHiddenCols.value))
  localStorage.setItem(`sap_b1_col_order_${obj}`, JSON.stringify(collectionColOrders.value))
  localStorage.setItem(`sap_b1_disp_mode_${obj}`, fieldDisplayMode.value)
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
        if (parsed.memoKeys && Array.isArray(parsed.memoKeys)) {
          memoFieldKeys.value = parsed.memoKeys
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
        if (parsed.fieldDisplayOverrides && typeof parsed.fieldDisplayOverrides === 'object') {
          fieldDisplayOverrides.value = parsed.fieldDisplayOverrides
        }
        if (parsed.displayMode) {
          fieldDisplayMode.value = parsed.displayMode
        }
        syncLocalLayoutCache()
      }
    }
  } catch {}
}


const {
  metaData,
  DEFAULT_HIDDEN_FIELDS,
  childTechColumns,
  getFieldLabel,
  formatFieldValue
} = useDocData(
  computed(() => props.objectCode),
  computed(() => props.companyId),
  parsedData,
  loadTieredLayoutFromServer,
  getFieldEffectiveDisplayMode,
  currencySymbol
)
// 提取顶部概览卡片动态钉选字段列表

// 针对草稿与财务日记账分录的智能识别与借贷平衡预计算
const isDraftDocument = computed(() => {
  const obj = (props.objectCode || parsedData.value?.Object || '').toUpperCase()
  return obj === 'DRAFTS' || obj === 'ODRF' || obj === '112' || parsedData.value?.DocObjectCode !== undefined
})

const journalBalanceInfo = computed(() => {
  const obj = (props.objectCode || parsedData.value?.Object || '').toUpperCase()
  if (obj !== 'OJDT' && obj !== 'JOURNALENTRIES' && obj !== 'OBTD' && obj !== 'JOURNALVOUCHERS') return null

  let totalDebit = 0
  let totalCredit = 0
  const lines = parsedData.value?.JournalEntryLines || parsedData.value?.JournalVoucherLines || parsedData.value?.BTD1 || parsedData.value?.JDT1 || []
  if (Array.isArray(lines)) {
    lines.forEach((l: any) => {
      const deb = parseFloat(l.Debit || l.SYMDeb || l.DebitSys || 0)
      const cred = parseFloat(l.Credit || l.SYMCred || l.CreditSys || 0)
      if (!isNaN(deb)) totalDebit += deb
      if (!isNaN(cred)) totalCredit += cred
    })
  }

  const isBalanced = Math.abs(totalDebit - totalCredit) < 0.001
  return {
    totalDebit,
    totalCredit,
    isBalanced,
    diff: Math.abs(totalDebit - totalCredit)
  }
})

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

// 规范化多行文本换行 (100% 忠实还原 SAP B1 原始回车换行与排版格式)
const normalizeMultilineText = (text: any): string => {
  if (text === null || text === undefined) return ''
  let str = String(text)
  // 处理字面量转义 \\r\\n 或 \\n
  str = str.replace(/\\r\\n/g, '\n').replace(/\\n/g, '\n').replace(/\\r/g, '\n')
  // 统一原生 \r\n 和 \r 为标准 \n
  str = str.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  return str
}

// 提取专属多重备注与长文本卡片列表 (0 运行时开销，杜绝内联函数重绘)
const processedMemoFields = computed(() => {
  const data = parsedData.value
  if (!data) return []

  const result: {
    key: string
    label: string
    value: string
    hasContent: boolean
  }[] = []

  memoFieldKeys.value.forEach(k => {
    let actualVal = data[k]
    let actualKey = k
    if (actualVal === undefined) {
      const alt = k.startsWith('U_') ? k.substring(2) : ('U_' + k)
      if (data[alt] !== undefined) {
        actualVal = data[alt]
        actualKey = alt
      }
    }

    if (actualVal !== undefined && actualVal !== null) {
      const strVal = String(actualVal).trim()
      if (strVal !== '' && strVal !== '-') {
        const formatted = formatFieldValue(actualKey, actualVal)
        const displayVal = formatted.isTranslated ? formatted.display : normalizeMultilineText(actualVal)
        result.push({
          key: k,
          label: getFieldLabel(k),
          value: displayVal,
          hasContent: true
        })
      }
    }
  })

  return result
})

// 智能子表标题清洗器 (剥离主表冗余前缀如 '型号订单 - '、'型号订单 - 表头 - ')
const cleanTableDescription = (desc: string, objDesc?: string): string => {
  if (!desc) return desc
  let res = desc.trim()

  // 1. 如果包含对象自身名称 (例如 '型号订单'、'销售订单')，剥离前缀
  if (objDesc && res.startsWith(objDesc)) {
    res = res.substring(objDesc.length).trim()
  }

  // 2. 剥离常见连接符与冗余词缀
  res = res.replace(/^[-\s—:]+/, '')
  res = res.replace(/^表头\s*[-\s—:]+/, '')
  res = res.replace(/^行\s*[-\s—:]+/, '')
  res = res.replace(/^主表\s*[-\s—:]+/, '')

  // 3. 严格遵循 SAP Business One 官方客户端标准术语规范
  if (res === '行' || res === '明细' || res === '明细行') {
    res = '内容'
  } else if (res === '生产明细' || res === '工序明细') {
    res = '组件'
  }

  return res.trim() || desc
}

// 提取全部子表集合与预计算单元格 (0 运行时开销，杜绝内联函数重绘)
const processedCollections = computed(() => {
  const data = parsedData.value
  const objName = metaData.value?.objectDescription || '型号订单'
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
      let cleanKey = k.replace('Collection', '')
      if (!cleanKey.startsWith('@')) cleanKey = '@' + cleanKey

      // 1. 优先从 SAP 真实数据库元数据字典 (OUDO / UDO1 / OUTB / 标准单据) 提取子表中文描述
      let rawTableDesc = metaData.value?.childTableDescriptions?.[k]
        || metaData.value?.childTableDescriptions?.[cleanKey]
        || metaData.value?.childTableDescriptions?.[cleanKey.substring(1)]
        || ''

      if (k.includes('1Collection') || k === 'DocumentLines') {
        tableId = '@CH_ORDR_1'
        if (!rawTableDesc) rawTableDesc = '型号订单 - 行'
      } else if (k.includes('3Collection')) {
        tableId = '@CH_ORDR_3'
        if (!rawTableDesc) rawTableDesc = '型号订单 - 表头 - 附加费用'
      } else if (k.includes('2Collection')) {
        tableId = '@CH_ORDR_2'
      } else {
        tableId = cleanKey
      }

      // 智能剥离主表冗余前缀 (例如 '型号订单 - 行' -> '明细行', '型号订单 - 表头 - 附加费用' -> '附加费用')
      const cleanDesc = cleanTableDescription(rawTableDesc, objName)

      // 2. 根据用户设置的呈现模式格式化 Tab 标签与表头
      const effMode = getFieldEffectiveDisplayMode(k)
      let label = ''
      if (cleanDesc) {
        if (effMode === 'NameOnly') {
          label = cleanDesc
        } else if (effMode === 'CodeOnly') {
          label = tableId
        } else {
          label = `${cleanDesc} (${tableId})`
        }
      } else {
        label = tableId
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

      // 预计算全部行与单元格 (支持快速搜索即时过滤)
      let rawRows = v
      if (tableSearchQuery.value.trim()) {
        const q = tableSearchQuery.value.trim().toLowerCase()
        rawRows = v.filter((row: any) => {
          return Object.values(row).some(val => val !== null && val !== undefined && String(val).toLowerCase().includes(q))
        })
      }

      const processedRows = rawRows.map((row: any, rIdx: number) => {
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

  // 1. 遍历当前单据中拥有的字段
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

  // 2. 全量 Union 合并 SAP 数据库 (CUFD) 中定义的所有表头扩展字段 (哪怕当前单据取值为空也能搜索和配置)
  if (metaData.value?.headerFields) {
    for (const [k, meta] of Object.entries(metaData.value.headerFields)) {
      if (!fieldsMap.has(k) && !excludeKeys.has(k)) {
        const actualVal = data[k] !== undefined ? data[k] : (data[k.startsWith('U_') ? k.substring(2) : ('U_' + k)])
        const label = (meta as any)?.description || getFieldLabel(k)
        const formatted = formatFieldValue(k, actualVal ?? '-')
        const isSystem = DEFAULT_HIDDEN_FIELDS.includes(k)

        fieldsMap.set(k, {
          key: k,
          label,
          isSystem,
          formatted
        })
      }
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






// 表格表头直接拖拽排序控制










// 主表属性卡片拖拽逻辑








// 顶部概览拖拽置顶逻辑






// 顶部置顶标签上下/左右拖拽调序








// ===================== 服务器分层配置保存与重置 =====================
const getLayoutPayloadJson = () => {
  return JSON.stringify({
    pinnedKeys: pinnedFieldKeys.value,
    hiddenHeaderKeys: userHiddenFields.value,
    headerOrder: headerFieldOrder.value,
    colHiddenMap: collectionHiddenCols.value,
    colOrderMap: collectionColOrders.value,
    displayMode: fieldDisplayMode.value
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

const resetAllLayoutToFactoryDefault = async () => {
  await resetToCompanyDefaultLayout()
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
  if (activeTransferTab.value === 'memo') {
    return allHeaderFieldsList.value.map(f => {
      const isAdded = isFieldInMemo(f.key)
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
  } else if (activeTransferTab.value === 'header') {
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
  if (activeTransferTab.value === 'memo') {
    return memoFieldKeys.value.map(k => {
      const f = allHeaderFieldsList.value.find(item => item.key === k) || { key: k, label: getFieldLabel(k), formatted: { display: '-' } }
      return {
        key: k,
        label: f.label,
        isPinned: false,
        isInMemo: true,
        sampleVal: f.formatted?.display || '-'
      }
    }).filter(item => {
      if (!q) return true
      return item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q)
    })
  } else if (activeTransferTab.value === 'header') {
    return allHeaderFieldsList.value
      .filter(f => !userHiddenFields.value.includes(f.key))
      .map(f => ({
        key: f.key,
        label: f.label,
        isPinned: isFieldPinned(f.key),
        isInMemo: isFieldInMemo(f.key),
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
        isInMemo: false,
        sampleVal: coll.processedRows[0]?.cells[colKey]?.display || '-'
      }))
      .filter(item => {
        if (!q) return true
        return item.key.toLowerCase().includes(q) || item.label.toLowerCase().includes(q)
      })
  }
})

const transferAddItem = (key: string) => {
  if (activeTransferTab.value === 'memo') {
    if (!memoFieldKeys.value.includes(key)) {
      memoFieldKeys.value.push(key)
    }
  } else if (activeTransferTab.value === 'header') {
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
  if (activeTransferTab.value === 'memo') {
    memoFieldKeys.value = memoFieldKeys.value.filter(k => k !== key)
  } else if (activeTransferTab.value === 'header') {
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
  if (activeTransferTab.value === 'memo') {
    memoFieldKeys.value = allHeaderFieldsList.value.map(f => f.key)
  } else if (activeTransferTab.value === 'header') {
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
  if (activeTransferTab.value === 'memo') {
    memoFieldKeys.value = []
  } else if (activeTransferTab.value === 'header') {
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

  if (activeTransferTab.value === 'memo') {
    const list = [...memoFieldKeys.value]
    const fromIdx = list.indexOf(draggingDrawerKey.value)
    const toIdx = list.indexOf(targetKey)
    if (fromIdx > -1 && toIdx > -1) {
      list.splice(fromIdx, 1)
      list.splice(toIdx, 0, draggingDrawerKey.value)
      memoFieldKeys.value = list
      syncLocalLayoutCache()
    }
  } else if (activeTransferTab.value === 'header') {
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


// 判断字段是否属于已识别的 Code-Name 字典关联字段 (仅针对带有 validValues、RTable 关联表或 Y/N 枚举的字段提供模式切换)
const isCodeNameField = (key: string, tableKey?: string): boolean => {
  if (key === 'U_Close' || key === 'LineCls') return true
  const stripped = key.startsWith('U_') ? key.substring(2) : key

  if (tableKey && metaData.value?.childTableFields?.[tableKey]) {
    const childMap = metaData.value.childTableFields[tableKey]
    if (childMap[key]?.validValues && Object.keys(childMap[key].validValues).length > 0) return true
    if (childMap[stripped]?.validValues && Object.keys(childMap[stripped].validValues).length > 0) return true
  }

  if (metaData.value?.headerFields) {
    if (metaData.value.headerFields[key]?.validValues && Object.keys(metaData.value.headerFields[key].validValues).length > 0) return true
    if (metaData.value.headerFields[stripped]?.validValues && Object.keys(metaData.value.headerFields[stripped].validValues).length > 0) return true
  }

  if (metaData.value?.childTableFields) {
    for (const cMap of Object.values(metaData.value.childTableFields) as any[]) {
      if (cMap?.[key]?.validValues && Object.keys(cMap[key].validValues).length > 0) return true
      if (cMap?.[stripped]?.validValues && Object.keys(cMap[stripped].validValues).length > 0) return true
    }
  }

  if (metaData.value) {
    const cleanKey = key.startsWith('U_') ? key.substring(2) : key
    if (
      cleanKey.includes('ExpnsCode') ||
      cleanKey.includes('SlpCode') ||
      cleanKey.includes('GroupNum') ||
      cleanKey.includes('VatGroup')
    ) {
      return true
    }
  }

  return false
}


// 统一打开字段与列定制抽屉 (根据当前所处视图智能直达对应定制 Tab)
const openUnifiedCustomizationDrawer = () => {
  if (activeDocTab.value === 'tab_header' || activeDocTab.value === 'tab_json') {
    openTransferDrawer('header')
  } else if (activeDocTab.value.startsWith('tab_table_')) {
    const idx = parseInt(activeDocTab.value.replace('tab_table_', ''))
    const coll = processedCollections.value[idx]
    openTransferDrawer(coll?.key || 'header')
  } else {
    openTransferDrawer('header')
  }
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
    <!-- 1. 发票级紧凑 Hero 抬头凭证栏 (高度仅 ~75px，核心要素一眼尽览) -->
    <div class="summary-hero-card">
      <!-- 财务/库存过账草稿与借贷平衡微型胶囊 -->
      <div v-if="isDraftDocument || journalBalanceInfo" class="doc-special-status-bar">
        <div v-if="isDraftDocument" class="draft-badge-pill">
          <span class="draft-dot"></span>
          <span>📋 财务/库存过账草稿 (审批通过后自动过账至 SAP)</span>
        </div>

        <div v-if="journalBalanceInfo" class="journal-balance-pill" :class="[journalBalanceInfo.isBalanced ? 'balanced' : 'unbalanced']">
          <span v-if="journalBalanceInfo.isBalanced">⚖️ 凭证借贷平衡：借贷总额 {{ currencySymbol }} {{ journalBalanceInfo.totalDebit.toLocaleString('zh-CN', { minimumFractionDigits: 2 }) }}</span>
          <span v-else>⚠️ 借贷不平衡！借方 {{ currencySymbol }} {{ journalBalanceInfo.totalDebit }} ≠ 贷方 {{ currencySymbol }} {{ journalBalanceInfo.totalCredit }}</span>
        </div>
      </div>

      <div class="hero-main-row">
        <!-- 左侧：客户名称与核心置顶要素 -->
        <div class="hero-left-section">
          <div class="customer-title-row">
            <Building2 class="w-4 h-4 text-blue-600 mr-2 shrink-0" />
            <h3 class="customer-name" :title="parsedData.U_CardCode || parsedData.CardCode">
              <span v-if="fieldDisplayMode === 'CodeOnly'">{{ parsedData.U_CardCode || parsedData.CardCode || '未指定客户' }}</span>
              <span v-else>{{ parsedData.U_CardName || parsedData.CardName || parsedData.U_CardCode || '未指定客户' }}</span>
              <span v-if="fieldDisplayMode === 'NameAndCode' && (parsedData.U_CardCode || parsedData.CardCode)" class="customer-code-pill">
                {{ parsedData.U_CardCode || parsedData.CardCode }}
              </span>
            </h3>
          </div>

          <!-- 极简中性胶囊标签流 (过账日期、业务员、到期日、运费承担等) -->
          <div class="hero-tags-flow">
            <div
              v-for="pf in topPinnedFields"
              :key="pf.key"
              class="hero-meta-pill"
            >
              <span class="meta-pill-label">{{ pf.label }}:</span>
              <span class="meta-pill-val" :class="[pf.formatted.isTranslated ? 'text-emerald-700 font-semibold' : '']">
                {{ pf.formatted.display }}
              </span>
            </div>
          </div>
        </div>

        <!-- 右侧：发票级大字总金额与外币/本币双轨看板 -->
        <div class="hero-right-section">
          <div class="amount-label-row">
            <span class="text-xs text-slate-500 font-medium">
              单据总金额
              <span v-if="docAmountsSummary.isForeign" class="font-bold text-blue-700">({{ docAmountsSummary.cur }})</span>
              <span v-else>(本币 RMB)</span>
            </span>
            <span v-if="parsedData.DocNum || parsedData.DocEntry" class="doc-num-badge font-mono">
              #{{ parsedData.DocNum || parsedData.DocEntry }}
            </span>
          </div>

          <!-- 主金额大字 (外币单据显示外币符号，如 $ 600.00 USD；本币显示 ¥ 4,217.40) -->
          <div class="hero-total-amount">
            <span v-if="docAmountsSummary.isForeign">
              {{ currencySymbol }} {{ docAmountsSummary.fcAmount.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 4 }) }}
            </span>
            <span v-else>
              ¥ {{ docAmountsSummary.localAmount.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 4 }) }}
            </span>
          </div>

          <!-- 外币单据时：自动展示本币金额与当前汇率 -->
          <div v-if="docAmountsSummary.isForeign" class="foreign-convert-sub">
            <span>本币: <strong>¥ {{ docAmountsSummary.localAmount.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 4 }) }}</strong></span>
            <span v-if="docAmountsSummary.rate" class="sub-rate-tag">(汇率 {{ docAmountsSummary.rate }})</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. 多重备注与说明专属收敛便签栏 (默认紧凑单行，消除黄色刺眼色块，支持平滑展开) -->
    <div v-if="processedMemoFields.length > 0" class="memo-bar-wrapper">
      <div class="memo-bar-header" @click="toggleMemoZone">
        <div class="memo-bar-left">
          <FileText class="w-4 h-4 text-amber-600 mr-2 shrink-0" />
          <span class="font-bold text-slate-800 text-xs mr-2">单据备注与特别说明 ({{ processedMemoFields.length }} 项)</span>
          <!-- 默认展示首条核心备注摘要 -->
          <span class="memo-preview-text">
            <strong>{{ processedMemoFields[0].label }}:</strong> {{ processedMemoFields[0].value }}
          </span>
        </div>

        <div class="memo-bar-actions" @click.stop>
          <button class="btn-memo-toggle-flat" @click="toggleMemoZone">
            <span>{{ isMemoZoneExpanded ? '收起明细 ▲' : '展开全部 ▼' }}</span>
          </button>
        </div>
      </div>

      <!-- 展开时的多重备注网格卡片 -->
      <div v-show="isMemoZoneExpanded" class="memo-expanded-grid">
        <div
          v-for="mf in processedMemoFields"
          :key="mf.key"
          class="memo-expanded-card"
        >
          <div class="memo-card-top">
            <span class="memo-card-name">{{ mf.label }}</span>
            <span class="memo-card-field font-mono">{{ mf.key }}</span>
          </div>
          <div class="memo-card-content">
            {{ mf.value }}
          </div>
        </div>
      </div>
    </div>

    <!-- 3. 世界级明细表格一体化容器 (Tab 导航 + 行内搜索 + 密度调节 + 列定制) -->
    <div class="table-unified-card">
      <div class="table-toolbar-row">
        <!-- Tab 切换 -->
        <div class="table-tabs-group">
          <button
            v-for="(c, cIdx) in processedCollections"
            :key="c.key"
            :class="['table-tab-item', activeDocTab === ('tab_table_' + cIdx) ? 'active' : '']"
            @click="activeDocTab = 'tab_table_' + cIdx"
          >
            <Layers class="w-3.5 h-3.5 mr-1.5" />
            <span>{{ c.label }}</span>
            <span class="tab-badge-count">{{ c.processedRows.length }}</span>
          </button>

          <button
            :class="['table-tab-item', activeDocTab === 'tab_header' ? 'active' : '']"
            @click="activeDocTab = 'tab_header'"
          >
            <Tag class="w-3.5 h-3.5 mr-1.5" />
            <span>对象主表属性</span>
            <span class="tab-badge-count">{{ headerUdfFields.length }}</span>
          </button>

          <button
            :class="['table-tab-item', activeDocTab === 'tab_json' ? 'active' : '']"
            @click="activeDocTab = 'tab_json'"
          >
            <Code2 class="w-3.5 h-3.5 mr-1.5" />
            <span>原始快照 (JSON)</span>
          </button>
        </div>

        <!-- 表格右上角高效工具箱 (过滤、密度、列定制) -->
        <div class="table-tools-group">
          <!-- 实时行搜索 -->
          <div v-if="activeDocTab.startsWith('tab_table_')" class="table-search-box">
            <Search class="w-3.5 h-3.5 text-slate-400 mr-1.5 shrink-0" />
            <input
              v-model="tableSearchQuery"
              placeholder="搜索物料/颜色/规格..."
              class="table-search-input"
            />
            <button v-if="tableSearchQuery" class="btn-clear-search" @click="tableSearchQuery = ''">
              <X class="w-3 h-3 text-slate-400" />
            </button>
          </div>

          <!-- 密度切换器 -->
          <div v-if="activeDocTab.startsWith('tab_table_')" class="density-switch-group">
            <button
              :class="['density-btn', tableDensity === 'compact' ? 'active' : '']"
              @click="setTableDensity('compact')"
              title="紧凑模式 (高密浏览)"
            >
              紧凑
            </button>
            <button
              :class="['density-btn', tableDensity === 'normal' ? 'active' : '']"
              @click="setTableDensity('normal')"
              title="标准模式"
            >
              标准
            </button>
            <button
              :class="['density-btn', tableDensity === 'comfortable' ? 'active' : '']"
              @click="setTableDensity('comfortable')"
              title="舒适模式"
            >
              宽松
            </button>
          </div>

          <!-- 字段与列定制唯一统一全局入口 (可配置主表、行表、备注归集与呈现模式) -->
          <button
            class="btn-col-customize-pill"
            @click="openUnifiedCustomizationDrawer"
            title="统一配置对象主表属性、子表明细列、备注归集与关联字典显示模式"
          >
            <SlidersHorizontal class="w-3 h-3 text-blue-600 mr-1" />
            <span>字段与列定制</span>
          </button>
        </div>
      </div>

      <!-- 表格内容主体 (Sticky 表头 + 虚拟沉浸式滚动) -->
      <div class="table-viewport-wrapper">
        <template v-for="(c, cIdx) in processedCollections" :key="c.key">
          <ChildTableView v-if="activeDocTab === ('tab_table_' + cIdx)" :collection="c" :tableDensity="tableDensity" />
        </template>

        <!-- 对象主表属性网格 -->
        <div v-if="activeDocTab === 'tab_header'" class="header-fields-wrapper">
          <div class="header-fields-filter-bar">
            <Search class="w-3.5 h-3.5 text-slate-400 mr-2" />
            <input
              v-model="searchUdf"
              placeholder="搜索对象主表属性名 / 描述 / 取值..."
              class="header-search-input"
            />
            
          </div>

          <div class="modern-fields-grid">
            <div
              v-for="f in headerUdfFields"
              :key="f.key"
              class="modern-field-card"
              :class="[isFieldPinned(f.key) ? 'pinned' : '']"
            >
              <div class="card-top-line">
                <span class="field-title" :title="f.key">{{ f.label }}</span>
                <span v-if="f.key !== f.label" class="field-key-code font-mono">{{ f.key }}</span>
                <div class="card-mini-actions">
                  <button
                    class="btn-icon-action"
                    :class="[isFieldPinned(f.key) ? 'active' : '']"
                    @click="togglePinField(f.key)"
                    :title="isFieldPinned(f.key) ? '取消置顶' : '置顶固定到顶部看板'"
                  >
                    <component :is="isFieldPinned(f.key) ? Pin : PinOff" class="w-3 h-3" />
                  </button>
                  <button
                    class="btn-icon-action"
                    :class="[isFieldInMemo(f.key) ? 'active-memo' : '']"
                    @click="toggleMemoField(f.key)"
                    :title="isFieldInMemo(f.key) ? '从备注区移出' : '归入多重备注区'"
                  >
                    <FileText class="w-3 h-3" />
                  </button>
                </div>
              </div>
              <div class="card-value-box">
                <span v-if="f.formatted.isTranslated" class="val-translated">
                  {{ f.formatted.display }}
                </span>
                <span v-else class="val-normal">
                  {{ f.formatted.display }}
                </span>
              </div>
            </div>
          </div>
        </div>

        <!-- 原始 JSON 签名快照 -->
        <div v-if="activeDocTab === 'tab_json'" class="json-snapshot-wrapper">
          <pre class="json-pre-box font-mono">{{ JSON.stringify(parsedData, null, 2) }}</pre>
        </div>
      </div>
    </div>

    <!-- 4. 双栏穿梭式列定制中心抽屉 (保持全功能与多重备注、字段级显示模式配置) -->
    <Teleport to="body">
      <div
        v-if="showTransferDrawer"
        class="transfer-drawer-overlay"
        @click.self="showTransferDrawer = false"
      >
        <div class="transfer-drawer-container">
          <div class="drawer-header">
            <div class="drawer-title-row">
              <SlidersHorizontal class="w-5 h-5 text-blue-600 mr-2" />
              <h3 class="drawer-title">单据字段与列个性化定制中心</h3>
              <span class="drawer-object-tag">当前对象: <strong>{{ objectCode || 'CHORDR' }}</strong></span>
              <span v-if="isCustomizedByMe" class="badge-customized-me ml-2">已启用个人专属偏好</span>
            </div>
            <button class="btn-close-drawer" @click="showTransferDrawer = false">
              <X class="w-5 h-5 text-slate-500" />
            </button>
          </div>

          <!-- 抽屉 Tab 切换 -->
          <div class="drawer-tabs">
            <button
              :class="['drawer-tab', activeTransferTab === 'header' ? 'active' : '']"
              @click="activeTransferTab = 'header'"
            >
              <Tag class="w-4 h-4 mr-1.5" />
              <span>对象主表属性 ({{ allHeaderFieldsList.length }} 项)</span>
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

            <button
              :class="['drawer-tab', activeTransferTab === 'memo' ? 'active' : '']"
              @click="activeTransferTab = 'memo'"
            >
              <FileText class="w-4 h-4 mr-1.5 text-amber-600" />
              <span>备注专区字段归集 ({{ memoFieldKeys.length }} 项)</span>
            </button>
          </div>

          <!-- 全局默认关联字典与代码呈现样式切换 -->
          <div class="display-mode-selector-bar">
            <div class="disp-mode-title">
              <SlidersHorizontal class="w-4 h-4 text-slate-600 mr-1.5" />
              <span class="font-bold text-slate-800 text-xs">全局关联字典与代码呈现样式:</span>
            </div>
            <div class="disp-mode-cards">
              <div
                class="mode-card-option"
                :class="[fieldDisplayMode === 'NameAndCode' ? 'active' : '']"
                @click="setDisplayMode('NameAndCode')"
              >
                <div class="mode-card-header">
                  <span class="mode-name">描述 (代码)</span>
                  <span class="mode-badge-rec">默认推荐</span>
                </div>
                <div class="mode-example">例：成品订单 (107)、月结 (18)、否 (N)</div>
              </div>

              <div
                class="mode-card-option"
                :class="[fieldDisplayMode === 'NameOnly' ? 'active' : '']"
                @click="setDisplayMode('NameOnly')"
              >
                <div class="mode-card-header">
                  <span class="mode-name">仅描述</span>
                  <span class="mode-badge-sub">极简清爽</span>
                </div>
                <div class="mode-example">例：成品订单、月结、否</div>
              </div>

              <div
                class="mode-card-option"
                :class="[fieldDisplayMode === 'CodeOnly' ? 'active' : '']"
                @click="setDisplayMode('CodeOnly')"
              >
                <div class="mode-card-header">
                  <span class="mode-name">仅代码</span>
                  <span class="mode-badge-code">高密代码</span>
                </div>
                <div class="mode-example">例：107、18、N</div>
              </div>
            </div>
          </div>

          <!-- 抽屉穿梭面板 -->
          <div class="drawer-body">
            <!-- 左栏：待选字段素材库 -->
            <div class="transfer-pane transfer-left">
              <div class="pane-header">
                <div class="pane-title">
                  <Search class="w-4 h-4 text-slate-500 mr-1.5" />
                  <span>待选字段素材库 ({{ transferLeftItems.length }})</span>
                </div>
                <button class="btn-link-action" @click="transferAddAll" title="一键全部添加">全部添加</button>
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
                >
                  <div class="item-info">
                    <div class="item-title-row">
                      <span class="item-label">{{ item.label }}</span>
                      <span class="item-key font-mono">{{ item.key }}</span>
                    </div>
                    <div v-if="item.sampleVal" class="item-sample-val">
                      样例: {{ item.sampleVal }}
                    </div>
                  </div>

                  <!-- 智能双态 Toggle 按钮：支持一键添加与一键取消，Hover 智能变身 -->
                  <button
                    v-if="!item.isAdded"
                    class="btn-toggle-action btn-action-add"
                    @click.stop="transferAddItem(item.key)"
                    title="点击添加至显示列表"
                  >
                    <Plus class="action-icon" />
                    <span>添加</span>
                  </button>
                  <button
                    v-else
                    class="btn-toggle-action btn-action-added"
                    @click.stop="transferRemoveItem(item.key)"
                    title="点击从显示列表中取消"
                  >
                    <Check class="action-icon icon-check" />
                    <X class="action-icon icon-remove" />
                    <span class="label-normal">已显示</span>
                    <span class="label-hover">取消</span>
                  </button>
                </div>
                <div v-if="transferLeftItems.length === 0" class="empty-list">未匹配到相关字段</div>
              </div>
            </div>

            <!-- 中间穿梭指示 -->
            <div class="transfer-divider">
              <ArrowRightLeft class="w-5 h-5 text-slate-400" />
            </div>

            <!-- 右栏：当前显示字段与排列顺序 (拖拽调序) -->
            <div class="transfer-pane transfer-right">
              <div class="pane-header">
                <div class="pane-title">
                  <CheckCheck class="w-4 h-4 text-emerald-600 mr-1.5" />
                  <span>当前显示字段与顺序 ({{ transferRightItems.length }})</span>
                </div>
                <button class="btn-link-action text-rose-600" @click="transferRemoveAll" title="一键全部清空">全部清空</button>
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
                <span>按住左侧抓手 <strong>上下拖拽</strong> 即可调序先后顺序</span>
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
                      <span v-if="item.isInMemo" class="memo-tag-mini">已归入备注区</span>
                    </div>
                  </div>

                  <div class="item-actions">
                    <!-- 单字段呈现模式下拉选择 -->
                    <select
                      v-if="isCodeNameField(item.key, activeTransferTab !== 'header' && activeTransferTab !== 'memo' ? activeTransferTab : undefined)"
                      class="field-mode-select"
                      :value="fieldDisplayOverrides[item.key] || 'Inherit'"
                      @change="setFieldDisplayOverride(item.key, ($event.target as HTMLSelectElement).value as any)"
                      title="单独设置该字段呈现格式"
                    >
                      <option value="Inherit">跟随全局</option>
                      <option value="NameAndCode">描述 (代码)</option>
                      <option value="NameOnly">仅描述</option>
                      <option value="CodeOnly">仅代码</option>
                    </select>

                    <button
                      class="btn-delete-item"
                      @click="transferRemoveItem(item.key)"
                      title="从当前显示中移除"
                    >
                      <Trash2 class="delete-icon" />
                    </button>
                  </div>
                </div>
                <div v-if="transferRightItems.length === 0" class="empty-list">当前未选择任何显示字段</div>
              </div>
            </div>
          </div>

          <div v-if="drawerToast" :class="['drawer-toast', drawerToast.type]">
            {{ drawerToast.text }}
          </div>

          <!-- 抽屉底部操作栏 -->
          <div class="drawer-footer">
            <button class="btn-restore-default" @click="resetAllLayoutToFactoryDefault">
              <RotateCcw class="w-4 h-4 mr-1" />
              <span>恢复全公司默认</span>
            </button>

            <div class="footer-save-btns">
              <button
                class="btn-save-user-pref"
                :disabled="isSavingLayout"
                @click="saveUserLayoutToServer"
              >
                <Save class="w-4 h-4 mr-1.5" />
                <span>{{ isSavingLayout ? '正在保存...' : '保存为我的个人偏好' }}</span>
              </button>

              <button
                class="btn-save-global-default"
                :disabled="isSavingLayout"
                @click="saveGlobalDefaultLayoutToServer"
                title="管理员权限：覆盖全公司新用户的默认排版规范"
              >
                <ShieldCheck class="w-4 h-4 mr-1.5" />
                <span>保存为全公司默认配置 (Admin)</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* ==========================================================================
   世界级全端 DPI & Windows 缩放自适应架构 (适配 4K、1080p、1366x768、150% 缩放笔记本与工厂触控终端)
   ========================================================================== */

.doc-viewer-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  width: 100%;
  gap: 8px;
  box-sizing: border-box;
}

/* 1. 发票级紧凑 Hero 抬头 (自适应单行/双行，高密度收敛) */
.summary-hero-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px 14px;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.02);
  flex-shrink: 0;
}

.hero-main-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.hero-left-section {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  flex: 1;
}

.customer-title-row {
  display: flex;
  align-items: center;
  min-width: 0;
}

.customer-name {
  font-size: 14px;
  font-weight: 700;
  color: #0f172a;
  margin: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: flex;
  align-items: center;
  gap: 6px;
}

.customer-code-pill {
  font-size: 10.5px;
  font-weight: 600;
  font-family: monospace;
  background: #f1f5f9;
  color: #475569;
  padding: 1px 5px;
  border-radius: 3px;
  border: 1px solid #e2e8f0;
}

.hero-tags-flow {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
}

.hero-meta-pill {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 3px;
  padding: 1px 6px;
  font-size: 11px;
  color: #334155;
  white-space: nowrap;
}

.meta-pill-label {
  color: #64748b;
  font-size: 10.5px;
}

.meta-pill-val {
  font-weight: 600;
  color: #0f172a;
}

/* 右侧发票级金额大字 (自适应字号) */
.hero-right-section {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  justify-content: center;
  flex-shrink: 0;
}

.amount-label-row {
  display: flex;
  align-items: center;
  gap: 5px;
}

.doc-num-badge {
  font-size: 10.5px;
  font-weight: 700;
  color: #2563eb;
  background: #eff6ff;
  border: 1px solid #dbeafe;
  padding: 0 4px;
  border-radius: 3px;
}

.hero-total-amount {
  font-size: 19px;
  font-weight: 800;
  color: #0f172a;
  letter-spacing: -0.5px;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Arial, sans-serif;
  line-height: 1.15;
}

/* 2. 多重备注收敛便签栏 */
.memo-bar-wrapper {
  background: #ffffff;
  border: 1px solid #fde68a;
  border-left: 3px solid #f59e0b;
  border-radius: 5px;
  padding: 5px 10px;
  flex-shrink: 0;
}

.memo-bar-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  cursor: pointer;
  user-select: none;
  min-height: 20px;
}

.memo-bar-left {
  display: flex;
  align-items: center;
  min-width: 0;
  flex: 1;
}

.memo-preview-text {
  font-size: 11.5px;
  color: #475569;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 60vw;
}

.memo-bar-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.btn-memo-action {
  display: inline-flex;
  align-items: center;
  padding: 1px 5px;
  background: #f8fafc;
  border: 1px solid #cbd5e1;
  border-radius: 3px;
  font-size: 10px;
  color: #475569;
  cursor: pointer;
}

.btn-memo-action:hover {
  background: #f1f5f9;
  color: #0f172a;
}

.btn-memo-toggle-flat {
  background: transparent;
  border: none;
  font-size: 10.5px;
  font-weight: 600;
  color: #d97706;
  cursor: pointer;
}

.memo-expanded-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 8px;
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px dashed #fde68a;
  max-height: 110px;
  overflow-y: auto;
}

.memo-expanded-grid::-webkit-scrollbar {
  width: 4px;
}

.memo-expanded-grid::-webkit-scrollbar-thumb {
  background: #fcd34d;
  border-radius: 2px;
}

.memo-expanded-card {
  background: #fffdf5;
  border: 1px solid #fef3c7;
  border-left: 3px solid #f59e0b;
  border-radius: 4px;
  padding: 6px 10px;
  display: flex;
  flex-direction: column;
}

.memo-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 3px;
}

.memo-card-name {
  font-size: 11px;
  font-weight: 700;
  color: #92400e;
}

.memo-card-field {
  font-size: 9.5px;
  color: #b45309;
}

.memo-card-content {
  font-size: 11.5px;
  color: #1e293b;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
}

/* 3. 一体化表格视窗容器 (Flex 1 自适应填满，高度永远贴合屏幕) */
.table-unified-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 200px;
  height: 0;
  overflow: hidden;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.02);
}

.table-toolbar-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 5px 10px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
  gap: 8px;
  flex-shrink: 0;
}

.table-tabs-group {
  display: flex;
  align-items: center;
  gap: 3px;
  overflow-x: auto;
}

.table-tab-item {
  display: inline-flex;
  align-items: center;
  padding: 4px 10px;
  font-size: 11.5px;
  font-weight: 600;
  color: #64748b;
  border: 1px solid transparent;
  border-radius: 4px;
  background: transparent;
  cursor: pointer;
  white-space: nowrap;
}

.table-tab-item:hover {
  color: #0f172a;
  background: #f1f5f9;
}

.table-tab-item.active {
  color: #2563eb;
  background: #ffffff;
  border-color: #cbd5e1;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.tab-badge-count {
  font-size: 9.5px;
  font-weight: 700;
  background: #e2e8f0;
  color: #475569;
  padding: 0 4px;
  border-radius: 8px;
  margin-left: 5px;
}

.table-tab-item.active .tab-badge-count {
  background: #dbeafe;
  color: #1d4ed8;
}

/* 工具箱 (搜索、密度、定制) */
.table-tools-group {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.table-search-box {
  display: flex;
  align-items: center;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 3px;
  padding: 2px 6px;
  width: 150px;
}

.table-search-box:focus-within {
  border-color: #3b82f6;
  box-shadow: 0 0 0 1px rgba(59, 130, 246, 0.2);
}

.table-search-input {
  border: none;
  outline: none;
  font-size: 11px;
  width: 100%;
  color: #0f172a;
  background: transparent;
}

.btn-clear-search {
  background: transparent;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  padding: 0;
}

.density-switch-group {
  display: flex;
  background: #f1f5f9;
  border: 1px solid #e2e8f0;
  border-radius: 3px;
  padding: 1px;
}

.density-btn {
  border: none;
  background: transparent;
  font-size: 10px;
  font-weight: 600;
  color: #64748b;
  padding: 1px 5px;
  border-radius: 2px;
  cursor: pointer;
}

.density-btn.active {
  background: #ffffff;
  color: #0f172a;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

.btn-col-customize-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.3em;
  padding: 3px 8px;
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 600;
  color: #1d4ed8;
  cursor: pointer;
}

.btn-col-customize-pill svg {
  width: 1em;
  height: 1em;
  flex-shrink: 0;
  vertical-align: -0.1em;
}

.btn-col-customize-pill:hover {
  background: #dbeafe;
}

/* 4. 数据表格 Sticky 视窗 (极客级虚拟弹性伸缩) */
.table-viewport-wrapper {
  position: relative;
  flex: 1;
  min-height: 0;
  height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.collection-table-box {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}

.table-scroll-container {
  flex: 1;
  min-height: 0;
  overflow: auto;
  position: relative;
}

.modern-grid-table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: 11.5px;
  text-align: left;
}

.modern-grid-table thead {
  position: sticky;
  top: 0;
  z-index: 10;
}

.modern-grid-table thead th {
  background: #f8fafc;
  color: #475569;
  font-weight: 700;
  font-size: 11px;
  padding: 6px 8px;
  border-bottom: 2px solid #cbd5e1;
  white-space: nowrap;
}

.th-seq-col, .td-seq-col {
  width: 32px;
  text-align: center;
  color: #94a3b8;
}

.grid-row:hover td {
  background-color: #f8fafc;
}

.grid-row td {
  border-bottom: 1px solid #f1f5f9;
  padding: 5px 8px;
  color: #1e293b;
  white-space: nowrap;
}

/* 密度控制 */
.modern-grid-table.density-compact td {
  padding: 3px 6px;
  font-size: 10.5px;
}
.modern-grid-table.density-normal td {
  padding: 5px 8px;
  font-size: 11.5px;
}
.modern-grid-table.density-comfortable td {
  padding: 8px 10px;
  font-size: 12px;
}

.align-right {
  text-align: right;
}

.cell-translated-badge {
  color: #047857;
  font-weight: 600;
}

.status-cell-closed {
  background: #fef3c7;
  color: #92400e;
  font-weight: 700;
}

/* 5. 主表 54 项卡片网格 */
.header-fields-wrapper {
  padding: 10px;
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}

.header-fields-filter-bar {
  display: flex;
  align-items: center;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 4px;
  padding: 4px 10px;
  margin-bottom: 8px;
  flex-shrink: 0;
}

.header-search-input {
  border: none;
  outline: none;
  background: transparent;
  font-size: 11.5px;
  width: 260px;
  color: #0f172a;
}

.modern-fields-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 6px;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.modern-field-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 4px;
  padding: 6px 8px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.modern-field-card.pinned {
  border-color: #93c5fd;
  background: #f8fbff;
}

.card-top-line {
  display: flex;
  align-items: center;
  gap: 4px;
}

.field-title {
  font-size: 11px;
  font-weight: 700;
  color: #334155;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
}

.field-key-code {
  font-size: 9px;
  color: #94a3b8;
}

.card-mini-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.btn-icon-action {
  border: none;
  background: transparent;
  padding: 1px;
  border-radius: 2px;
  color: #94a3b8;
  cursor: pointer;
}

.btn-icon-action:hover {
  color: #0f172a;
  background: #f1f5f9;
}

.btn-icon-action.active {
  color: #2563eb;
}

.btn-icon-action.active-memo {
  color: #d97706;
}

.card-value-box {
  font-size: 11.5px;
  color: #0f172a;
  word-break: break-all;
  min-height: 16px;
}

.val-translated {
  color: #047857;
  font-weight: 600;
}

.json-snapshot-wrapper {
  padding: 10px;
  flex: 1;
  min-height: 0;
  overflow: auto;
}

.json-pre-box {
  background: #0f172a;
  color: #e2e8f0;
  padding: 10px;
  border-radius: 4px;
  font-size: 11px;
}

/* ==========================================================================
   响应式断点：针对笔记本低分辨率 (1366x768) 与 Windows 高缩放 (125% / 150%) 深度调优
   ========================================================================== */
@media (max-height: 768px) {
  .doc-viewer-container {
    gap: 6px;
  }
  .summary-hero-card {
    padding: 6px 10px;
  }
  .customer-name {
    font-size: 13px;
  }
  .hero-total-amount {
    font-size: 17px;
  }
  .hero-meta-pill {
    padding: 1px 4px;
    font-size: 10.5px;
  }
  .table-toolbar-row {
    padding: 3px 8px;
  }
  .table-tab-item {
    padding: 3px 8px;
    font-size: 11px;
  }
  .modern-grid-table thead th {
    padding: 4px 6px;
  }
  .modern-grid-table.density-normal td {
    padding: 4px 6px;
    font-size: 11px;
  }
}

/* 6. 列定制抽屉样式 (保持精致) */
.transfer-drawer-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
}

.transfer-drawer-container {
  width: calc(780px / var(--app-font-scale, 1));
  max-width: calc(92vw / var(--app-font-scale, 1));
  height: calc(100vh / var(--app-font-scale, 1));
  background: #ffffff;
  display: flex;
  flex-direction: column;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.12);
  zoom: var(--app-font-scale, 1);
  transform-origin: top right;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  border-bottom: 1px solid #e2e8f0;
}

.drawer-title-row {
  display: flex;
  align-items: center;
}

.drawer-title {
  font-size: 15px;
  font-weight: 700;
  color: #0f172a;
  margin: 0;
}

.drawer-object-tag {
  font-size: 11px;
  color: #64748b;
  margin-left: 8px;
  background: #f1f5f9;
  padding: 1px 6px;
  border-radius: 3px;
}

.badge-customized-me {
  font-size: 10.5px;
  background: #dbeafe;
  color: #1d4ed8;
  padding: 1px 5px;
  border-radius: 3px;
  font-weight: 600;
}

.btn-close-drawer {
  background: transparent;
  border: none;
  cursor: pointer;
}

.drawer-tabs {
  display: flex;
  gap: 4px;
  padding: 6px 16px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
}

.drawer-tab {
  display: inline-flex;
  align-items: center;
  padding: 5px 10px;
  font-size: 11.5px;
  font-weight: 600;
  color: #64748b;
  border: 1px solid transparent;
  border-radius: 4px;
  background: transparent;
  cursor: pointer;
}

.drawer-tab.active {
  background: #ffffff;
  color: #2563eb;
  border-color: #cbd5e1;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.display-mode-selector-bar {
  padding: 8px 16px;
  background: #fafbfc;
  border-bottom: 1px solid #f1f5f9;
}

.disp-mode-cards {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
  margin-top: 4px;
}

.mode-card-option {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 4px;
  padding: 6px 8px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.mode-card-option.active {
  border-color: #3b82f6;
  background: #eff6ff;
  box-shadow: 0 0 0 1px #3b82f6;
}

.mode-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2px;
}

.mode-name {
  font-size: 11px;
  font-weight: 700;
  color: #0f172a;
}

.mode-badge-rec {
  font-size: 9px;
  background: #dbeafe;
  color: #1d4ed8;
  padding: 0 3px;
  border-radius: 2px;
  font-weight: 600;
}

.mode-badge-sub {
  font-size: 9px;
  background: #dcfce7;
  color: #15803d;
  padding: 0 3px;
  border-radius: 2px;
  font-weight: 600;
}

.mode-badge-code {
  font-size: 9px;
  background: #f3e8ff;
  color: #7e22ce;
  padding: 0 3px;
  border-radius: 2px;
  font-weight: 600;
}

.mode-example {
  font-size: 10px;
  color: #64748b;
}

.drawer-body {
  flex: 1;
  display: flex;
  overflow: hidden;
  padding: 10px 16px;
  gap: 10px;
}

.transfer-pane {
  flex: 1;
  display: flex;
  flex-direction: column;
  border: 1px solid #e2e8f0;
  border-radius: 5px;
  overflow: hidden;
  background: #ffffff;
}

.pane-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 10px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
}

.pane-title {
  font-size: 11.5px;
  font-weight: 700;
  color: #334155;
  display: flex;
  align-items: center;
}

.btn-link-action {
  font-size: 10.5px;
  color: #2563eb;
  background: transparent;
  border: none;
  cursor: pointer;
  font-weight: 600;
}

.pane-search {
  display: flex;
  align-items: center;
  padding: 4px 8px;
  border-bottom: 1px solid #f1f5f9;
}

.pane-search-input {
  border: none;
  outline: none;
  font-size: 11px;
  width: 100%;
}

.reorder-tip-bar {
  display: flex;
  align-items: center;
  background: #eff6ff;
  color: #1d4ed8;
  font-size: 10px;
  padding: 3px 8px;
  border-bottom: 1px solid #dbeafe;
}

.transfer-list {
  flex: 1;
  overflow-y: auto;
  padding: 4px;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.transfer-item {
  display: flex;
  align-items: center;
  padding: 5px 8px;
  border: 1px solid #f1f5f9;
  border-radius: 4px;
  background: #ffffff;
  min-height: 34px;
  transition: all 0.15s ease;
}

.transfer-item:hover {
  background: #f8fafc;
  border-color: #cbd5e1;
}

.transfer-item.dragging-source {
  opacity: 0.4;
  border: 1px dashed #3b82f6;
}

.reorder-grip {
  display: flex;
  align-items: center;
  gap: 3px;
  margin-right: 6px;
  color: #94a3b8;
  user-select: none;
}

.reorder-grip svg {
  width: 13px;
  height: 13px;
  color: #94a3b8;
}

.order-seq {
  font-size: 10px;
  color: #64748b;
  width: 16px;
  text-align: right;
  font-weight: 500;
}

.item-info {
  flex: 1;
  min-width: 0;
}

.item-title-row {
  display: flex;
  align-items: center;
  gap: 4px;
}

.item-label {
  font-size: 11.5px;
  font-weight: 500;
  color: #0f172a;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.item-key {
  font-size: 9.5px;
  color: #94a3b8;
}

.item-sample-val {
  font-size: 10px;
  color: #64748b;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-top: 1px;
}

/* 智能双态 Toggle 按钮系统 (世界级微交互) */
.btn-toggle-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 3px;
  height: 22px;
  padding: 0 8px;
  font-size: 11px;
  font-weight: 500;
  border-radius: 4px;
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.18s cubic-bezier(0.4, 0, 0.2, 1);
  user-select: none;
  flex-shrink: 0;
}

.btn-toggle-action .action-icon {
  width: 12px;
  height: 12px;
  flex-shrink: 0;
}

/* 1. 未添加状态 (添加按钮) */
.btn-action-add {
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  color: #1d4ed8;
}

.btn-action-add:hover {
  background: #dbeafe;
  border-color: #93c5fd;
  color: #1e40af;
  box-shadow: 0 1px 2px rgba(37, 99, 235, 0.1);
}

/* 2. 已添加状态 (智能悬浮变身按钮: 默认绿[已显示] / 悬浮红[取消]) */
.btn-action-added {
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #15803d;
}

.btn-action-added .icon-remove,
.btn-action-added .label-hover {
  display: none;
}

.btn-action-added:hover {
  background: #fef2f2;
  border-color: #fecaca;
  color: #dc2626;
  box-shadow: 0 1px 2px rgba(220, 38, 38, 0.08);
}

.btn-action-added:hover .icon-check,
.btn-action-added:hover .label-normal {
  display: none;
}

.btn-action-added:hover .icon-remove,
.btn-action-added:hover .label-hover {
  display: inline-flex;
}

.item-actions {
  display: flex;
  align-items: center;
  gap: 4px;
}

.field-mode-select {
  font-size: 10px;
  height: 22px;
  padding: 1px 4px;
  border: 1px solid #cbd5e1;
  border-radius: 3px;
  background: #f8fafc;
  color: #334155;
  cursor: pointer;
  outline: none;
  transition: all 0.15s ease;
}

.field-mode-select:hover,
.field-mode-select:focus {
  border-color: #3b82f6;
  background: #ffffff;
}

/* 右侧垃圾桶微型删除按钮 */
.btn-delete-item {
  width: 22px;
  height: 22px;
  border-radius: 4px;
  border: none;
  background: transparent;
  color: #94a3b8;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s ease;
  flex-shrink: 0;
  padding: 0;
}

.btn-delete-item .delete-icon {
  width: 13px;
  height: 13px;
  transition: transform 0.15s ease;
}

.btn-delete-item:hover {
  background: #fee2e2;
  color: #e11d48;
}

.btn-delete-item:hover .delete-icon {
  transform: scale(1.1);
}

.transfer-divider {
  display: flex;
  align-items: center;
  justify-content: center;
}

.empty-list {
  text-align: center;
  color: #94a3b8;
  font-size: 11px;
  padding: 18px 0;
}

.drawer-toast {
  margin: 0 16px 6px 16px;
  padding: 4px 10px;
  border-radius: 3px;
  font-size: 11px;
  font-weight: 600;
}

.drawer-toast.success {
  background: #ecfdf5;
  color: #047857;
  border: 1px solid #a7f3d0;
}

.drawer-toast.error {
  background: #fef2f2;
  color: #b91c1c;
  border: 1px solid #fecaca;
}

.drawer-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  border-top: 1px solid #e2e8f0;
  background: #f8fafc;
}

.btn-restore-default {
  display: inline-flex;
  align-items: center;
  padding: 5px 10px;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 11.5px;
  color: #475569;
  cursor: pointer;
}

.footer-save-btns {
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-save-user-pref {
  display: inline-flex;
  align-items: center;
  padding: 6px 14px;
  background: #2563eb;
  color: #ffffff;
  border: none;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
}

.btn-save-user-pref:hover {
  background: #1d4ed8;
}

.btn-save-global-default {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  background: #0f172a;
  color: #ffffff;
  border: none;
  border-radius: 4px;
  font-size: 11.5px;
  font-weight: 600;
  cursor: pointer;
}

.btn-save-global-default:hover {
  background: #1e293b;
}

.pinned-tag-mini {
  font-size: 9px;
  background: #dbeafe;
  color: #1d4ed8;
  padding: 0 3px;
  border-radius: 2px;
  font-weight: 600;
}

.memo-tag-mini {
  font-size: 9px;
  background: #fef3c7;
  color: #92400e;
  padding: 0 3px;
  border-radius: 2px;
  font-weight: 600;
}
</style>
