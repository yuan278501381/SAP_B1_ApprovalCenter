<script setup lang="ts">
import { ref, onMounted } from 'vue'
import api from '../config/request'
import { appConfig } from '../config'
import {
  Plus,
  Edit2,
  Trash2,
  Play,
  CheckCircle2,
  XCircle,
  Users,
  Building2,
  Calculator,
  GitFork,
  RefreshCw,
  Sliders,
  Table,
  Layers,
  X,
  Sparkles,
  FileCode,
  Tag
} from 'lucide-vue-next'

const rules = ref<any[]>([])
const definitions = ref<any[]>([])
const loading = ref(false)
const saving = ref(false)
const showModal = ref(false)
const isEdit = ref(false)
const toast = ref<{ text: string; type: 'success' | 'error' } | null>(null)

// 条件构建器模式: 'builder' (可视化低代码) | 'raw' (纯文本表达式)
const conditionTab = ref<'builder' | 'raw'>('builder')

// 可视化条件模型
const builder = ref<{
  combine: 'AND' | 'OR'
  headerConditions: Array<{ field: string; op: string; value: string }>
  lineConditions: Array<{ collection: string; mode: 'ANY' | 'ALL'; field: string; op: string; value: string }>
}>({
  combine: 'AND',
  headerConditions: [],
  lineConditions: []
})

// 规则表单模型
const form = ref({
  id: '',
  companyId: appConfig.defaultCompanyId,
  objectCode: 'CHORDR',
  objectType: 'Document',
  ruleName: '',
  description: '',
  triggerMode: 'AutoAlways',
  triggerFieldName: 'U_APSubmit',
  userScopeMode: 'All', // All, Whitelist, Blacklist
  userScopeInput: '',
  deptScopeInput: '',
  conditionExpr: '',
  targetDefinitionId: '',
  priority: 10,
  isActive: true
})

// 模拟测试器模型
const sim = ref({
  objectCode: 'CHORDR',
  creatorUserCode: 'manager',
  department: 'Sales1',
  docTotal: 85000,
  cardCode: 'C20000',
  hasCheckbox: 'Y',
  rawLinesJson: '[\n  { "LineId": 1, "ItemCode": "A0001", "Quantity": 100, "Price": 350 },\n  { "LineId": 2, "ItemCode": "A0002", "Quantity": 50, "Price": 420 }\n]'
})
const simTesting = ref(false)
const simResult = ref<any>(null)

const showToast = (text: string, type: 'success' | 'error' = 'success') => {
  toast.value = { text, type }
  setTimeout(() => { toast.value = null }, 3500)
}

const loadData = async () => {
  loading.value = true
  try {
    const [rulesRes, defsRes] = await Promise.all([
      api.get('/rules', { params: { companyId: appConfig.defaultCompanyId } }),
      api.get('/definitions')
    ])
    rules.value = rulesRes.data.data || []
    definitions.value = defsRes.data.data || []
  } catch (err: any) {
    showToast(err.response?.data?.message || '加载规则列表失败', 'error')
  } finally {
    loading.value = false
  }
}

// 辅助方法：添加表头条件
const addHeaderCond = () => {
  builder.value.headerConditions.push({
    field: 'DocTotal',
    op: '>=',
    value: '50000'
  })
}

// 辅助方法：添加子表行条件
const addLineCond = () => {
  builder.value.lineConditions.push({
    collection: 'CH_ORDR_1Collection',
    mode: 'ANY',
    field: 'ItemCode',
    op: 'IN',
    value: 'A0001,A0002'
  })
}

const removeHeaderCond = (idx: number) => {
  builder.value.headerConditions.splice(idx, 1)
}

const removeLineCond = (idx: number) => {
  builder.value.lineConditions.splice(idx, 1)
}

const openCreateModal = () => {
  isEdit.value = false
  conditionTab.value = 'builder'
  builder.value = {
    combine: 'AND',
    headerConditions: [{ field: 'DocTotal', op: '>=', value: '50000' }],
    lineConditions: []
  }
  form.value = {
    id: '',
    companyId: appConfig.defaultCompanyId,
    objectCode: 'CHORDR',
    objectType: 'Document',
    ruleName: '',
    description: '',
    triggerMode: 'AutoAlways',
    triggerFieldName: 'U_APSubmit',
    userScopeMode: 'All',
    userScopeInput: '',
    deptScopeInput: '',
    conditionExpr: '',
    targetDefinitionId: definitions.value[0]?.id || 'DEF_CHORDR',
    priority: 10,
    isActive: true
  }
  showModal.value = true
}

