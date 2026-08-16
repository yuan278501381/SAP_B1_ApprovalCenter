import { ref } from 'vue'

export type FontSizeLevel = 'compact' | 'standard' | 'comfortable' | 'large'

export interface FontSizeOption {
  value: FontSizeLevel
  label: string
  shortLabel: string
  scalePercent: string
  description: string
  samplePx: string
}

export const FONT_SIZE_OPTIONS: FontSizeOption[] = [
  {
    value: 'compact',
    label: '紧凑模式',
    shortLabel: '紧凑 (90%)',
    scalePercent: '90%',
    description: '高信息密度，适合 1080p 笔记本与密集核算',
    samplePx: '11.5px'
  },
  {
    value: 'standard',
    label: '标准模式',
    shortLabel: '标准 (100%)',
    scalePercent: '100%',
    description: '官方推荐出厂标准，平衡典雅',
    samplePx: '13px'
  },
  {
    value: 'comfortable',
    label: '舒适大字',
    shortLabel: '舒适 (112%)',
    scalePercent: '112%',
    description: '清晰轻松，适合 2K/4K 屏幕日常审阅',
    samplePx: '14.5px'
  },
  {
    value: 'large',
    label: '特大字号',
    shortLabel: '特大 (125%)',
    scalePercent: '125%',
    description: '视力关怀，适合高管大屏审阅与高分屏',
    samplePx: '16px'
  }
]

// 全局响应式字号状态
const currentFontSize = ref<FontSizeLevel>('standard')
const currentUsername = ref<string>('manager')

/**
 * 应用字号到 DOM 根节点并持久化
 */
const applyFontSizeToDom = (size: FontSizeLevel) => {
  if (typeof document !== 'undefined') {
    document.documentElement.setAttribute('data-font-size', size)
  }
}

/**
 * 加载特定账号的字号偏好 (按账号隔离)
 */
export const loadUserFontSize = (username: string) => {
  currentUsername.value = username || 'manager'
  const saved = localStorage.getItem(`sap_b1_font_scale_${currentUsername.value}`) as FontSizeLevel
  if (saved && ['compact', 'standard', 'comfortable', 'large'].includes(saved)) {
    currentFontSize.value = saved
  } else {
    // 兼容旧的全局配置，否则默认 standard
    const legacy = localStorage.getItem('sap_b1_global_font_scale') as FontSizeLevel
    currentFontSize.value = (legacy && ['compact', 'standard', 'comfortable', 'large'].includes(legacy)) ? legacy : 'standard'
  }
  applyFontSizeToDom(currentFontSize.value)
}

/**
 * 设置特定账号的字号偏好并保存
 */
export const setUserFontSize = (size: FontSizeLevel, username?: string) => {
  const user = username || currentUsername.value || 'manager'
  currentFontSize.value = size
  localStorage.setItem(`sap_b1_font_scale_${user}`, size)
  applyFontSizeToDom(size)
}

/**
 * 循环切换下一档字号
 */
export const cycleUserFontSize = (username?: string) => {
  const order: FontSizeLevel[] = ['compact', 'standard', 'comfortable', 'large']
  const curIdx = order.indexOf(currentFontSize.value)
  const nextIdx = (curIdx + 1) % order.length
  setUserFontSize(order[nextIdx], username)
  return order[nextIdx]
}

export function useFontSize() {
  return {
    currentFontSize,
    currentUsername,
    fontSizeOptions: FONT_SIZE_OPTIONS,
    setUserFontSize,
    loadUserFontSize,
    cycleUserFontSize
  }
}
