<template>
  <div class="font-size-switcher-wrapper" ref="containerRef">
    <!-- 顶部常驻胶囊触发按钮 -->
    <button
      class="font-switcher-btn"
      :class="{ 'active': isOpen }"
      @click.stop="toggleDropdown"
      :title="'当前全局缩放: ' + currentFontScale + '% (点击拖动滑块调节)'"
    >
      <span class="aa-icon">Aa</span>
      <span class="font-label font-mono font-bold">{{ currentFontScale }}%</span>
      <ChevronDown class="chevron-icon" :class="{ 'rotate': isOpen }" />
    </button>

    <!-- 下拉滑块调节面板 -->
    <transition name="dropdown-pop">
      <div v-if="isOpen" class="font-dropdown-panel card shadow-lg" @click.stop>
        <!-- 头部 -->
        <div class="panel-header">
          <div class="panel-title-wrap">
            <SlidersHorizontal class="w-4 h-4 text-blue-600 mr-1.5" />
            <span class="panel-title">全局字号与界面缩放</span>
          </div>
          <span class="user-tag">{{ currentUsername }} 专属偏好</span>
        </div>

        <div class="panel-desc">
          支持连续无级拖动，适配不同年龄视力与高分大屏
        </div>

        <!-- 核心：滑块连续调节区 -->
        <div class="slider-control-box">
          <div class="slider-header-row">
            <span class="scale-label">当前缩放:</span>
            <div class="scale-value-pill">
              <span class="scale-num font-mono">{{ currentFontScale }}%</span>
              <span class="scale-name">{{ currentPresetName }}</span>
            </div>
          </div>

          <div class="slider-row">
            <button
              class="btn-step"
              @click="stepUserFontScale(-5)"
              title="减小 5%"
              :disabled="currentFontScale <= minScale"
            >
              <span class="step-text">A-</span>
            </button>

            <div class="range-slider-wrap">
              <input
                type="range"
                :min="minScale"
                :max="maxScale"
                step="1"
                :value="currentFontScale"
                @input="onSliderInput"
                class="modern-range-slider"
              />
              <div class="range-ticks">
                <span
                  v-for="p in presets"
                  :key="p.value"
                  class="tick-mark"
                  :style="{ left: ((p.value - minScale) / (maxScale - minScale) * 100) + '%' }"
                ></span>
              </div>
            </div>

            <button
              class="btn-step"
              @click="stepUserFontScale(5)"
              title="增大 5%"
              :disabled="currentFontScale >= maxScale"
            >
              <span class="step-text">A+</span>
            </button>
          </div>

          <!-- 4 档常用快捷吸附胶囊 -->
          <div class="presets-row">
            <button
              v-for="p in presets"
              :key="p.value"
              class="btn-preset-pill"
              :class="{ 'active': currentFontScale === p.value }"
              @click="setUserFontScale(p.value)"
            >
              {{ p.shortLabel }}
            </button>
          </div>
        </div>

        <!-- 实时文字排版预览框 -->
        <div class="preview-box">
          <div class="preview-title">实时效果预览:</div>
          <div class="preview-content" :style="{ fontSize: (13 * currentFontScale / 100) + 'px' }">
            <div class="preview-line-main">
              <strong>型号订单 #1709</strong> 邢台金宗泽服装有限公司
            </div>
            <div class="preview-line-sub">
              单据总金额: <span class="text-blue-600 font-mono font-bold">¥ 4,839.54</span> · 审批状态: 待审批
            </div>
          </div>
        </div>

        <!-- 底部 -->
        <div class="panel-footer">
          <span class="hint-text">💡 拖动即时生效，换电脑自动同步该账号设置</span>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useFontSize } from '../../composables/useFontSize'
import { SlidersHorizontal, ChevronDown } from 'lucide-vue-next'

const {
  currentFontScale,
  currentUsername,
  minScale,
  maxScale,
  presets,
  setUserFontScale,
  stepUserFontScale
} = useFontSize()

const isOpen = ref(false)
const containerRef = ref<HTMLElement | null>(null)

const currentPresetName = computed(() => {
  const matched = presets.find(p => p.value === currentFontScale.value)
  if (matched) return matched.label
  if (currentFontScale.value <= 92) return '紧凑精简'
  if (currentFontScale.value >= 122) return '特大清晰'
  if (currentFontScale.value >= 108) return '舒适大字'
  return '自定义'
})

const toggleDropdown = () => {
  isOpen.value = !isOpen.value
}

const onSliderInput = (e: Event) => {
  const val = Number((e.target as HTMLInputElement).value)
  setUserFontScale(val)
}

// 点击外部区域自动收起
const onDocumentClick = (e: MouseEvent) => {
  if (containerRef.value && !containerRef.value.contains(e.target as Node)) {
    isOpen.value = false
  }
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocumentClick)
})
</script>

<style scoped>
.font-size-switcher-wrapper {
  position: relative;
  display: inline-flex;
  align-items: center;
}