const openEditModal = (r: any) => {
  isEdit.value = true
  const expr = r.conditionExpr || ''
  
  // 尝试解析结构化条件
  if (expr.trim().startsWith('{') && expr.trim().endsWith('}')) {
    try {
      const parsed = JSON.parse(expr)
      builder.value = {
        combine: parsed.combine || 'AND',
        headerConditions: parsed.headerConditions || [],
        lineConditions: parsed.lineConditions || []
      }
      conditionTab.value = 'builder'
    } catch {
      conditionTab.value = 'raw'
    }
  } else {
    // 传统简单表达式
    builder.value = {
      combine: 'AND',
      headerConditions: expr ? [{ field: 'DocTotal', op: expr.includes('>') ? '>=' : '<=', value: expr.replace(/[^0-9.]/g, '') }] : [],
      lineConditions: []
    }
    conditionTab.value = 'builder'
  }

  form.value = {
    id: r.id,
    companyId: r.companyId,
    objectCode: r.objectCode,
    objectType: r.objectType || 'Document',
    ruleName: r.ruleName,
    description: r.description || '',
    triggerMode: r.triggerMode || 'AutoAlways',
    triggerFieldName: r.triggerFieldName || 'U_APSubmit',
    userScopeMode: r.userScopeMode || 'All',
    userScopeInput: (r.userScopeList || []).join(', '),
    deptScopeInput: (r.deptScopeList || []).join(', '),
    conditionExpr: expr,
    targetDefinitionId: r.targetDefinitionId,
    priority: r.priority,
    isActive: r.isActive
  }
  showModal.value = true
}

const saveRule = async () => {
  if (!form.value.ruleName.trim()) {
    showToast('请输入规则名称', 'error')
    return
  }
  if (!form.value.targetDefinitionId) {
    showToast('请选择目标流程定义', 'error')
    return
  }

  // 组装 conditionExpr
  let finalConditionExpr = form.value.conditionExpr
  if (conditionTab.value === 'builder') {
    if (builder.value.headerConditions.length > 0 || builder.value.lineConditions.length > 0) {
      finalConditionExpr = JSON.stringify(builder.value)
    } else {
      finalConditionExpr = ''
    }
  }

  saving.value = true
  try {
    const userScopeList = form.value.userScopeInput
      .split(/[,，\s]+/)
      .map(s => s.trim())
      .filter(s => s.length > 0)

    const deptScopeList = form.value.deptScopeInput
      .split(/[,，\s]+/)
      .map(s => s.trim())
      .filter(s => s.length > 0)

    const payload = {
      id: form.value.id || undefined,
      companyId: form.value.companyId,
      objectCode: form.value.objectCode.toUpperCase(),
      objectType: form.value.objectType,
      ruleName: form.value.ruleName,
      description: form.value.description,
      triggerMode: form.value.triggerMode,
      triggerFieldName: form.value.triggerFieldName,
      userScopeMode: form.value.userScopeMode,
      userScopeList,
      deptScopeList,
      conditionExpr: finalConditionExpr || undefined,
      targetDefinitionId: form.value.targetDefinitionId,
      priority: form.value.priority,
      isActive: form.value.isActive
    }

    if (isEdit.value) {
      await api.put(`/rules/${form.value.id}`, payload)
      showToast('规则更新成功')
    } else {
      await api.post('/rules', payload)
      showToast('新规则创建成功')
    }

    showModal.value = false
    await loadData()
  } catch (err: any) {
    showToast(err.response?.data?.message || '保存规则失败', 'error')
  } finally {
    saving.value = false
  }
}

const deleteRule = async (r: any) => {
  if (!confirm(`确定要删除规则【${r.ruleName}】吗？`)) return
  try {
    await api.delete(`/rules/${r.id}`)
    showToast(`规则已删除`)
    await loadData()
  } catch (err: any) {
    showToast('删除规则失败', 'error')
  }
}

