import { ref } from 'vue'

export interface FontScalePreset {
  value: number
  label: string
  shortLabel: string
  description: string
}

export const FONT_SCALE_PRESETS: FontScalePreset[] = [
  {
    value: 90,
    label: '紧凑模式',
    shortLabel: '紧凑 (90%)',
    description: '适合 1080p 笔记本与密集单据核对'
  },
  {
    value: 100,
    label: '标准模式',
    shortLabel: '标准 (100%)',
    description: '官方推荐出厂标准，平衡典雅'
  },
  {
    value: 115,
    label: '舒适大字',
    shortLabel: '舒适 (115%)',
    description: '清晰轻松，适合 2K/4K 屏幕日常审阅'
  },
  {
    value: 125,
    label: '特大字号',
    shortLabel: '特大 (125%)',
    description: '视力关怀，适合年长高管大屏审阅'
  }
]

// 最小与最大滑动范围
export const MIN_FONT_SCALE = 85
export const MAX_FONT_SCALE = 135
export const DEFAULT_FONT_SCALE = 100

// 全局响应式字号缩放比例 (85 ~ 135)
const currentFontScale = ref<number>(DEFAULT_FONT_SCALE)
const currentUsername = ref<string>('manager')

/**
 * 应用缩放比例到 DOM 根节点
 */
const applyFontScaleToDom = (scale: number) => {
  if (typeof document === 'undefined') return
  
  const factor = scale / 100
  document.documentElement.style.setProperty('--app-font-scale', factor.toString())
  
  // 匹配档位名
  let level = 'standard'
  if (scale <= 92) level = 'compact'
  else if (scale >= 122) level = 'large'
  else if (scale >= 110) level = 'comfortable'
  
  document.documentElement.setAttribute('data-font-size', level)
}

/**
 * 加载特定账号的字号偏好 (按账号物理隔离)
 */
export const loadUserFontSize = (username: string) => {
  currentUsername.value = username || 'manager'
  
  // 1. 优先读取数值百分比配置
  const savedPct = localStorage.getItem(`sap_b1_font_scale_pct_${currentUsername.value}`)
  if (savedPct) {
    const num = parseInt(savedPct, 10)
    if (!isNaN(num) && num >= MIN_FONT_SCALE && num <= MAX_FONT_SCALE) {
      currentFontScale.value = num
      applyFontScaleToDom(currentFontScale.value)
      return
    }
  }

  // 2. 兼容历史的档位命名
  const savedLevel = localStorage.getItem(`sap_b1_font_scale_${currentUsername.value}`)
  if (savedLevel === 'compact') currentFontScale.value = 90
  else if (savedLevel === 'comfortable') currentFontScale.value = 115
  else if (savedLevel === 'large') currentFontScale.value = 125
  else currentFontScale.value = DEFAULT_FONT_SCALE

  applyFontScaleToDom(currentFontScale.value)
}

/**
 * 设置特定账号的字号偏好并保存
 */
export const setUserFontScale = (scale: number, username?: string) => {
  const user = username || currentUsername.value || 'manager'
  const clamped = Math.max(MIN_FONT_SCALE, Math.min(MAX_FONT_SCALE, Math.round(scale)))
  currentFontScale.value = clamped
  localStorage.setItem(`sap_b1_font_scale_pct_${user}`, clamped.toString())
  applyFontScaleToDom(clamped)
}

/**
 * 步进调整 (+5% 或 -5%)
 */
export const stepUserFontScale = (delta: number, username?: string) => {
  setUserFontScale(currentFontScale.value + delta, username)
}

export function useFontSize() {
  return {
    currentFontScale,
    currentUsername,
    minScale: MIN_FONT_SCALE,
    maxScale: MAX_FONT_SCALE,
    presets: FONT_SCALE_PRESETS,
    setUserFontScale,
    stepUserFontScale,
    loadUserFontSize
  }
}
