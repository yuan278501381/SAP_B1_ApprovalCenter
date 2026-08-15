<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import axios from 'axios'
import DocDataViewer from './DocDataViewer.vue'
import {
  CheckCircle2,
  XCircle,
  Clock,
  FileText,
  ShieldCheck,
  RefreshCw,
  Layers,
  Send,
  UserCheck,
  Zap,
  Bell,
  RotateCcw,
  CheckCheck,
  Search,
  Keyboard,
  Check,
  X,
  Building2
} from 'lucide-vue-next'

const API_BASE = import.meta.env.VITE_API_BASE || '/api/v1'
const launchParams = new URLSearchParams(window.location.search)
const launchCompanyId = launchParams.get('companyId') || 'DB_KCC'
const launchObjectCode = launchParams.get('objectCode')
const launchObjectKey = launchParams.get('objectKey')
const urlUser = launchParams.get('user') || launchParams.get('userCode')
const isSapEmbeddedUser = ref(!!urlUser)

// 当前操作员 (优先从 URL 参数 -> localStorage -> 默认 manager)
const currentUser = ref(urlUser || localStorage.getItem('sap_b1_approval_user') || 'manager')

// 业务账套真实全称/描述
const companyDisplayName = ref(launchCompanyId)

const currentScope = ref<'pending' | 'completed' | 'mine'>('pending')
const tasks = ref<any[]>([])
const activeTask = ref<any>(null)
const taskDetail = ref<any>(null)
const loading = ref(false)
const submittingDecision = ref(false)
const decisionComments = ref('')
const messageToast = ref<{ text: string; type: 'success' | 'error' } | null>(null)

// 待办搜索过滤
const taskSearchQuery = ref('')

// 快捷键帮助模态框
const showShortcutModal = ref(false)

// 快捷审批语标签
const QUICK_COMMENTS = [
  '同意，予以批准',
  '已核对物料与金额无误，请财务放行',
  '交期已确认，请安排排产',
  '单价不符合标准，请重新核算',
  '请补充包装与唛头详细要求'
]

const applyQuickComment = (text: string) => {
  decisionComments.value = text
}

// 快捷单据发起控制
const submittingDemo = ref(false)
const demoDocType = ref(launchObjectCode || 'CHORDR')
const demoDocKey = ref(launchObjectKey || '1001')

// 撤回审批申请模态框
const showRevokeModal = ref(false)
const revoking = ref(false)
const revokeReason = ref('')

// SAP 业务对象中文映射 (对齐 OUDO 及标准单据对象)
const SAP_OBJECT_NAMES: Record<string, string> = {
  CHORDR: '型号订单',
  CHOQUT: '型号报价单',
  ORDR: '销售订单',
  OQUT: '销售报价单',
  OPOR: '采购订单',
  OPDN: '采购收货单',
  ODLN: '销售交货单',
  OINV: '应收发票',
  OPCH: '应付发票'
}

const getObjectTypeName = (code?: string) => {
  if (!code) return '单据'
  const c = code.trim().toUpperCase()
  return SAP_OBJECT_NAMES[c] || c
}

// 不同单据类型专属配色方案
const getObjectStyle = (code?: string) => {
  const c = (code || '').trim().toUpperCase()
  switch (c) {
    case 'CHORDR':
      return { bg: '#eff6ff', border: '#bfdbfe', color: '#1d4ed8', numColor: '#2563eb' }
    case 'CHOQUT':
      return { bg: '#faf5ff', border: '#e9d5ff', color: '#7e22ce', numColor: '#9333ea' }
    case 'ORDR':
      return { bg: '#ecfdf5', border: '#a7f3d0', color: '#047857', numColor: '#059669' }
    case 'OQUT':
      return { bg: '#f0fdfa', border: '#99f6e4', color: '#0f766e', numColor: '#0d9488' }
    case 'OPOR':
    case 'OPDN':
      return { bg: '#fffbeb', border: '#fde68a', color: '#b45309', numColor: '#d97706' }
    case 'OINV':
    case 'OPCH':
      return { bg: '#fff1f2', border: '#fecdd3', color: '#be123c', numColor: '#e11d48' }
    default:
      return { bg: '#f1f5f9', border: '#cbd5e1', color: '#334155', numColor: '#475569' }
  }
}

// 站内消息通知中心抽屉
const showNotifDrawer = ref(false)
const notifications = ref<any[]>([])
const unreadCount = ref(0)
const notifLoading = ref(false)

const api = axios.create({ baseURL: API_BASE })

// 全局请求拦截器：无缝透传用户身份与链路追踪 TraceID
api.interceptors.request.use((config) => {
  config.headers['X-Approval-User'] = currentUser.value
  config.headers['X-Approval-User-Name'] = currentUser.value
  if (!config.headers['X-Trace-Id']) {
    config.headers['X-Trace-Id'] = 'trace_fe_' + Math.random().toString(36).substring(2, 9)
  }
  return config
})

const onUserChange = () => {
  localStorage.setItem('sap_b1_approval_user', currentUser.value)
  showToast(`已切换操作员身份为: ${currentUser.value}`)
  loadTasks()
  loadNotifications()
}

const showToast = (text: string, type: 'success' | 'error' = 'success') => {
  messageToast.value = { text, type }
  setTimeout(() => {
    messageToast.value = null
  }, 3500)
}

// 加载 SAP 公司真实名称
const loadCompanyInfo = async () => {
  try {
    const res = await api.get('/metadata/company', {
      params: { companyId: launchCompanyId }
    })
    if (res.data?.success && res.data?.data?.companyName) {
      companyDisplayName.value = res.data.data.companyName
    }
  } catch {}
}