const runSimulation = async () => {
  simTesting.value = true
  simResult.value = null
  try {
    const headers: Record<string, any> = {}
    if (sim.value.hasCheckbox) {
      headers['U_APSubmit'] = sim.value.hasCheckbox
    }
    if (sim.value.department) {
      headers['Department'] = sim.value.department
    }
    if (sim.value.cardCode) {
      headers['CardCode'] = sim.value.cardCode
    }

    let parsedLines: any[] = []
    try {
      if (sim.value.rawLinesJson) {
        parsedLines = JSON.parse(sim.value.rawLinesJson)
      }
    } catch {
      parsedLines = []
    }

    const mockRaw: Record<string, any> = {
      DocEntry: 9999,
      DocTotal: Number(sim.value.docTotal) || 0,
      CardCode: sim.value.cardCode,
      CH_ORDR_1Collection: parsedLines,
      DocumentLines: parsedLines
    }

    const res = await api.post('/rules/test-match', {
      companyId: appConfig.defaultCompanyId,
      objectCode: sim.value.objectCode,
      creatorUserCode: sim.value.creatorUserCode,
      department: sim.value.department,
      docTotal: Number(sim.value.docTotal) || 0,
      headerFields: headers,
      rawJson: JSON.stringify(mockRaw)
    })
    simResult.value = res.data.data
  } catch (err: any) {
    showToast('模拟测试执行失败', 'error')
  } finally {
    simTesting.value = false
  }
}

// 格式化展示规则条件为可读标签
const parseRuleDisplay = (expr: string | null) => {
  if (!expr) return null
  const trimmed = expr.trim()
  if (trimmed.startsWith('{') && trimmed.endsWith('}')) {
    try {
      const parsed = JSON.parse(trimmed)
      return {
        isComposite: true,
        combine: parsed.combine || 'AND',
        headers: parsed.headerConditions || [],
        lines: parsed.lineConditions || []
      }
    } catch {
      return { isComposite: false, raw: expr }
    }
  }
  return { isComposite: false, raw: expr }
}

onMounted(() => {
  loadData()
})
</script>

