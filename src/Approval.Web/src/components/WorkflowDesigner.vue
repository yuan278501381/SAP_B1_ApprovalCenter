<script setup lang="ts">
import { ref, onMounted } from 'vue'
import axios from 'axios'
import {
  GitFork,
  Users,
  Calculator,
  Plus,
  Save,
  Layers,
  ArrowRight,
  ShieldAlert,
  Sparkles,
  RefreshCw
} from 'lucide-vue-next'

const API_BASE = import.meta.env.VITE_API_BASE || '/api/v1'
const api = axios.create({ baseURL: API_BASE })

// 注入请求头与 TraceId
api.interceptors.request.use((config) => {
  const user = localStorage.getItem('sap_b1_approval_user') || 'admin'
  config.headers['X-Approval-User'] = user
  config.headers['X-Approval-User-Name'] = user
  config.headers['X-Trace-Id'] = 'trace_fe_designer_' + Math.random().toString(36).substring(2, 9)
  return config
})

const definitions = ref<any[]>([])
const activeDef = ref<any>(null)
const graphModel = ref<{ nodes: any[]; edges: any[] }>({ nodes: [], edges: [] })
const allowSubmitterRevoke = ref(true)
const loading = ref(false)
const publishing = ref(false)
const selectedNode = ref<any>(null)
const toast = ref<{ text: string; type: 'success' | 'error' } | null>(null)

const showToast = (text: string, type: 'success' | 'error' = 'success') => {
  toast.value = { text, type }
  setTimeout(() => { toast.value = null }, 3500)
}

const loadDefinitions = async () => {
  loading.value = true
  try {
    const res = await api.get('/definitions')
    definitions.value = res.data.data || []
    if (definitions.value.length > 0) {
      selectDefinition(definitions.value[0])
    }
  } catch (err: any) {
    showToast(err.response?.data?.message || '加载流程定义失败', 'error')
  } finally {
    loading.value = false
  }
}

const selectDefinition = (def: any) => {
  activeDef.value = def
  try {
    const graphJson = def.latestVersion?.graphJson || '{}'
    const parsed = JSON.parse(graphJson)
    allowSubmitterRevoke.value = parsed.allowSubmitterRevoke ?? parsed.AllowSubmitterRevoke ?? true
    graphModel.value = {
      nodes: parsed.nodes || parsed.Nodes || [],
      edges: parsed.edges || parsed.Edges || []
    }
    selectedNode.value = graphModel.value.nodes[0] || null
  } catch {
    allowSubmitterRevoke.value = true
    graphModel.value = { nodes: [], edges: [] }
    selectedNode.value = null
  }
}

const publishVersion = async () => {
  if (!activeDef.value) return
  publishing.value = true
  try {
    const fullGraph = {
      allowSubmitterRevoke: allowSubmitterRevoke.value,
      nodes: graphModel.value.nodes,
      edges: graphModel.value.edges
    }
    const payload = {
      graphJson: JSON.stringify(fullGraph)
    }
    await api.post(`/definitions/${activeDef.value.id}/versions`, payload)
    showToast(`流程【${activeDef.value.name}】新版本 (BPMN 2.0) 发布成功！`)
    await loadDefinitions()
  } catch (err: any) {
    showToast(err.response?.data?.message || '发布新版本失败', 'error')
  } finally {
    publishing.value = false
  }
}

const addApprovalNode = () => {
  const newKey = 'appr_' + Math.random().toString(36).substring(2, 6)
  const newNode = {
    nodeKey: newKey,
    name: '新增审批节点',
    nodeType: 2, // Approval
    taskType: 1, // Approve
    candidateValues: ['manager'],
    conditionExpression: null
  }
  graphModel.value.nodes.push(newNode)
  selectedNode.value = newNode
  showToast('已添加新审批节点，请在右侧配置审批人')
}

onMounted(() => {
  loadDefinitions()
})
</script>