// 客户端单据详情内存高速缓存与请求并发取消
const taskDetailCache = new Map<string, any>()
let activeAbortCtrl: AbortController | null = null
let navDebounceTimer: any = null

const prefetchTaskDetails = (taskList: any[]) => {
  if (!taskList || taskList.length === 0) return
  const toPrefetch = taskList.slice(0, 8)
  toPrefetch.forEach(async (t) => {
    if (!taskDetailCache.has(t.taskId)) {
      try {
        const res = await api.get(`/tasks/${t.taskId}`)
        if (res.data?.success && res.data?.data) {
          taskDetailCache.set(t.taskId, res.data.data)
        }
      } catch {}
    }
  })
}

const loadTasks = async () => {
  loading.value = true
  try {
    const res = await api.get('/tasks', {
      params: {
        scope: currentScope.value,
        status: currentScope.value === 'mine' ? undefined : currentScope.value,
        companyId: launchCompanyId,
        objectCode: launchObjectCode || undefined,
        objectKey: launchObjectKey || undefined
      }
    })
    tasks.value = res.data.data.items || []
    if (tasks.value.length > 0) {
      const found = tasks.value.find(t => t.taskId === activeTask.value?.taskId)
      selectTask(found || tasks.value[0])
      prefetchTaskDetails(tasks.value)
    } else {
      activeTask.value = null
      taskDetail.value = null
    }
  } catch (err: any) {
    showToast(err.response?.data?.message || err.message || '加载任务列表失败', 'error')
  } finally {
    loading.value = false
  }
}

// 待办搜索过滤列表
const filteredTasks = computed(() => {
  if (!taskSearchQuery.value.trim()) return tasks.value
  const q = taskSearchQuery.value.trim().toLowerCase()
  return tasks.value.filter(t =>
    t.title?.toLowerCase().includes(q) ||
    t.objectKey?.toLowerCase().includes(q) ||
    getObjectTypeName(t.objectCode).toLowerCase().includes(q) ||
    t.submitter?.toLowerCase().includes(q) ||
    t.nodeName?.toLowerCase().includes(q)
  )
})

const selectTask = async (task: any, forceRefresh = false) => {
  if (!task) return
  activeTask.value = task

  // 1. 命中内存缓存时，0ms 瞬间渲染无感知切换！
  if (!forceRefresh && taskDetailCache.has(task.taskId)) {
    taskDetail.value = taskDetailCache.get(task.taskId)
    return
  }

  // 2. 取消上一个未完成的网络请求，避免连续按键时的并发冲突与竞态重绘
  if (activeAbortCtrl) {
    activeAbortCtrl.abort()
  }
  activeAbortCtrl = new AbortController()

  try {
    const res = await api.get(`/tasks/${task.taskId}`, {
      signal: activeAbortCtrl.signal
    })
    if (res.data?.success && res.data?.data) {
      taskDetailCache.set(task.taskId, res.data.data)
      if (activeTask.value?.taskId === task.taskId) {
        taskDetail.value = res.data.data
      }
    }
  } catch (err: any) {
    if (err?.name !== 'CanceledError' && !axios.isCancel(err)) {
      showToast('加载单据审批详情失败', 'error')
    }
  }
}

const handleDecision = async (decision: 'Approve' | 'Reject' | 'Return') => {
  if (!activeTask.value) return
  submittingDecision.value = true
  const currentTaskId = activeTask.value.taskId
  try {
    await api.post(`/tasks/${currentTaskId}/decisions`, {
      decision,
      comments: decisionComments.value || undefined
    })
    showToast(`审批已处理完成: 【${decision}】`)
    decisionComments.value = ''
    taskDetailCache.delete(currentTaskId)
    await loadTasks()
    await loadNotifications()
  } catch (err: any) {
    showToast(err.response?.data?.message || '处理审批决策失败', 'error')
  } finally {
    submittingDecision.value = false
  }
}

// 键盘快捷键导航 (极速响应与防抖优化)
const navigateTask = (offset: number) => {
  const list = filteredTasks.value
  if (list.length === 0) return
  const currIdx = list.findIndex(t => t.taskId === activeTask.value?.taskId)
  let nextIdx = currIdx + offset
  if (nextIdx < 0) nextIdx = 0
  if (nextIdx >= list.length) nextIdx = list.length - 1

  const targetTask = list[nextIdx]
  activeTask.value = targetTask // 即刻点亮左侧待办卡片，0ms UI 响应！

  clearTimeout(navDebounceTimer)
  if (taskDetailCache.has(targetTask.taskId)) {
    taskDetail.value = taskDetailCache.get(targetTask.taskId)
  } else {
    navDebounceTimer = setTimeout(() => {
      selectTask(targetTask)
    }, 25)
  }
}

const onGlobalKeyDown = (e: KeyboardEvent) => {
  const target = e.target as HTMLElement
  const isInput = target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')

  if (isInput && !e.altKey) return

  if (e.key === 'j' || e.key === 'ArrowDown') {
    e.preventDefault()
    navigateTask(1)
  } else if (e.key === 'k' || e.key === 'ArrowUp') {
    e.preventDefault()
    navigateTask(-1)
  } else if (e.altKey && (e.key === 'a' || e.key === 'A')) {
    e.preventDefault()
    if (activeTask.value?.status === 'Pending') {
      handleDecision('Approve')
    }
  } else if (e.key === '?') {
    e.preventDefault()
    showShortcutModal.value = !showShortcutModal.value
  }
}

// 发起人撤回审批
const openRevokeModal = () => {
  revokeReason.value = ''
  showRevokeModal.value = true
}