<template>
  <div class="rule-container">
    <!-- 头部工具栏 -->
    <div class="header-bar card">
      <div class="title-info">
        <div class="icon-wrap">
          <Sliders class="w-5 h-5 text-blue-600" />
        </div>
        <div>
          <h2>审批触发与多维路由规则矩阵</h2>
          <p class="sub-text">支持表头多字段组合 (AND/OR) 与子表明细行内容 (ANY/ALL) 级低代码可视化配置</p>
        </div>
      </div>

      <div class="action-buttons">
        <button class="btn btn-secondary" @click="loadData" :disabled="loading">
          <RefreshCw class="w-4 h-4 mr-1" :class="{ 'animate-spin': loading }" />
          <span>刷新</span>
        </button>
        <button class="btn btn-primary" @click="openCreateModal">
          <Plus class="w-4 h-4 mr-1" />
          <span>新建组合路由规则</span>
        </button>
      </div>
    </div>

    <!-- 主体：左侧规则表格 + 右侧实时模拟器 -->
    <div class="content-grid">
      <!-- 左侧：规则列表矩阵 -->
      <section class="matrix-panel card">
        <div class="panel-header">
          <h3>生效规则列表 (按优先级降序评估)</h3>
          <span class="badge badge-info">{{ rules.length }} 条规则</span>
        </div>

        <div class="rules-table-wrap">
          <div v-if="rules.length === 0" class="empty-rules">
            <Sliders class="w-12 h-12 text-slate-300 mb-2" />
            <p>暂无配置规则，系统将采用默认对象绑定流程</p>
          </div>

          <table v-else class="rules-table">
            <thead>
              <tr>
                <th style="width: 60px;">优先级</th>
                <th>规则名称 / 对象</th>
                <th>触发方式</th>
                <th>制单人范围</th>
                <th>部门范围</th>
                <th>复合条件 (表头 + 行表明细)</th>
                <th>目标流程模型</th>
                <th style="width: 70px;">状态</th>
                <th style="width: 100px; text-align: right;">操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in rules" :key="r.id">
                <td>
                  <span class="prio-tag">{{ r.priority }}</span>
                </td>
                <td>
                  <div class="rule-name-cell">
                    <strong>{{ r.ruleName }}</strong>
                    <span class="obj-badge">{{ r.objectCode }}</span>
                  </div>
                </td>
                <td>
                  <span class="text-xs text-slate-600">
                    {{ r.triggerMode === 'ExplicitCheckbox' ? '勾选 ' + (r.triggerFieldName || 'U_APSubmit') : '总是触发' }}
                  </span>
                </td>
                <td>
                  <div class="scope-cell">
                    <span v-if="r.userScopeMode === 'All'" class="scope-pill all">
                      <Users class="w-3 h-3" /> 全员
                    </span>
                    <span v-else-if="r.userScopeMode === 'Whitelist'" class="scope-pill white" :title="(r.userScopeList || []).join(', ')">
                      <CheckCircle2 class="w-3 h-3" /> 白名单 ({{ (r.userScopeList || []).length }}人)
                    </span>
                    <span v-else class="scope-pill black" :title="(r.userScopeList || []).join(', ')">
                      <XCircle class="w-3 h-3" /> 黑名单免审
                    </span>
                  </div>
                </td>
                <td>
                  <span v-if="!r.deptScopeList || r.deptScopeList.length === 0" class="text-xs text-slate-400">全部部门</span>
                  <span v-else class="dept-tag" :title="r.deptScopeList.join(', ')">
                    <Building2 class="w-3 h-3 inline mr-0.5" />
                    {{ r.deptScopeList.join(', ') }}
                  </span>
                </td>
                <td>
                  <!-- 复合规则结构化标签展示 -->
                  <div v-if="parseRuleDisplay(r.conditionExpr)?.isComposite" class="composite-tags">
                    <span class="combine-badge">{{ parseRuleDisplay(r.conditionExpr)?.combine }}</span>
                    <!-- 表头条件标签 -->
                    <span
                      v-for="(hc, hIdx) in parseRuleDisplay(r.conditionExpr)?.headers"
                      :key="'h_'+hIdx"
                      class="cond-badge header"
                    >
                      表头: {{ hc.field }} {{ hc.op }} {{ hc.value }}
                    </span>
                    <!-- 行表条件标签 -->
                    <span
                      v-for="(lc, lIdx) in parseRuleDisplay(r.conditionExpr)?.lines"
                      :key="'l_'+lIdx"
                      class="cond-badge line"
                    >
                      行表[{{ lc.collection || '明细' }}]: {{ lc.mode === 'ALL' ? '全部' : '任意' }}行 {{ lc.field }} {{ lc.op }} {{ lc.value }}
                    </span>
                  </div>
                  <span v-else-if="r.conditionExpr" class="cond-expr-pill">
                    <Calculator class="w-3 h-3" />
                    <code>{{ r.conditionExpr }}</code>
                  </span>
                  <span v-else class="text-xs text-slate-400">无附加条件 (直接流转)</span>
                </td>
                <td>
                  <div class="target-def-cell">
                    <GitFork class="w-3.5 h-3.5 text-blue-600" />
                    <span>{{ r.targetDefinitionName || r.targetDefinitionId }}</span>
                  </div>
                </td>
                <td>
                  <span :class="['status-dot', r.isActive ? 'active' : 'inactive']">
                    {{ r.isActive ? '已启用' : '已停用' }}
                  </span>
                </td>
                <td style="text-align: right;">
                  <div class="action-cell">
                    <button class="btn-icon" @click="openEditModal(r)" title="编辑规则">
                      <Edit2 class="w-3.5 h-3.5 text-blue-600" />
                    </button>
                    <button class="btn-icon" @click="deleteRule(r)" title="删除规则">
                      <Trash2 class="w-3.5 h-3.5 text-rose-600" />
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- 右侧：规则命中实时模拟器 -->
      <aside class="sim-panel card">
        <div class="panel-header">
          <div class="sim-title">
            <Play class="w-4 h-4 text-emerald-600" />
            <h3>规则命中实时模拟器</h3>
          </div>
        </div>

        <p class="sim-desc">输入模拟单据参数与子表明细，即时测试评估单据将命中哪条规则与哪个审批流程：</p>

        <div class="sim-form">
          <div class="form-group">
            <label>单据/主数据类型:</label>
            <select v-model="sim.objectCode">
              <option value="CHORDR">型号订单 UDO (CHORDR)</option>
              <option value="CHOQUT">型号报价单 UDO (CHOQUT)</option>
              <option value="ORDR">标准销售订单 (ORDR)</option>
            </select>
          </div>

          <div class="form-group">
            <label>制单人账号 (UserCode):</label>
            <input v-model="sim.creatorUserCode" placeholder="如: manager, sales01" />
          </div>

          <div class="form-group">
            <label>制单人部门:</label>
            <input v-model="sim.department" placeholder="如: Sales1" />
          </div>

          <div class="form-group">
            <label>客户代码 (CardCode):</label>
            <input v-model="sim.cardCode" placeholder="如: C20000" />
          </div>

          <div class="form-group">
            <label>单据金额 (DocTotal):</label>
            <input type="number" v-model="sim.docTotal" />
          </div>

          <div class="form-group">
            <label>子表明细行模拟测试数据 (JSON 数组):</label>
            <textarea
              v-model="sim.rawLinesJson"
              rows="4"
              class="lines-textarea"
              placeholder='[ { "ItemCode": "A0001", "Quantity": 100 } ]'
            ></textarea>
          </div>

          <button class="btn btn-primary w-full mt-2" @click="runSimulation" :disabled="simTesting">
            <Play class="w-4 h-4 mr-1" />
            <span>{{ simTesting ? '正在评估规则...' : '测试规则命中' }}</span>
          </button>
        </div>

        <!-- 模拟结果卡片 -->
        <div v-if="simResult" class="sim-result-card mt-4" :class="[simResult.shouldTrigger ? 'hit' : 'miss']">
          <div class="res-header">
            <CheckCircle2 v-if="simResult.shouldTrigger" class="w-5 h-5 text-emerald-600" />
            <XCircle v-else class="w-5 h-5 text-slate-400" />
            <strong>{{ simResult.shouldTrigger ? '触发审批' : '免审放行 / 未触发' }}</strong>
          </div>
          <div class="res-body">
            <p><strong>评估原因:</strong> {{ simResult.triggerReason }}</p>
            <div v-if="simResult.matchedRule" class="matched-detail">
              <div>命中规则: <strong>{{ simResult.matchedRule.ruleName }}</strong></div>
              <div>目标流程: <code>{{ simResult.targetDefinitionId }}</code></div>
            </div>
          </div>
        </div>
      </aside>
    </div>

    <!-- 规则新建/编辑模态弹窗 -->
    <div v-if="showModal" class="modal-overlay">
      <div class="modal-card modal-lg">
        <div class="modal-header">
          <h3>{{ isEdit ? '编辑审批路由规则' : '新建审批路由规则 (表头+行表复合)' }}</h3>
          <button class="btn-close" @click="showModal = false">
            <X class="w-4 h-4" />
          </button>
        </div>

        <div class="modal-body">
          <div class="form-row">
            <div class="form-group flex-2">
              <label>规则名称 <span class="required">*</span>:</label>
              <input v-model="form.ruleName" placeholder="如: 大额特殊物料总监终审规则" />
            </div>
            <div class="form-group flex-1">
              <label>规则优先级 (越大越先评估):</label>
              <input type="number" v-model="form.priority" />
            </div>
          </div>

          <div class="form-row">
            <div class="form-group flex-1">
              <label>适用单据/主数据对象 <span class="required">*</span>:</label>
              <select v-model="form.objectCode">
                <option value="CHORDR">型号订单 UDO (CHORDR)</option>
                <option value="CHOQUT">型号报价单 UDO (CHOQUT)</option>
                <option value="ORDR">标准销售订单 (ORDR)</option>
              </select>
            </div>
            <div class="form-group flex-1">
              <label>触发方式:</label>
              <select v-model="form.triggerMode">
                <option value="AutoAlways">默认自动触发 (无字段或默认总是触发)</option>
                <option value="ExplicitCheckbox">显式勾选触发 (当且仅当字段为Y时触发)</option>
              </select>
            </div>
          </div>

          <div class="form-group">
            <label>人员范围模式 (User Scope):</label>
            <div class="radio-cards">
              <label :class="['radio-card', form.userScopeMode === 'All' ? 'selected' : '']">
                <input type="radio" value="All" v-model="form.userScopeMode" />
                <div class="r-info">
                  <strong>全部用户</strong>
                  <span>所有操作员制单均触发</span>
                </div>
              </label>
              <label :class="['radio-card', form.userScopeMode === 'Whitelist' ? 'selected' : '']">
                <input type="radio" value="Whitelist" v-model="form.userScopeMode" />
                <div class="r-info">
                  <strong>白名单模式</strong>
                  <span>仅指定人员制单时触发</span>
                </div>
              </label>
              <label :class="['radio-card', form.userScopeMode === 'Blacklist' ? 'selected' : '']">
                <input type="radio" value="Blacklist" v-model="form.userScopeMode" />
                <div class="r-info">
                  <strong>黑名单模式</strong>
                  <span>指定人员免审，其余全触发</span>
                </div>
              </label>
            </div>
          </div>

          <div v-if="form.userScopeMode !== 'All'" class="form-group">
            <label>{{ form.userScopeMode === 'Whitelist' ? '白名单用户账号列表' : '黑名单免审用户账号列表' }} (以逗号隔开):</label>
            <input v-model="form.userScopeInput" placeholder="如: manager, sales01, sales02" />
          </div>

          <div class="form-group">
            <label>制单人部门范围 (可选，以逗号隔开):</label>
            <input v-model="form.deptScopeInput" placeholder="如: Sales1, Sales2 (留空表示不限部门)" />
          </div>

          <!-- 核心：低代码可视化条件构建器 (表头组合 + 行表明细) -->
          <div class="condition-builder-box">
            <div class="cb-header">
              <div class="cb-title">
                <Sparkles class="w-4 h-4 text-amber-500" />
                <strong>业务触发条件配置 (表头多字段组合 + 子表明细行扫描)</strong>
              </div>

              <div class="cb-tabs">
                <button
                  :class="['cb-tab-btn', conditionTab === 'builder' ? 'active' : '']"
                  @click="conditionTab = 'builder'"
                >
                  <Table class="w-3.5 h-3.5 mr-1" />
                  <span>可视化构建器</span>
                </button>
                <button
                  :class="['cb-tab-btn', conditionTab === 'raw' ? 'active' : '']"
                  @click="conditionTab = 'raw'"
                >
                  <FileCode class="w-3.5 h-3.5 mr-1" />
                  <span>高级表达式 / JSON</span>
                </button>
              </div>
            </div>

            <!-- A. 可视化构建器模式 -->
            <div v-if="conditionTab === 'builder'" class="cb-body">
              <div class="combine-row">
                <span class="label-text">条件关系组合逻辑：</span>
                <select v-model="builder.combine" class="combine-select">
                  <option value="AND">全部满足 (AND - 且)</option>
                  <option value="OR">任意满足 (OR - 或)</option>
                </select>
                <span class="combine-hint">选择在所有表头和行表条件之间是全部匹配还是任意一项命中</span>
              </div>

              <!-- 1. 表头条件组 -->
              <div class="cond-group-block">
                <div class="group-title">
                  <Tag class="w-3.5 h-3.5 text-blue-600" />
                  <span>1. 表头字段条件组 (Header Fields)</span>
                  <button class="btn btn-xs btn-secondary ml-auto" @click="addHeaderCond">
                    <Plus class="w-3 h-3 mr-0.5" /> 添加表头条件
                  </button>
                </div>

                <div v-if="builder.headerConditions.length === 0" class="empty-cond-tip">
                  未配置表头条件（无表头金额/字段限制）
                </div>

                <div v-for="(hc, hIdx) in builder.headerConditions" :key="'hc_'+hIdx" class="cond-row">
                  <select v-model="hc.field" class="cond-field-select">
                    <option value="DocTotal">单据总金额 (DocTotal)</option>
                    <option value="CardCode">客户代码 (CardCode)</option>
                    <option value="Department">所属部门 (Department)</option>
                    <option value="Creator">制单人 (Creator)</option>
                    <option value="Comments">单据备注 (Comments)</option>
                  </select>

                  <select v-model="hc.op" class="cond-op-select">
                    <option value=">=">&gt;= 大于等于</option>
                    <option value=">">&gt; 大于</option>
                    <option value="<=">&lt;= 小于等于</option>
                    <option value="<">&lt; 小于</option>
                    <option value="==">== 等于</option>
                    <option value="!=">!= 不等于</option>
                    <option value="IN">IN 包含于列表 (逗号隔开)</option>
                    <option value="CONTAINS">CONTAINS 包含文本</option>
                  </select>

                  <input v-model="hc.value" class="cond-val-input" placeholder="输入比较目标值，如 50000 或 C20000" />

                  <button class="btn-icon-danger" @click="removeHeaderCond(hIdx)">
                    <X class="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              <!-- 2. 子表明细行条件组 -->
              <div class="cond-group-block">
                <div class="group-title">
                  <Layers class="w-3.5 h-3.5 text-purple-600" />
                  <span>2. 子表明细行条件组 (Line Items & Collections)</span>
                  <button class="btn btn-xs btn-secondary ml-auto" @click="addLineCond">
                    <Plus class="w-3 h-3 mr-0.5" /> 添加子表行条件
                  </button>
                </div>

                <div v-if="builder.lineConditions.length === 0" class="empty-cond-tip">
                  未配置子表明细条件（不扫描明细行物料、单价或数量）
                </div>

                <div v-for="(lc, lIdx) in builder.lineConditions" :key="'lc_'+lIdx" class="cond-row">
                  <select v-model="lc.collection" class="cond-field-select" style="max-width: 140px;">
                    <option value="CH_ORDR_1Collection">型号明细表 (CH_ORDR_1)</option>
                    <option value="CH_ORDR_3Collection">工序/阶梯表 (CH_ORDR_3)</option>
                    <option value="DocumentLines">标准物料明细 (DocumentLines)</option>
                  </select>

                  <select v-model="lc.mode" class="cond-op-select" style="max-width: 110px;">
                    <option value="ANY">任意行满足</option>
                    <option value="ALL">全部行满足</option>
                  </select>

                  <select v-model="lc.field" class="cond-field-select" style="max-width: 110px;">
                    <option value="ItemCode">物料编码 (ItemCode)</option>
                    <option value="Quantity">数量 (Quantity)</option>
                    <option value="Price">单价 (Price)</option>
                    <option value="LineTotal">行金额 (LineTotal)</option>
                    <option value="WhsCode">仓库 (WhsCode)</option>
                    <option value="U_Model">规格型号 (U_Model)</option>
                  </select>

                  <select v-model="lc.op" class="cond-op-select" style="max-width: 80px;">
                    <option value="IN">IN 属于</option>
                    <option value="==">== 等于</option>
                    <option value=">=">&gt;= 大于等于</option>
                    <option value=">">&gt; 大于</option>
                    <option value="CONTAINS">包含</option>
                  </select>

                  <input v-model="lc.value" class="cond-val-input" placeholder="如 A0001,A0002 或 100" />

                  <button class="btn-icon-danger" @click="removeLineCond(lIdx)">
                    <X class="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>
            </div>

            <!-- B. 高级表达式模式 -->
            <div v-else class="cb-raw-body">
              <textarea
                v-model="form.conditionExpr"
                rows="4"
                placeholder='可输入 JSON 复合配置或简单表达式如: DocTotal > 50000'
                class="raw-textarea"
              ></textarea>
            </div>
          </div>

          <div class="form-group mt-3">
            <label>命中后流转的目标审批流程模型 <span class="required">*</span>:</label>
            <select v-model="form.targetDefinitionId">
              <option v-for="d in definitions" :key="d.id" :value="d.id">
                {{ d.name }} ({{ d.id }})
              </option>
            </select>
          </div>

          <div class="form-group">
            <label class="checkbox-label">
              <input type="checkbox" v-model="form.isActive" />
              <span>启用此规则</span>
            </label>
          </div>
        </div>

        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showModal = false">取消</button>
          <button class="btn btn-primary" @click="saveRule" :disabled="saving">
            {{ saving ? '保存中...' : '保存规则' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 浮动 Toast 提示 -->
    <div v-if="toast" :class="['toast', toast.type]">
      {{ toast.text }}
    </div>
  </div>
</template>

<style scoped>
.rule-container {
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
  background: #eff6ff;
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

.content-grid {
  display: grid;
  grid-template-columns: 1fr 360px;
  gap: 12px;
  flex: 1;
  min-height: 0;
}

.matrix-panel, .sim-panel {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 16px;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.panel-header h3 {
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
}

.rules-table-wrap {
  flex: 1;
  overflow-y: auto;
}

.rules-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}

.rules-table th {
  background: #f8fafc;
  padding: 8px 10px;
  text-align: left;
  font-weight: 600;
  color: #475569;
  border-bottom: 1px solid var(--border-color);
  position: sticky;
  top: 0;
  z-index: 1;
}

.rules-table td {
  padding: 10px;
  border-bottom: 1px solid #f1f5f9;
  color: #1e293b;
}

.rules-table tbody tr:hover {
  background: #f8fafc;
}

.prio-tag {
  background: #e2e8f0;
  color: #334155;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 11px;
}

.rule-name-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.obj-badge {
  font-size: 10px;
  background: #eff6ff;
  color: #2563eb;
  padding: 1px 4px;
  border-radius: 3px;
  width: fit-content;
  font-family: monospace;
}

.scope-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
}

.scope-pill.all {
  background: #f1f5f9;
  color: #475569;
}

.scope-pill.white {
  background: #ecfdf5;
  color: #059669;
}

.scope-pill.black {
  background: #fff1f2;
  color: #e11d48;
}

.dept-tag {
  font-size: 11px;
  background: #f8fafc;
  border: 1px solid var(--border-color);
  padding: 2px 6px;
  border-radius: 4px;
  color: #334155;
}

.composite-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
}

.combine-badge {
  background: #3b82f6;
  color: #fff;
  font-size: 9px;
  font-weight: 800;
  padding: 1px 4px;
  border-radius: 3px;
}

.cond-badge {
  font-size: 10px;
  padding: 1px 6px;
  border-radius: 3px;
}

.cond-badge.header {
  background: #eff6ff;
  color: #1d4ed8;
  border: 1px solid #bfdbfe;
}

.cond-badge.line {
  background: #faf5ff;
  color: #7e22ce;
  border: 1px solid #e9d5ff;
}

.cond-expr-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: #fffbeb;
  color: #b45309;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 11px;
}