<template>
  <div class="designer-container">
    <!-- 头部工具栏 -->
    <div class="header-bar card">
      <div class="title-info">
        <div class="icon-wrap">
          <GitFork class="w-5 h-5 text-purple-600" />
        </div>
        <div>
          <h2>可视化审批流程模型设计中心</h2>
          <p class="sub-text">有向无环图节点编排、多级审批人候选池设置、条件分支与版本热发布</p>
        </div>
      </div>

      <div class="action-buttons">
        <button class="btn btn-secondary btn-sm" @click="loadDefinitions" :disabled="loading">
          <RefreshCw :class="['w-4 h-4', loading ? 'animate-spin' : '']" />
          <span>刷新</span>
        </button>
        <button class="btn btn-primary btn-sm" @click="publishVersion" :disabled="publishing || !activeDef">
          <Save class="w-4 h-4" />
          <span>{{ publishing ? '发布中...' : '发布为新版本 (热生效)' }}</span>
        </button>
      </div>
    </div>

    <!-- 主体：左侧流程列表 + 中间可视化节点画布 + 右侧节点属性抽屉 -->
    <div class="designer-body">
      <!-- 左侧：流程列表 -->
      <aside class="def-list card">
        <div class="panel-header">
          <h3>流程定义列表</h3>
        </div>
        <div class="def-items">
          <div
            v-for="d in definitions"
            :key="d.id"
            :class="['def-item', activeDef?.id === d.id ? 'active' : '']"
            @click="selectDefinition(d)"
          >
            <div class="def-title">{{ d.name }}</div>
            <div class="def-meta">
              <span class="def-code">{{ d.id }}</span>
              <span class="badge badge-info">V{{ d.latestVersion?.versionNum || 1 }}</span>
            </div>
          </div>
        </div>
      </aside>

      <!-- 中间：节点流程图画布 -->
      <main class="canvas-panel card">
        <div class="canvas-header">
          <div class="canvas-title">
            <Layers class="w-4 h-4 text-blue-600" />
            <span>当前流程：<strong>{{ activeDef?.name }}</strong> (版本: V{{ activeDef?.latestVersion?.versionNum }})</span>
          </div>
          <button class="btn btn-secondary btn-sm" @click="addApprovalNode">
            <Plus class="w-3.5 h-3.5" />
            <span>添加审批节点</span>
          </button>
        </div>

        <div class="nodes-flow-wrap">
          <div class="nodes-sequence">
            <template v-for="(node, idx) in graphModel.nodes" :key="node.nodeKey || idx">
              <!-- 节点卡片 -->
              <div
                :class="[
                  'node-card',
                  node.nodeType === 1 || node.NodeType === 1 ? 'node-start' : '',
                  node.nodeType === 7 || node.NodeType === 7 ? 'node-end' : '',
                  node.nodeType === 3 || node.NodeType === 3 ? 'node-cond' : '',
                  node.nodeType === 2 || node.NodeType === 2 ? 'node-appr' : '',
                  selectedNode?.nodeKey === node.nodeKey ? 'selected' : ''
                ]"
                @click="selectedNode = node"
              >
                <div class="node-badge">
                  <span v-if="node.nodeType === 1 || node.NodeType === 1">开始节点</span>
                  <span v-else-if="node.nodeType === 7 || node.NodeType === 7">放行结束</span>
                  <span v-else-if="node.nodeType === 3 || node.NodeType === 3">条件判断</span>
                  <span v-else>审批节点</span>
                </div>

                <div class="node-name">{{ node.name || node.Name }}</div>

                <div v-if="node.nodeType === 3 || node.NodeType === 3" class="node-detail cond">
                  <Calculator class="w-3.5 h-3.5 text-amber-600" />
                  <code>{{ node.conditionExpression || node.ConditionExpression || '无表达式' }}</code>
                </div>

                <div v-if="node.nodeType === 2 || node.NodeType === 2" class="node-detail appr">
                  <Users class="w-3.5 h-3.5 text-blue-600" />
                  <span>候选人: {{ (node.candidateValues || node.CandidateValues || []).join(', ') || '未配置' }}</span>
                </div>
              </div>

              <!-- 连接箭头 -->
              <div v-if="idx < graphModel.nodes.length - 1" class="flow-arrow">
                <ArrowRight class="w-5 h-5 text-slate-400" />
              </div>
            </template>
          </div>
        </div>

        <!-- 连线规则总览 -->
        <div class="edges-summary">
          <h4>流转逻辑与分支连线 (Edges)</h4>
          <div class="edge-tags">
            <span v-for="(e, i) in graphModel.edges" :key="i" class="edge-tag flex items-center">
              <span>{{ e.fromNodeKey || e.FromNodeKey }}</span>
              <ArrowRight class="w-3.5 h-3.5 inline mx-1.5 text-slate-400" />
              <span>{{ e.toNodeKey || e.ToNodeKey }}</span>
              <strong v-if="e.label || e.Label" class="ml-1">({{ e.label || e.Label }})</strong>
            </span>
          </div>
        </div>
      </main>

      <!-- 右侧：节点属性与审批人配置 -->
      <aside class="node-props card">
        <div class="panel-header">
          <h3>节点属性与流程控制</h3>
        </div>

        <!-- 流程全局控制策略 (BPMN 2.0) -->
        <div class="policy-card">
          <div class="policy-header">
            <strong>BPMN 2.0 流程流转控制策略</strong>
          </div>
          <label class="policy-item">
            <input type="checkbox" v-model="allowSubmitterRevoke" />
            <div class="policy-label">
              <span>允许发起人主动撤销审批</span>
              <span class="policy-hint">关闭后，发起人无法在审批中撤回单据</span>
            </div>
          </label>
        </div>

        <div v-if="!selectedNode" class="empty-props">
          <Sparkles class="w-10 h-10 text-slate-300" />
          <p>请点击中间节点进行属性与审批人配置</p>
        </div>

        <div v-else class="props-form">
          <div class="form-group">
            <label>节点标识 (Key):</label>
            <input v-model="selectedNode.nodeKey" disabled />
          </div>

          <div class="form-group">
            <label>节点名称:</label>
            <input v-model="selectedNode.name" />
          </div>

          <div v-if="selectedNode.nodeType === 3 || selectedNode.NodeType === 3" class="form-group">
            <label>条件判定表达式:</label>
            <input v-model="selectedNode.conditionExpression" placeholder="如: DocTotal > 50000" />
          </div>

          <div v-if="selectedNode.nodeType === 2 || selectedNode.NodeType === 2" class="form-group">
            <label>审批候选人池 (以逗号隔开):</label>
            <input
              :value="(selectedNode.candidateValues || []).join(', ')"
              @input="selectedNode.candidateValues = ($event.target as HTMLInputElement).value.split(/[,，\s]+/).filter(Boolean)"
              placeholder="如: manager, director, admin"
            />
            <span class="hint">支持填写具体 SAP 操作员代码（如 manager, sales01, director）</span>
          </div>

          <div class="tips-box">
            <ShieldAlert class="w-4 h-4 text-amber-600" />
            <p>修改节点后，点击右上角【发布为新版本】即可立即对后续新单据生效，老单据依然沿用历史版本图以确保审计不可变。</p>
          </div>
        </div>
      </aside>
    </div>

    <!-- 浮动 Toast 提示 -->
    <div v-if="toast" :class="['toast', toast.type]">
      {{ toast.text }}
    </div>
  </div>