.font-switcher-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 26px;
  padding: 0 8px;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 11.5px;
  color: #334155;
  cursor: pointer;
  transition: all 0.15s ease;
  user-select: none;
}

.font-switcher-btn:hover,
.font-switcher-btn.active {
  background: #eff6ff;
  border-color: #93c5fd;
  color: #1d4ed8;
}

.aa-icon {
  font-weight: 800;
  font-size: 12px;
  color: #2563eb;
  letter-spacing: -0.5px;
}

.font-label {
  font-size: 11.5px;
}

.chevron-icon {
  width: 12px;
  height: 12px;
  color: #94a3b8;
  transition: transform 0.2s ease;
}

.chevron-icon.rotate {
  transform: rotate(180deg);
}

/* 下拉弹窗面板 */
.font-dropdown-panel {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  width: 320px;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px 14px;
  z-index: 1000;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.12), 0 8px 10px -6px rgba(0, 0, 0, 0.05);
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 2px;
}

.panel-title-wrap {
  display: flex;
  align-items: center;
}

.panel-title {
  font-size: 13px;
  font-weight: 700;
  color: #0f172a;
}

.user-tag {
  font-size: 10px;
  padding: 1px 6px;
  background: #f1f5f9;
  color: #475569;
  border-radius: 10px;
  font-family: ui-monospace, monospace;
}

.panel-desc {
  font-size: 10.5px;
  color: #64748b;
  margin-bottom: 12px;
}

/* 滑块控制容器 */
.slider-control-box {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 12px;
  margin-bottom: 10px;
}

.slider-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.scale-label {
  font-size: 11.5px;
  color: #475569;
  font-weight: 500;
}

.scale-value-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: #dbeafe;
  padding: 2px 8px;
  border-radius: 12px;
}

.scale-num {
  font-size: 12px;
  font-weight: 700;
  color: #1e40af;
}

.scale-name {
  font-size: 10.5px;
  color: #2563eb;
  font-weight: 600;
}

.slider-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
}

.btn-step {
  width: 26px;
  height: 26px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.15s ease;
  flex-shrink: 0;
}

.btn-step:hover:not(:disabled) {
  background: #eff6ff;
  border-color: #3b82f6;
  color: #2563eb;
}

.btn-step:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.step-text {
  font-size: 11px;
  font-weight: 700;
}

.range-slider-wrap {
  position: relative;
  flex: 1;
  display: flex;
  align-items: center;
}

.modern-range-slider {
  width: 100%;
  height: 5px;
  border-radius: 3px;
  background: #cbd5e1;
  outline: none;
  -webkit-appearance: none;
  cursor: pointer;
  position: relative;
  z-index: 2;
}

.modern-range-slider::-webkit-slider-thumb {
  -webkit-appearance: none;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: #2563eb;
  border: 2px solid #ffffff;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.25);
  cursor: pointer;
  transition: transform 0.1s ease;
}

.modern-range-slider::-webkit-slider-thumb:hover {
  transform: scale(1.2);
  background: #1d4ed8;
}

.range-ticks {
  position: absolute;
  top: 50%;
  left: 0;
  right: 0;
  height: 5px;
  pointer-events: none;
  transform: translateY(-50%);
}

.tick-mark {
  position: absolute;
  width: 3px;
  height: 7px;
  background: #94a3b8;
  border-radius: 1px;
  transform: translateX(-50%) translateY(-1px);
}

.presets-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 4px;
}

.btn-preset-pill {
  padding: 3px 0;
  font-size: 10.5px;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 3px;
  color: #475569;
  cursor: pointer;
  transition: all 0.15s ease;
  text-align: center;
}

.btn-preset-pill:hover {
  background: #eff6ff;
  border-color: #93c5fd;
  color: #1d4ed8;
}

.btn-preset-pill.active {
  background: #2563eb;
  border-color: #2563eb;
  color: #ffffff;
  font-weight: 600;
}

/* 实时预览区 */
.preview-box {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px 10px;
  margin-bottom: 10px;
}

.preview-title {
  font-size: 10px;
  color: #94a3b8;
  margin-bottom: 4px;
}

.preview-content {
  line-height: 1.4;
  color: #1e293b;
  transition: font-size 0.1s ease;
}

.preview-line-main {
  font-weight: 500;
  margin-bottom: 2px;
}

.preview-line-sub {
  color: #64748b;
}

.panel-footer {
  border-top: 1px solid #f1f5f9;
  padding-top: 8px;
  text-align: center;
}

.hint-text {
  font-size: 10.5px;
  color: #94a3b8;
}

/* 动效 */
.dropdown-pop-enter-active,
.dropdown-pop-leave-active {
  transition: all 0.18s cubic-bezier(0.16, 1, 0.3, 1);
}

.dropdown-pop-enter-from,
.dropdown-pop-leave-to {
  opacity: 0;
  transform: translateY(-6px) scale(0.97);
}
</style>