.target-def-cell {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  font-weight: 600;
  color: #2563eb;
}

.status-dot {
  font-size: 11px;
  font-weight: 600;
}

.status-dot.active {
  color: #059669;
}

.status-dot.inactive {
  color: #94a3b8;
}

.action-cell {
  display: flex;
  justify-content: flex-end;
  gap: 4px;
}

/* 模拟器 */
.sim-desc {
  font-size: 11px;
  color: #64748b;
  margin-bottom: 12px;
  line-height: 1.4;
}

.sim-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.lines-textarea {
  width: 100%;
  font-family: Consolas, monospace;
  font-size: 11px;
  padding: 6px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  background: #f8fafc;
}

.sim-result-card {
  border-radius: var(--radius-sm);
  padding: 10px 12px;
  font-size: 12px;
}

.sim-result-card.hit {
  background: #ecfdf5;
  border: 1px solid #a7f3d0;
  color: #065f46;
}

.sim-result-card.miss {
  background: #f8fafc;
  border: 1px solid var(--border-color);
  color: #475569;
}

.res-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 4px;
}

.res-body p {
  margin: 0;
  font-size: 11px;
}

.matched-detail {
  background: #fff;
  padding: 6px 8px;
  border-radius: 4px;
  margin-top: 6px;
  font-size: 11px;
}