</template>

<style scoped>
.designer-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  box-sizing: border-box;
  overflow: hidden;
  gap: 12px;
  padding: 16px;
}

.header-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 20px;
}

.title-info {
  display: flex;
  align-items: center;
  gap: 12px;
}

.icon-wrap {
  background: #f3e8ff;
  padding: 8px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
}

.title-info h2 {
  font-size: 16px;
  font-weight: 700;
  color: #0f172a;
}

.title-info .sub-text {
  font-size: 12px;
  color: var(--text-secondary);
}

.designer-body {
  display: grid;
  grid-template-columns: 240px 1fr 300px;
  gap: 12px;
  flex: 1;
  min-height: 0;
}

.def-list, .canvas-panel, .node-props {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 16px;
}

.panel-header {
  margin-bottom: 12px;
}

.panel-header h3 {
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
}

.def-items {
  display: flex;
  flex-direction: column;
  gap: 8px;
  overflow-y: auto;
}

.def-item {
  border: 1px solid var(--border-color);
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  background: #fff;
  transition: all 0.15s;
}

.def-item:hover {
  border-color: #9333ea;
  background: #faf5ff;
}

.def-item.active {
  border-color: #9333ea;
  background: #f3e8ff;
}

.def-title {
  font-size: 13px;
  font-weight: 600;
  color: #0f172a;
}