const handleRevoke = async () => {
  if (!taskDetail.value?.instance?.id) return
  revoking.value = true
  try {
    await api.post(`/instances/${taskDetail.value.instance.id}/revoke`, {
      reason: revokeReason.value || '发起人主动撤销'
    })
    showToast('审批申请已撤回，相关待办已关闭并已通知审批人')
    showRevokeModal.value = false
    await loadTasks()
    await loadNotifications()
  } catch (err: any) {
    showToast(err.response?.data?.message || '撤回审批失败', 'error')
  } finally {
    revoking.value = false
  }
}

// 站内通知查询
const loadNotifications = async () => {
  notifLoading.value = true
  try {
    const res = await api.get('/notifications')
    notifications.value = res.data.data?.items || []
    unreadCount.value = res.data.data?.unreadCount || 0
  } catch (err) {
    // 忽略
  } finally {
    notifLoading.value = false
  }
}

const markNotifRead = async (n: any) => {
  if (n.isRead) return
  try {
    await api.post(`/notifications/${n.id}/read`)
    n.isRead = true
    unreadCount.value = Math.max(0, unreadCount.value - 1)
  } catch {}
}

const markAllNotifsRead = async () => {
  try {
    await api.post('/notifications/read-all')
    notifications.value.forEach(n => n.isRead = true)
    unreadCount.value = 0
    showToast('已全部标记为已读')
  } catch {}
}

const submitDocApproval = async (docType: string, docKey: string) => {
  if (!docKey.trim()) {
    showToast('请输入有效的 SAP 单据 Key / DocEntry', 'error')
    return
  }
  submittingDemo.value = true
  try {
    await api.post(`/objects/${docType}/${docKey}/submit?companyId=${launchCompanyId}`, null, {
      headers: {
        'Idempotency-Key': 'sub_' + docType + '_' + docKey + '_' + Date.now()
      }
    })
    showToast(`单据 ${docType} #${docKey} 已成功发起审批！`)
    await loadTasks()
    await loadNotifications()
  } catch (err: any) {
    showToast(err.response?.data?.message || '发起审批失败', 'error')
  } finally {
    submittingDemo.value = false
  }
}

// 判断当前单据是否允许撤回
const canRevokeCurrentDoc = computed(() => {
  if (!taskDetail.value?.instance) return false
  const inst = taskDetail.value.instance
  const isRunning = inst.status === 'Running'
  const isSubmitter = inst.submitterCode === currentUser.value || currentUser.value === 'admin'
  return isRunning && isSubmitter
})