/* 模态框与条件构建器 */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
  backdrop-filter: blur(2px);
}

.modal-card {
  background: #fff;
  border-radius: var(--radius-md);
  width: 90%;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 20px 25px -5px rgba(0,0,0,0.1);
}

.modal-lg {
  max-width: 780px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 20px;
  border-bottom: 1px solid var(--border-color);
}

.modal-header h3 {
  font-size: 15px;
  font-weight: 700;
  color: #0f172a;
}

.modal-body {
  padding: 16px 20px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-row {
  display: flex;
  gap: 12px;
}

.flex-1 { flex: 1; }
.flex-2 { flex: 2; }

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 11px;
  font-weight: 600;
  color: #334155;
}

.form-group input, .form-group select {
  padding: 7px 10px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  font-size: 12px;
}

.radio-cards {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
}

.radio-card {
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 8px 10px;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  cursor: pointer;
}

.radio-card.selected {
  border-color: #2563eb;
  background: #eff6ff;
}

.r-info {
  display: flex;
  flex-direction: column;
}

.r-info strong {
  font-size: 12px;
  color: #0f172a;
}

.r-info span {
  font-size: 10px;
  color: #64748b;
}

/* 条件构建器容器 */
.condition-builder-box {
  background: #f8fafc;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 12px;
}