.def-meta {
  display: flex;
  justify-content: space-between;
  margin-top: 4px;
  font-size: 11px;
}

.def-code {
  color: #64748b;
  font-family: monospace;
}

/* 画布 */
.canvas-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border-color);
  margin-bottom: 16px;
}

.canvas-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}

.nodes-flow-wrap {
  flex: 1;
  overflow-x: auto;
  display: flex;
  align-items: center;
  padding: 20px 10px;
}

.nodes-sequence {
  display: flex;
  align-items: center;
  gap: 12px;
}

.node-card {
  width: 170px;
  border: 2px solid var(--border-color);
  background: #fff;
  border-radius: var(--radius-md);
  padding: 12px;
  cursor: pointer;
  transition: all 0.2s;
  box-shadow: var(--shadow-sm);
  position: relative;
}

.node-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-md);
}

.node-card.selected {
  border-color: #2563eb;
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.2);
}

.node-card.node-start {
  border-color: #10b981;
}

.node-card.node-end {
  border-color: #64748b;
}

.node-card.node-cond {
  border-color: #f59e0b;
}

.node-card.node-appr {
  border-color: #3b82f6;
}

.node-badge {
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  color: #64748b;
  margin-bottom: 4px;
}

.node-name {
  font-size: 13px;
  font-weight: 700;
  color: #0f172a;
  margin-bottom: 6px;
}

.node-detail {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  color: #475569;
}

.node-detail code {
  background: #fef3c7;
  padding: 1px 4px;
  border-radius: 3px;
}

.flow-arrow {
  display: flex;
  align-items: center;
}

.edges-summary {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--border-color);
}

.edges-summary h4 {
  font-size: 12px;
  color: #64748b;
  margin-bottom: 6px;
}

.edge-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.edge-tag {
  background: #f1f5f9;
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 11px;
  color: #334155;
}

/* 属性 */
.empty-props {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 200px;
  color: var(--text-secondary);
  font-size: 12px;
}

.props-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 12px;
  font-weight: 600;
  color: #334155;
}

.form-group input {
  padding: 7px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 13px;
}

.hint {
  font-size: 11px;
  color: #94a3b8;
}

.tips-box {
  background: #fffbeb;
  border: 1px solid #fef3c7;
  padding: 10px;
  border-radius: var(--radius-sm);
  display: flex;
  gap: 8px;
  font-size: 11px;
  color: #92400e;
  line-height: 1.4;
}

.policy-card {
  background: #f8fafc;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 12px;
  margin-bottom: 12px;
}

.policy-header {
  font-size: 12px;
  color: #334155;
  margin-bottom: 8px;
}

.policy-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  cursor: pointer;
}

.policy-label {
  display: flex;
  flex-direction: column;
  font-size: 12px;
  font-weight: 600;
  color: #0f172a;
}

.policy-hint {
  font-size: 10px;
  font-weight: normal;
  color: #64748b;
  margin-top: 2px;
}
</style>