onMounted(() => {
  loadCompanyInfo()
  loadTasks()
  loadNotifications()
  window.addEventListener('keydown', onGlobalKeyDown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', onGlobalKeyDown)
})
</script>

<template>
  <div class="workbench-container">
    <!-- 1. 顶部全局导航栏 -->
    <header class="top-nav">
      <div class="brand">
        <div class="brand-icon">
          <Layers class="w-5 h-5 text-white" />
        </div>
        <div class="brand-info">
          <h1>SAP B1 通用审批中心</h1>
          <span class="sub-text">企业级 BPMN 2.0 规范与多对象通用审批流转平台</span>
        </div>
      </div>

      <div class="nav-controls">
        <!-- 业务账套真实描述名称展示 -->
        <div class="company-badge" title="当前登录的 SAP Business One 业务公司账套">
          <Building2 class="w-4 h-4 text-blue-600 mr-1" />
          <span>公司：<strong>{{ companyDisplayName }}</strong></span>
        </div>

        <!-- 键盘快捷键提示按钮 -->
        <button class="icon-nav-btn" @click="showShortcutModal = true" title="查看键盘快捷键指南 (?)">
          <Keyboard class="w-4 h-4 text-slate-600" />
        </button>

        <!-- 站内通知铃铛 -->
        <button class="icon-nav-btn" @click="showNotifDrawer = !showNotifDrawer" title="站内通知消息">
          <Bell class="w-4 h-4 text-slate-700" />
          <span v-if="unreadCount > 0" class="notif-badge">{{ unreadCount }}</span>
        </button>

        <!-- 操作员身份指示器 / 切换器 -->
        <div v-if="isSapEmbeddedUser" class="company-badge user-badge">
          <UserCheck class="w-4 h-4 text-emerald-600 mr-1" />
          <span>操作员: <strong>{{ currentUser }}</strong></span>
        </div>
        <div v-else class="user-selector">
          <label>操作员:</label>
          <select v-model="currentUser" @change="onUserChange">
            <option value="manager">系统管理员 (manager) - 全权限</option>
            <option value="SALE01">業助主管 (SALE01 朱躍南) - 审核人</option>
            <option value="SALE02">销售业助 (SALE02 范冬梅) - 审核人</option>
            <option value="SALE03">销售制单 (SALE03 吴鑫梅) - 发起人</option>
            <option value="SALE04">销售代表 (SALE04 吴小平) - 业务员</option>
            <option value="admin">平台管理员 (admin) - 全权限</option>
          </select>
        </div>

        <button class="btn btn-secondary btn-sm" @click="loadTasks(); loadNotifications();" :disabled="loading">
          <RefreshCw :class="['w-3.5 h-3.5 mr-1', loading ? 'animate-spin' : '']" />
          <span>刷新</span>
        </button>
      </div>
    </header>

    <!-- 2. 快捷发起单据审批工具条 -->
    <div class="demo-bar">
      <div class="demo-desc">
        <Send class="w-3.5 h-3.5 text-blue-600 mr-1.5" />
        <span>快捷发起审批:</span>
      </div>
      <div class="demo-actions">
        <select v-model="demoDocType" class="demo-select">
          <option value="CHORDR">型号订单 (CHORDR)</option>
          <option value="CHOQUT">型号报价单 (CHOQUT)</option>
        </select>
        <input v-model="demoDocKey" placeholder="单号如1702" class="demo-input" />
        <button
          class="btn btn-primary btn-sm"
          :disabled="submittingDemo"
          @click="submitDocApproval(demoDocType, demoDocKey)"
        >
          <span>{{ submittingDemo ? '发起中...' : '发起审批流转' }}</span>
        </button>
      </div>
    </div>

    <!-- 3. 世界级三栏式黄金分割工作台核心主体 -->
    <div class="workbench-three-pane">
      <!-- ================= 左栏：极简待办与任务队列 (280px) ================= -->
      <aside class="pane-left-queue card">
        <div class="queue-header">
          <div class="search-queue-box">
            <Search class="w-3.5 h-3.5 text-slate-400 mr-1.5" />
            <input
              v-model="taskSearchQuery"
              placeholder="搜索单号/客户/节点/提交人..."
              class="queue-search-input"
            />
          </div>

          <div class="scope-tabs">
            <button
              :class="['scope-tab', currentScope === 'pending' ? 'active' : '']"
              @click="currentScope = 'pending'; loadTasks()"
            >
              待我审批
              <span v-if="currentScope === 'pending' && tasks.length > 0" class="badge-count">
                {{ tasks.length }}
              </span>
            </button>
            <button
              :class="['scope-tab', currentScope === 'completed' ? 'active' : '']"
              @click="currentScope = 'completed'; loadTasks()"
            >
              我已处理
            </button>
            <button
              :class="['scope-tab', currentScope === 'mine' ? 'active' : '']"
              @click="currentScope = 'mine'; loadTasks()"
            >
              我发起的
            </button>
          </div>
        </div>

        <div class="task-list-scroll">
          <div v-if="loading && tasks.length === 0" class="loading-box">
            <RefreshCw class="w-6 h-6 animate-spin text-blue-600" />
            <span>加载待办中...</span>
          </div>

          <div v-else-if="filteredTasks.length === 0" class="empty-box">
            <CheckCircle2 class="w-9 h-9 text-slate-300 mb-1" />
            <p>暂无相关审批单据</p>
          </div>

          <div
            v-for="t in filteredTasks"
            :key="t.taskId"
            :class="['task-card', activeTask?.taskId === t.taskId ? 'selected' : '']"
            @click="selectTask(t)"
          >
            <div class="task-card-header">
              <div
                class="obj-badge-wrap"
                :style="{
                  backgroundColor: getObjectStyle(t.objectCode).bg,
                  borderColor: getObjectStyle(t.objectCode).border
                }"
              >
                <span class="obj-type-name" :style="{ color: getObjectStyle(t.objectCode).color }">
                  {{ getObjectTypeName(t.objectCode) }}
                </span>
                <span class="obj-doc-num" :style="{ color: getObjectStyle(t.objectCode).numColor }">
                  #{{ t.objectKey }}
                </span>
              </div>
              <span :class="['badge', t.status === 'Pending' ? 'badge-pending' : (t.status === 'Cancelled' ? 'badge-cancelled' : 'badge-approved')]">
                {{ t.status === 'Pending' ? '待审批' : (t.status === 'Cancelled' ? '已撤回' : '已完成') }}
              </span>
            </div>

            <div class="task-title" :title="t.title">{{ t.title }}</div>

            <div class="task-meta-row">
              <span class="node-tag">节点: <strong>{{ t.nodeName }}</strong></span>
              <span class="submitter-tag">{{ t.submitter }}</span>
            </div>

            <div class="task-time-row">
              <Clock class="w-3 h-3 text-slate-400 mr-1" />
              <span>{{ new Date(t.createdAt).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }) }}</span>
            </div>
          </div>
        </div>

        <div class="queue-footer-tips">
          <Keyboard class="w-3 h-3 text-slate-400 mr-1" />
          <span>按 <strong>J</strong> / <strong>K</strong> 可快速上下切换单据</span>
        </div>
      </aside>

      <!-- ================= 中栏：单据核心工作视窗 (Flex-1, 0滚动一屏聚焦) ================= -->
      <main class="pane-center-doc card">
        <div v-if="!activeTask || !taskDetail" class="empty-detail">
          <div v-if="launchObjectCode && launchObjectKey" class="sap-launch-guide">
            <Layers class="w-12 h-12 text-blue-500 mb-2" />
            <h3>{{ getObjectTypeName(launchObjectCode) }} #{{ launchObjectKey }}</h3>
            <p class="guide-text">当前操作员 ({{ currentUser }}) 暂无该单据的直接待办任务。</p>
            <p class="guide-sub">如果该单据尚未进入审批流，您可以直接点击下方按钮发起审批：</p>
            <button
              class="btn btn-primary"
              :disabled="submittingDemo"
              @click="submitDocApproval(launchObjectCode!, launchObjectKey!)"
            >
              <span>{{ submittingDemo ? '正在发起...' : '立即为此单据发起审批申请' }}</span>
            </button>
          </div>
          <div v-else>
            <FileText class="w-12 h-12 text-slate-300" />
            <p>请选择左侧单据以查看明细与审批轨迹</p>
          </div>
        </div>

        <div v-else class="doc-view-scroll">
          <!-- 单据表头极简条 (含防篡改安全盾牌与 SAP 穿透小黄箭头) -->
          <div class="doc-micro-header">
            <div class="micro-left">
              <span
                class="tag-obj-pill"
                :style="{
                  backgroundColor: getObjectStyle(taskDetail.instance?.objectCode).bg,
                  borderColor: getObjectStyle(taskDetail.instance?.objectCode).border,
                  color: getObjectStyle(taskDetail.instance?.objectCode).color
                }"
              >
                {{ getObjectTypeName(taskDetail.instance?.objectCode) }} #{{ taskDetail.instance?.objectKey }}
              </span>
              <h2 class="doc-main-title">{{ taskDetail.instance?.title }}</h2>
            </div>

            <div class="micro-right">
              <!-- 防篡改 SHA-256 安全盾牌徽章 (悬浮展开完整指纹) -->
              <div
                class="security-seal"
                :title="'不可变规范化快照签名 (SHA-256): ' + taskDetail.snapshot?.dataSha256"
              >
                <ShieldCheck class="w-3.5 h-3.5 text-emerald-600 mr-1" />
                <span>SHA-256 已验真</span>
              </div>

              <!-- SAP 客户端穿透黄箭头提示 -->
              <div class="sap-link-pill" title="在 SAP B1 客户端窗口点击单号旁黄色箭头可直接穿透查看原始单据">
                <Zap class="w-3.5 h-3.5 text-amber-500 mr-1" />
                <span>SAP 黄箭头穿透</span>
              </div>

              <!-- 发起人撤回按钮 -->
              <button
                v-if="canRevokeCurrentDoc"
                class="btn btn-secondary btn-xs text-rose-600"
                @click="openRevokeModal"
                title="撤回当前审批申请"
              >
                <RotateCcw class="w-3 h-3 mr-1" />
                <span>撤回</span>
              </button>
            </div>
          </div>

          <!-- 单据结构化明细与可视化数据展示 (支持双栏定制与云端分层) -->
          <DocDataViewer
            :rawJson="taskDetail.snapshot?.rawJson || '{}'"
            :objectCode="taskDetail.instance?.objectCode || activeTask?.objectCode"
            :companyId="launchCompanyId"
          />
        </div>
      </main>

      <!-- ================= 右栏：常驻流转轨迹与决策控制台 (330px) ================= -->
      <aside class="pane-right-inspector card">
        <div class="inspector-header">
          <div class="inspector-title">
            <Clock class="w-4 h-4 text-blue-600 mr-1.5" />
            <span>审批轨迹与决策台</span>
          </div>
          <span
            v-if="taskDetail?.instance"
            :class="['status-chip', 'chip-' + taskDetail.instance.status?.toLowerCase()]"
          >
            {{ taskDetail.instance.status }}
          </span>
        </div>

        <div class="inspector-body-scroll">
          <!-- 垂直时序轨迹图 (Vertical Stepper Timeline) -->
          <div class="stepper-timeline">
            <div
              v-for="(log, idx) in (taskDetail?.auditLogs || [])"
              :key="log.id || String(idx)"
              class="stepper-item"
            >
              <div class="stepper-line" v-if="Number(idx) < (taskDetail?.auditLogs?.length || 1) - 1"></div>
              <div class="stepper-dot" :class="[log.action === 'Revoke' ? 'dot-revoke' : (log.action === 'Approve' ? 'dot-approve' : (log.action === 'Reject' ? 'dot-reject' : 'dot-default'))]">
                <Check v-if="log.action === 'Approve'" class="w-3 h-3 text-white" />
                <X v-else-if="log.action === 'Reject'" class="w-3 h-3 text-white" />
                <RotateCcw v-else-if="log.action === 'Revoke'" class="w-3 h-3 text-white" />
                <span v-else class="dot-inner"></span>
              </div>
              <div class="stepper-content">
                <div class="stepper-top-row">
                  <span class="stepper-user font-bold">{{ log.operatorName || log.operatorCode }}</span>
                  <span class="stepper-time">{{ new Date(log.actionTime).toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }) }}</span>
                </div>
                <div class="stepper-action-tag">
                  <span :class="['action-badge', 'badge-act-' + log.action?.toLowerCase()]">
                    {{ log.action === 'Approve' ? '同意放行' : (log.action === 'Reject' ? '拒绝终止' : (log.action === 'Submit' ? '发起审批' : (log.action === 'Revoke' ? '撤回申请' : log.action))) }}
                  </span>
                </div>
                <p v-if="log.comment" class="stepper-comment">
                  “{{ log.comment }}”
                </p>
              </div>
            </div>

            <!-- 当前待办节点指示 -->
            <div v-if="activeTask?.status === 'Pending'" class="stepper-item current-step">
              <div class="stepper-dot dot-active animate-pulse">
                <span class="pulse-core"></span>
              </div>
              <div class="stepper-content">
                <div class="stepper-top-row">
                  <span class="stepper-user font-bold text-blue-600">当前节点：{{ activeTask.nodeName }}</span>
                </div>
                <div class="stepper-action-tag">
                  <span class="action-badge badge-act-pending">等待审批处理</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- 底部审批决策控制台 (常驻视口，无需滚动) -->
        <div v-if="activeTask?.status === 'Pending' && currentScope !== 'mine'" class="inspector-decision-footer">
          <div class="quick-chips-wrap">
            <span class="quick-title">快捷意见:</span>
            <div class="chips-list">
              <button
                v-for="c in QUICK_COMMENTS"
                :key="c"
                class="chip-btn"
                @click="applyQuickComment(c)"
              >
                {{ c }}
              </button>
            </div>
          </div>

          <div class="decision-input-wrap">
            <textarea
              v-model="decisionComments"
              placeholder="请输入审批处理意见 (选填，支持快捷语)..."
              rows="2"
              class="decision-textarea"
            ></textarea>
          </div>

          <div class="decision-buttons-grid">
            <button
              class="btn btn-decision-approve"
              :disabled="submittingDecision"
              @click="handleDecision('Approve')"
              title="同意放行 (快捷键: Alt + A)"
            >
              <CheckCircle2 class="w-4 h-4 mr-1" />
              <span>同意 (放行)</span>
            </button>
            <button
              class="btn btn-decision-reject"
              :disabled="submittingDecision"
              @click="handleDecision('Reject')"
              title="拒绝并终止审批"
            >
              <XCircle class="w-4 h-4 mr-1" />
              <span>拒绝</span>
            </button>
            <button
              class="btn btn-decision-return"
              :disabled="submittingDecision"
              @click="handleDecision('Return')"
              title="退回申请人重新修改"
            >
              <RotateCcw class="w-4 h-4 mr-1" />
              <span>退回</span>
            </button>
          </div>
        </div>

        <!-- 已完成或只读状态展示 -->
        <div v-else class="inspector-readonly-footer">
          <div class="seal-box">
            <CheckCheck class="w-5 h-5 text-slate-400 mr-2" />
            <span>当前任务已完成或仅供查看</span>
          </div>
        </div>
      </aside>
    </div>

    <!-- 站内通知抽屉 -->
    <div v-if="showNotifDrawer" class="notif-drawer-backdrop" @click.self="showNotifDrawer = false">
      <div class="notif-drawer-panel">
        <div class="notif-header">
          <div class="notif-title">
            <Bell class="w-4 h-4 text-blue-600 mr-2" />
            <span>站内通知与审批提醒 ({{ notifications.length }})</span>
          </div>
          <div class="notif-actions">
            <button class="btn btn-secondary btn-xs" @click="markAllNotifsRead">全部已读</button>
            <button class="btn-close-drawer" @click="showNotifDrawer = false"><X class="w-4 h-4" /></button>
          </div>
        </div>
        <div class="notif-list">
          <div
            v-for="n in notifications"
            :key="n.id"
            class="notif-item"
            :class="[n.isRead ? 'read' : 'unread']"
            @click="markNotifRead(n)"
          >
            <div class="notif-dot" v-if="!n.isRead"></div>
            <div class="notif-content">
              <div class="notif-item-title">{{ n.title }}</div>
              <div class="notif-item-body">{{ n.body }}</div>
              <div class="notif-item-time">{{ new Date(n.createdAt).toLocaleString('zh-CN') }}</div>
            </div>
          </div>
          <div v-if="notifications.length === 0" class="empty-list">暂无站内通知消息</div>
        </div>
      </div>
    </div>

    <!-- 键盘快捷键指南模态框 -->
    <div v-if="showShortcutModal" class="shortcut-backdrop" @click.self="showShortcutModal = false">
      <div class="shortcut-card">
        <div class="shortcut-header">
          <div class="shortcut-title">
            <Keyboard class="w-4 h-4 text-blue-600 mr-2" />
            <span>极客键盘盲操快捷键指南</span>
          </div>
          <button class="btn-close-drawer" @click="showShortcutModal = false"><X class="w-4 h-4" /></button>
        </div>
        <div class="shortcut-body">
          <div class="shortcut-row">
            <div class="keys-wrap"><kbd>J</kbd> 或 <kbd>↓</kbd></div>
            <span class="shortcut-desc">切换到下一个待办单据</span>
          </div>
          <div class="shortcut-row">
            <div class="keys-wrap"><kbd>K</kbd> 或 <kbd>↑</kbd></div>
            <span class="shortcut-desc">切换到上一个待办单据</span>
          </div>
          <div class="shortcut-row">
            <div class="keys-wrap"><kbd>Alt</kbd> + <kbd>A</kbd></div>
            <span class="shortcut-desc">快速同意并放行当前单据</span>
          </div>
          <div class="shortcut-row">
            <div class="keys-wrap"><kbd>?</kbd></div>
            <span class="shortcut-desc">打开 / 关闭此快捷键指南</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 撤回审批申请模态框 -->
    <div v-if="showRevokeModal" class="modal-backdrop">
      <div class="modal-card">
        <div class="modal-header">
          <div class="modal-title">
            <RotateCcw class="w-4 h-4 text-rose-600 mr-1.5" />
            <span>撤回审批流转申请</span>
          </div>
          <button class="btn-close-drawer" @click="showRevokeModal = false"><X class="w-4 h-4" /></button>
        </div>
        <div class="modal-body">
          <p class="text-sm text-slate-600 mb-2">撤回后，当前所有审批人的待办任务将立即作废并收到撤回通知。</p>
          <textarea v-model="revokeReason" placeholder="请输入撤回原因..." rows="3" class="decision-textarea"></textarea>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary btn-sm" @click="showRevokeModal = false">取消</button>
          <button class="btn btn-danger btn-sm" :disabled="revoking" @click="handleRevoke">
            <span>{{ revoking ? '正在撤回...' : '确认撤回' }}</span>
          </button>
        </div>
      </div>
    </div>

    <!-- 全局操作 Toast 提示 -->
    <div v-if="messageToast" :class="['global-toast', messageToast.type]">
      {{ messageToast.text }}
    </div>
  </div>