.cb-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.cb-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #0f172a;
}

.cb-tabs {
  display: flex;
  background: #e2e8f0;
  padding: 2px;
  border-radius: 4px;
  gap: 2px;
}

.cb-tab-btn {
  display: flex;
  align-items: center;
  padding: 3px 8px;
  border: none;
  background: none;
  font-size: 11px;
  font-weight: 600;
  color: #475569;
  border-radius: 3px;
  cursor: pointer;
}

.cb-tab-btn.active {
  background: #fff;
  color: #2563eb;
}

.combine-row {
  display: flex;
  align-items: center;
  gap: 8px;
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  padding: 6px 10px;
  border-radius: var(--radius-sm);
  margin-bottom: 10px;
}

.combine-select {
  padding: 3px 6px;
  font-size: 11px;
  font-weight: 700;
  color: #1e40af;
  border-radius: 3px;
}

.combine-hint {
  font-size: 10px;
  color: #64748b;
}

.cond-group-block {
  background: #fff;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 8px 10px;
  margin-bottom: 8px;
}

.group-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  font-weight: 700;
  color: #334155;
  margin-bottom: 6px;
}

.empty-cond-tip {
  font-size: 11px;
  color: #94a3b8;
  padding: 6px 0;
  text-align: center;
}

.cond-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}

.cond-field-select, .cond-op-select {
  padding: 4px 6px;
  font-size: 11px;
  border: 1px solid var(--border-color);
  border-radius: 3px;
}

.cond-val-input {
  flex: 1;
  padding: 4px 8px;
  font-size: 11px;
  border: 1px solid var(--border-color);
  border-radius: 3px;
}

.btn-icon-danger {
  background: none;
  border: none;
  color: #f43f5e;
  cursor: pointer;
  padding: 4px;
  border-radius: 3px;
}

.btn-icon-danger:hover {
  background: #ffe4e6;
}

.raw-textarea {
  width: 100%;
  font-family: Consolas, monospace;
  font-size: 11px;
  padding: 8px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 20px;
  border-top: 1px solid var(--border-color);
}
</style>
