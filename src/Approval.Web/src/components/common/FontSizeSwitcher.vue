<template>
  <div class="font-size-switcher-wrapper" ref="containerRef">
    <button
      class="font-switcher-btn"
      :class="{ 'active': isOpen }"
      @click.stop="toggleDropdown"
      :title="'当前全局字号: ' + currentOption.label + ' (' + currentOption.scalePercent + ') - 点击调整'"
    >
      <span class="aa-icon">Aa</span>
      <span class="font-label">{{ currentOption.shortLabel }}</span>
      <ChevronDown class="chevron-icon" :class="{ 'rotate': isOpen }" />
    </button>

    <!-- 下拉调节面板 -->
    <transition name="dropdown-pop">
      <div v-if="isOpen" class="font-dropdown-panel card shadow-lg" @click.stop>
        <div class="panel-header">
          <div class="panel-title-wrap">
            <Type class="w-4 h-4 text-blue-600 mr-1.5" />
            <span class="panel-title">全局显示字号调节</span>
          </div>
          <span class="user-tag">{{ currentUsername }} 专属偏好</span>
        </div>

        <div class="panel-desc">
          为不同年龄与屏幕分辨率定制，按账号独立保存
        </div>

        <!-- 4 档分段卡片选择器 -->
        <div class="font-options-grid">
          <div
            v-for="opt in fontSizeOptions"
            :key="opt.value"
            class="font-option-card"
            :class="{ 'selected': currentFontSize === opt.value }"
            @click="selectFontSize(opt.value)"
          >
            <div class="opt-top">
              <div class="opt-name-wrap">
                <span class="opt-name">{{ opt.label }}</span>
                <span class="opt-scale font-mono">{{ opt.scalePercent }}</span>
              </div>
              <Check v-if="currentFontSize === opt.value" class="w-3.5 h-3.5 text-blue-600 check-icon" />
            </div>
            
            <div class="opt-preview" :style="{ fontSize: opt.samplePx }">
              预览文字 123
            </div>

            <div class="opt-desc">
              {{ opt.description }}
            </div>
          </div>
        </div>

        <div class="panel-footer">
          <span class="hint-text">💡 设置将实时生效并自动保存到服务器</span>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useFontSize, type FontSizeLevel } from '../../composables/useFontSize'
import { Type, ChevronDown, Check } from 'lucide-vue-next'

const { currentFontSize, currentUsername, fontSizeOptions, setUserFontSize } = useFontSize()

const isOpen = ref(false)
const containerRef = ref<HTMLElement | null>(null)

const currentOption = computed(() => {
  return fontSizeOptions.find(o => o.value === currentFontSize.value) || fontSizeOptions[1]
})

const toggleDropdown = () => {
  isOpen.value = !isOpen.value
}

const selectFontSize = (level: FontSizeLevel) => {
  setUserFontSize(level)
  isOpen.value = false
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
  font-weight: 500;
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
  font-size: 11px;
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
  padding: 12px;
  z-index: 1000;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.05);
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
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
  font-size: 11px;
  color: #64748b;
  margin-bottom: 10px;
}

.font-options-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-bottom: 10px;
}

.font-option-card {
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 8px 10px;
  background: #f8fafc;
  cursor: pointer;
  transition: all 0.15s ease;
  display: flex;
  flex-direction: column;
}

.font-option-card:hover {
  background: #f0fdf4;
  border-color: #86efac;
}

.font-option-card.selected {
  background: #eff6ff;
  border-color: #3b82f6;
  box-shadow: 0 0 0 1px #3b82f6;
}

.opt-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}

.opt-name-wrap {
  display: flex;
  align-items: center;
  gap: 4px;
}

.opt-name {
  font-size: 11.5px;
  font-weight: 600;
  color: #1e293b;
}

.opt-scale {
  font-size: 10px;
  color: #3b82f6;
  font-weight: 600;
}

.opt-preview {
  color: #334155;
  font-weight: 500;
  margin-bottom: 4px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.opt-desc {
  font-size: 10px;
  color: #64748b;
  line-height: 1.3;
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
