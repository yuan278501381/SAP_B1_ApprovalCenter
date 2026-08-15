<script setup lang="ts">
import { ref } from 'vue'
import TaskWorkbench from './components/TaskWorkbench.vue'
import RuleMatrixManager from './components/RuleMatrixManager.vue'
import WorkflowDesigner from './components/WorkflowDesigner.vue'
import BpmnPlayground from './components/BpmnPlayground.vue'
import {
  CheckSquare,
  Sliders,
  GitFork,
  Sparkles
} from 'lucide-vue-next'

const currentTab = ref<'workbench' | 'rules' | 'designer' | 'bpmnlab'>('bpmnlab')
</script>

<template>
  <div class="app-root">
    <!-- 主导航 Tab 条 -->
    <nav class="main-tab-bar">
      <div class="tabs-container">
        <button
          :class="['tab-nav-btn', currentTab === 'bpmnlab' ? 'active' : '']"
          @click="currentTab = 'bpmnlab'"
        >
          <Sparkles class="w-4 h-4 text-sky-400" />
          <span>BPMN 2.0 实验室 (bpmn-js / LogicFlow / Flowable)</span>
        </button>

        <button
          :class="['tab-nav-btn', currentTab === 'workbench' ? 'active' : '']"
          @click="currentTab = 'workbench'"
        >
          <CheckSquare class="w-4 h-4" />
          <span>审批处理工作台</span>
        </button>

        <button
          :class="['tab-nav-btn', currentTab === 'rules' ? 'active' : '']"
          @click="currentTab = 'rules'"
        >
          <Sliders class="w-4 h-4" />
          <span>触发规则矩阵 (人员/部门/金额/单据)</span>
        </button>

        <button
          :class="['tab-nav-btn', currentTab === 'designer' ? 'active' : '']"
          @click="currentTab = 'designer'"
        >
          <GitFork class="w-4 h-4" />
          <span>流程模型设计器 (节点/审批人)</span>
        </button>
      </div>
    </nav>

    <!-- 视图呈现 -->
    <div class="tab-view-body">
      <BpmnPlayground v-if="currentTab === 'bpmnlab'" />
      <TaskWorkbench v-else-if="currentTab === 'workbench'" />
      <RuleMatrixManager v-else-if="currentTab === 'rules'" />
      <WorkflowDesigner v-else-if="currentTab === 'designer'" />
    </div>
  </div>
</template>

<style>
/* 全局基础重置与极客排版 */
html, body, #app {
  height: 100%;
  margin: 0;
  padding: 0;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  background-color: #f8fafc;
  color: #0f172a;
}

.app-root {
  display: flex;
  flex-direction: column;
  height: 100vh;
  overflow: hidden;
}

.main-tab-bar {
  background: #0f172a;
  padding: 0 16px;
  display: flex;
  align-items: center;
  border-bottom: 1px solid #1e293b;
  box-shadow: 0 2px 4px rgba(0,0,0,0.1);
  z-index: 50;
}

.tabs-container {
  display: flex;
  gap: 4px;
}

.tab-nav-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 18px;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  color: #94a3b8;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.tab-nav-btn:hover {
  color: #f8fafc;
  background: rgba(255, 255, 255, 0.05);
}

.tab-nav-btn.active {
  color: #38bdf8;
  border-bottom-color: #38bdf8;
  background: rgba(56, 189, 248, 0.08);
}

.tab-view-body {
  flex: 1;
  min-height: 0;
  height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
</style>