</template>

<style scoped>
.workbench-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  box-sizing: border-box;
  background: #f1f5f9;
  overflow: hidden;
}

/* 顶部全局导航栏 */
.top-nav {
  height: 48px;
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0 16px;
  flex-shrink: 0;
}

.brand {
  display: flex;
  align-items: center;
  gap: 10px;
}

.brand-icon {
  width: 28px;
  height: 28px;
  background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.brand-info h1 {
  margin: 0;
  font-size: 14px;
  font-weight: 700;
  color: #0f172a;
  line-height: 1.2;
}

.brand-info .sub-text {
  font-size: 10px;
  color: #64748b;
}

.nav-controls {
  display: flex;
  align-items: center;
  gap: 10px;
}

.company-badge {
  display: flex;
  align-items: center;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  padding: 3px 10px;
  border-radius: 4px;
  font-size: 12px;
  color: #334155;
}

.icon-nav-btn {
  border: 1px solid #e2e8f0;
  background: #f8fafc;
  padding: 5px;
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
}

.icon-nav-btn:hover {
  background: #e2e8f0;
}

.notif-badge {
  position: absolute;
  top: -4px;
  right: -4px;
  background: #ef4444;
  color: #fff;
  font-size: 9px;
  font-weight: 700;
  min-width: 14px;
  height: 14px;
  border-radius: 7px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.user-selector {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
}

.user-selector select {
  padding: 3px 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 12px;
  background: #fff;
}

/* 快捷发起单据审批条 */
.demo-bar {
  height: 36px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 16px;
  font-size: 11.5px;
  color: #475569;
  flex-shrink: 0;
}

.demo-desc {
  display: flex;
  align-items: center;
}

.demo-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.demo-select, .demo-input {
  padding: 2px 6px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 11.5px;
}

/* ================= 三栏式核心布局 ================= */
.workbench-three-pane {
  flex: 1;
  display: flex;
  gap: 10px;
  padding: 10px;
  overflow: hidden;
  box-sizing: border-box;
}

.card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

/* 1. 左栏：待办队列 */
.pane-left-queue {
  width: 270px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.queue-header {
  padding: 10px;
  border-bottom: 1px solid #f1f5f9;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.search-queue-box {
  display: flex;
  align-items: center;
  padding: 4px 8px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 4px;
}

.queue-search-input {
  width: 100%;
  border: none;
  background: transparent;
  outline: none;
  font-size: 11.5px;
}

.scope-tabs {
  display: flex;
  background: #f1f5f9;
  padding: 2px;
  border-radius: 4px;
  gap: 2px;
}

.scope-tab {
  flex: 1;
  border: none;
  background: transparent;
  padding: 4px 0;
  font-size: 11px;
  font-weight: 600;
  color: #64748b;
  border-radius: 3px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
}

.scope-tab.active {
  background: #fff;
  color: #2563eb;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

.badge-count {
  background: #2563eb;
  color: #fff;
  font-size: 9px;
  padding: 0 4px;
  border-radius: 6px;
}

.task-list-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.task-card {
  padding: 8px 10px;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.task-card:hover {
  border-color: #93c5fd;
  background: #f8fafc;
}

.task-card.selected {
  border-color: #2563eb;
  background: #eff6ff;
  box-shadow: 0 0 0 1px #2563eb;
}

.task-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.obj-badge-wrap {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 1px 6px;
  border-radius: 4px;
  border: 1px solid;
  font-size: 10.5px;
}

.obj-type-name {
  font-weight: 700;
}

.obj-doc-num {
  font-family: monospace;
  font-weight: 700;
}

.task-title {
  font-size: 12px;
  font-weight: 600;
  color: #1e293b;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.task-meta-row {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  color: #64748b;
}

.task-time-row {
  display: flex;
  align-items: center;
  font-size: 10.5px;
  color: #94a3b8;
}

.queue-footer-tips {
  padding: 6px 10px;
  background: #f8fafc;
  border-top: 1px solid #f1f5f9;
  font-size: 10.5px;
  color: #64748b;
  display: flex;
  align-items: center;
}

/* 2. 中栏：单据核心视窗 */
.pane-center-doc {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.doc-view-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.doc-micro-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

.micro-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.tag-obj-pill {
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11.5px;
  font-weight: 700;
  border: 1px solid;
}

.doc-main-title {
  margin: 0;
  font-size: 14px;
  font-weight: 700;
  color: #0f172a;
}

.micro-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.security-seal {
  display: flex;
  align-items: center;
  background: #ecfdf5;
  border: 1px solid #a7f3d0;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  color: #047857;
  font-weight: 600;
  cursor: help;
}

.sap-link-pill {
  display: flex;
  align-items: center;
  background: #fffbeb;
  border: 1px solid #fde68a;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  color: #b45309;
  font-weight: 600;
}

/* 3. 右栏：常驻流转轨迹与决策控制台 */
.pane-right-inspector {
  width: 320px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.inspector-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  border-bottom: 1px solid #f1f5f9;
  background: #fdfdfd;
}

.inspector-title {
  display: flex;
  align-items: center;
  font-size: 13px;
  font-weight: 700;
  color: #0f172a;
}

.status-chip {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 12px;
  font-weight: 700;
}

.chip-running, .chip-pending {
  background: #eff6ff;
  color: #2563eb;
  border: 1px solid #bfdbfe;
}

.chip-approved {
  background: #ecfdf5;
  color: #059669;
  border: 1px solid #a7f3d0;
}

.chip-rejected {
  background: #fef2f2;
  color: #dc2626;
  border: 1px solid #fecaca;
}

.inspector-body-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 14px;
}

.stepper-timeline {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.stepper-item {
  display: flex;
  gap: 10px;
  position: relative;
}

.stepper-line {
  position: absolute;
  left: 11px;
  top: 22px;
  bottom: -16px;
  width: 2px;
  background: #e2e8f0;
}

.stepper-dot {
  width: 24px;
  height: 24px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  z-index: 1;
}

.dot-default {
  background: #e2e8f0;
  color: #64748b;
}

.dot-inner {
  width: 8px;
  height: 8px;
  border-radius: 4px;
  background: #94a3b8;
}

.dot-approve {
  background: #10b981;
}

.dot-reject {
  background: #ef4444;
}

.dot-revoke {
  background: #f59e0b;
}

.dot-active {
  background: #3b82f6;
  position: relative;
}

.pulse-core {
  width: 8px;
  height: 8px;
  border-radius: 4px;
  background: #ffffff;
}

.stepper-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.stepper-top-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.stepper-user {
  font-size: 12px;
  color: #1e293b;
}

.stepper-time {
  font-size: 10.5px;
  color: #94a3b8;
}

.action-badge {
  display: inline-block;
  font-size: 10px;
  padding: 1px 6px;
  border-radius: 3px;
  font-weight: 600;
}

.badge-act-approve { background: #ecfdf5; color: #059669; }
.badge-act-reject { background: #fef2f2; color: #dc2626; }
.badge-act-revoke { background: #fffbeb; color: #d97706; }
.badge-act-submit { background: #eff6ff; color: #2563eb; }
.badge-act-pending { background: #eff6ff; color: #1d4ed8; }

.stepper-comment {
  margin: 4px 0 0 0;
  font-size: 11.5px;
  color: #475569;
  background: #f8fafc;
  padding: 4px 8px;
  border-radius: 4px;
  border: 1px dashed #e2e8f0;
}

.inspector-decision-footer {
  padding: 12px;
  border-top: 1px solid #e2e8f0;
  background: #fdfdfd;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.quick-chips-wrap {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.quick-title {
  font-size: 10.5px;
  font-weight: 700;
  color: #64748b;
}

.chips-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.chip-btn {
  border: 1px solid #e2e8f0;
  background: #f8fafc;
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 10.5px;
  color: #334155;
  cursor: pointer;
  transition: all 0.15s;
}

.chip-btn:hover {
  background: #eff6ff;
  border-color: #bfdbfe;
  color: #2563eb;
}

.decision-textarea {
  width: 100%;
  box-sizing: border-box;
  padding: 6px 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 11.5px;
  outline: none;
  resize: vertical;
}

.decision-textarea:focus {
  border-color: #2563eb;
}

.decision-buttons-grid {
  display: grid;
  grid-template-columns: 2fr 1fr 1fr;
  gap: 6px;
}

.btn-decision-approve {
  background: linear-gradient(135deg, #059669 0%, #047857 100%);
  color: #fff;
  border: none;
  padding: 7px 0;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-decision-approve:hover:not(:disabled) {
  background: linear-gradient(135deg, #10b981 0%, #059669 100%);
}

.btn-decision-reject {
  background: #fef2f2;
  color: #dc2626;
  border: 1px solid #fecaca;
  padding: 7px 0;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-decision-reject:hover:not(:disabled) {
  background: #fee2e2;
}

.btn-decision-return {
  background: #fffbeb;
  color: #b45309;
  border: 1px solid #fde68a;
  padding: 7px 0;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-decision-return:hover:not(:disabled) {
  background: #fef3c7;
}

.inspector-readonly-footer {
  padding: 12px;
  border-top: 1px solid #e2e8f0;
  background: #f8fafc;
}

.seal-box {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11.5px;
  color: #64748b;
}

/* 快捷键弹窗 */
.shortcut-backdrop, .modal-backdrop, .notif-drawer-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.5);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
}

.shortcut-card, .modal-card {
  background: #fff;
  border-radius: 8px;
  width: 440px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
  overflow: hidden;
}

.shortcut-header, .modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid #e2e8f0;
}

.shortcut-title, .modal-title {
  display: flex;
  align-items: center;
  font-size: 13px;
  font-weight: 700;
}

.shortcut-body {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.shortcut-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
}

.keys-wrap kbd {
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  border-radius: 3px;
  padding: 2px 6px;
  font-size: 11px;
  font-family: monospace;
  box-shadow: 0 1px 1px rgba(0, 0, 0, 0.1);
}

.shortcut-desc {
  color: #475569;
}

/* 站内通知抽屉 */
.notif-drawer-backdrop {
  justify-content: flex-end;
}

.notif-drawer-panel {
  width: 360px;
  height: 100vh;
  background: #fff;
  box-shadow: -8px 0 24px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
}

.notif-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid #e2e8f0;
}

.notif-title {
  display: flex;
  align-items: center;
  font-size: 13px;
  font-weight: 700;
}

.notif-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.notif-list {
  flex: 1;
  overflow-y: auto;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.notif-item {
  padding: 8px 10px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  gap: 8px;
  position: relative;
}

.notif-item.unread {
  background: #eff6ff;
  border-color: #bfdbfe;
}

.notif-dot {
  width: 6px;
  height: 6px;
  border-radius: 3px;
  background: #ef4444;
  margin-top: 4px;
}

.notif-content {
  flex: 1;
}

.notif-item-title {
  font-size: 12px;
  font-weight: 700;
  color: #1e293b;
}

.notif-item-body {
  font-size: 11.5px;
  color: #475569;
}

.notif-item-time {
  font-size: 10px;
  color: #94a3b8;
  margin-top: 2px;
}

.global-toast {
  position: fixed;
  top: 16px;
  left: 50%;
  transform: translateX(-50%);
  padding: 8px 20px;
  border-radius: 6px;
  font-size: 12.5px;
  font-weight: 600;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  z-index: 99999;
  animation: fadeIn 0.15s ease-out;
}

.global-toast.success { background: #065f46; color: #fff; }
.global-toast.error { background: #991b1b; color: #fff; }
</style>
