<script setup lang="ts">
import { ref, onMounted } from 'vue'
import axios from 'axios'
import {
  CheckCircle2,
  XCircle,
  Clock,
  FileText,
  ShieldCheck,
  RefreshCw,
  ExternalLink,
  Layers,
  Send
} from 'lucide-vue-next'

const API_BASE = import.meta.env.VITE_API_BASE || '/api/v1'
const isDevelopment = import.meta.env.DEV
const launchParams = new URLSearchParams(window.location.search)
const launchCompanyId = launchParams.get('companyId') || 'DB_KCC'
const launchObjectCode = launchParams.get('objectCode')
const launchObjectKey = launchParams.get('objectKey')

const currentUser = ref('director')
const currentScope = ref<'pending' | 'completed'>('pending')
const tasks = ref<any[]>([])
const activeTask = ref<any>(null)
const taskDetail = ref<any>(null)
const loading = ref(false)
const submittingDecision = ref(false)
const decisionComments = ref('')
const messageToast = ref<{ text: string; type: 'success' | 'error' } | null>(null)

// 演示用快速提交单据
const submittingDemo = ref(false)
const demoDocType = ref('CHORDR')
const demoDocKey = ref('1001')

const api = axios.create({ baseURL: API_BASE })
api.interceptors.request.use((config) => {
  // 仅本地开发时模拟网关注入身份。生产环境必须由受信任反向代理认证并注入，浏览器不能自行指定。
  if (isDevelopment) {
    config.headers['X-Approval-User'] = currentUser.value
    config.headers['X-Approval-User-Name'] = currentUser.value
  }
  return config
})

const showToast = (text: string, type: 'success' | 'error' = 'success') => {
  messageToast.value = { text, type }
  setTimeout(() => {
    messageToast.value = null
  }, 3500)
}

const loadTasks = async () => {
  loading.value = true
  try {
    const res = await api.get('/tasks', {
      params: {
        scope: 'mine',
        status: currentScope.value,
        companyId: launchCompanyId,
        objectCode: launchObjectCode || undefined,
        objectKey: launchObjectKey || undefined
      }
    })
    tasks.value = res.data.data.items || []
    if (tasks.value.length > 0 && !activeTask.value) {
      selectTask(tasks.value[0])
    } else if (tasks.value.length === 0) {
      activeTask.value = null
      taskDetail.value = null
    }
  } catch (err: any) {
    showToast(err.message || '加载任务列表失败', 'error')
  } finally {
    loading.value = false
  }
}

const selectTask = async (task: any) => {
  activeTask.value = task
  loading.value = true
  try {
    const res = await api.get(`/tasks/${task.taskId}`)
    taskDetail.value = res.data.data
  } catch (err: any) {
    showToast('获取任务明细失败', 'error')
  } finally {
    loading.value = false
  }
}

const handleDecision = async (decision: 'Approve' | 'Reject' | 'Return') => {
  if (!activeTask.value) return
  submittingDecision.value = true
  try {
    const traceId = 'trace_web_' + Math.random().toString(36).substring(2, 9)
    await api.post(
      `/tasks/${activeTask.value.taskId}/decisions`,
      {
        decision,
        comments: decisionComments.value || (decision === 'Approve' ? '审核通过，放行执行' : '审核不通过，予以驳回')
      },
      {
        headers: {
          'Idempotency-Key': crypto.randomUUID(),
          'X-Trace-Id': traceId
        }
      }
    )

    const label = decision === 'Approve' ? '同意放行' : decision === 'Reject' ? '拒绝终止' : '退回修改'
    showToast(`审批成功: 已执行 [${label}]`)
    decisionComments.value = ''
    await loadTasks()
  } catch (err: any) {
    showToast(err.response?.data?.message || '审批处理失败', 'error')
  } finally {
    submittingDecision.value = false
  }
}

const submitDemoDoc = async () => {
  submittingDemo.value = true
  try {
    await api.post(`/objects/${demoDocType.value}/${demoDocKey.value}/submit?companyId=${encodeURIComponent(launchCompanyId)}`, null, {
      headers: { 'Idempotency-Key': crypto.randomUUID() }
    })
    showToast(`单据 ${demoDocType.value} #${demoDocKey.value} 已成功发起审批！`)
    await loadTasks()
  } catch (err: any) {
    showToast(err.response?.data?.message || '发起审批失败', 'error')
  } finally {
    submittingDemo.value = false
  }
}

onMounted(() => {
  loadTasks()
})
</script>

<template>
  <div class="workbench-container">
    <!-- 顶部状态栏 -->
    <header class="top-nav">
      <div class="brand">
        <div class="brand-icon">
          <Layers class="w-5 h-5 text-white" />
        </div>
        <div class="brand-info">
          <h1>SAP B1 通用审批中心</h1>
          <span class="sub-text">企业级多对象通用审批流转平台 (Clean Architecture)</span>
        </div>
      </div>

      <div class="nav-controls">
        <div class="company-badge">
          <span class="indicator"></span>
          <span>公司库: <strong>{{ launchCompanyId }}</strong> (SQL Server)</span>
        </div>

        <div v-if="isDevelopment" class="user-selector">
          <label>模拟当前操作人:</label>
          <select v-model="currentUser" @change="loadTasks">
            <option value="director">业务总监 (director) - 终审</option>
            <option value="manager">部门主管 (manager) - 初审</option>
            <option value="sales_mgr">销售经理 (sales_mgr) - 报价单</option>
            <option value="admin">系统管理员 (admin)</option>
          </select>
        </div>
        <div v-else class="company-badge">用户身份由统一认证提供</div>

        <button class="btn btn-secondary" @click="loadTasks" :disabled="loading">
          <RefreshCw :class="['w-4 h-4', loading ? 'animate-spin' : '']" />
          <span>刷新</span>
        </button>
      </div>
    </header>

    <div v-if="launchObjectCode && launchObjectKey" class="sap-penetrate-tip">
      <ExternalLink class="w-4 h-4 text-amber-600" />
      <span>当前由 SAP 单据打开：<strong>{{ launchObjectCode }} #{{ launchObjectKey }}</strong>，列表已自动限定为该单据。</span>
    </div>

    <!-- 演示快捷发起区 -->
    <div v-if="isDevelopment" class="demo-bar">
      <div class="demo-desc">
        <Send class="w-4 h-4 text-blue-600" />
        <span>快捷发起测试单据 (模拟 SAP 客户端保存单据后发起审批):</span>
      </div>
      <div class="demo-actions">
        <select v-model="demoDocType">
          <option value="CHORDR">型号订单 UDO (CHORDR)</option>
          <option value="CHOQUT">型号报价单 UDO (CHOQUT)</option>
        </select>
        <input v-model="demoDocKey" placeholder="单据Key如1001" style="width: 110px;" />
        <button class="btn btn-primary btn-sm" @click="submitDemoDoc" :disabled="submittingDemo">
          {{ submittingDemo ? '提交中...' : '发起审批申请' }}
        </button>
      </div>
    </div>

    <!-- 主体布局: 左侧待办列表 + 右侧单据详情看板 -->
    <div class="main-body">
      <!-- 左侧待办列表 -->
      <aside class="sidebar-panel card">
        <div class="tab-header">
          <button
            :class="['tab-btn', currentScope === 'pending' ? 'active' : '']"
            @click="currentScope = 'pending'; loadTasks()"
          >
            待我处理 ({{ currentScope === 'pending' ? tasks.length : '' }})
          </button>
          <button
            :class="['tab-btn', currentScope === 'completed' ? 'active' : '']"
            @click="currentScope = 'completed'; loadTasks()"
          >
            我已处理
          </button>
        </div>

        <div class="task-list">
          <div v-if="loading && tasks.length === 0" class="loading-box">
            <RefreshCw class="w-6 h-6 animate-spin text-blue-600" />
            <span>加载任务中...</span>
          </div>

          <div v-else-if="tasks.length === 0" class="empty-box">
            <CheckCircle2 class="w-10 h-10 text-slate-300" />
            <p>暂无相关待办事项</p>
          </div>

          <div
            v-for="t in tasks"
            :key="t.taskId"
            :class="['task-card', activeTask?.taskId === t.taskId ? 'selected' : '']"
            @click="selectTask(t)"
          >
            <div class="task-card-header">
              <span class="obj-code">{{ t.objectCode }}</span>
              <span :class="['badge', t.status === 'Pending' ? 'badge-pending' : 'badge-approved']">
                {{ t.status === 'Pending' ? '待审批' : '已完成' }}
              </span>
            </div>
            <div class="task-title">{{ t.title }}</div>
            <div class="task-meta">
              <span>节点: <strong>{{ t.nodeName }}</strong></span>
              <span>提交人: {{ t.submitter }}</span>
            </div>
            <div class="task-time">
              <Clock class="w-3.5 h-3.5" />
              <span>{{ new Date(t.createdAt).toLocaleString('zh-CN') }}</span>
            </div>
          </div>
        </div>
      </aside>

      <!-- 右侧单据详情与审批看板 -->
      <section class="detail-panel card">
        <div v-if="!activeTask || !taskDetail" class="empty-detail">
          <FileText class="w-12 h-12 text-slate-300" />
          <p>请选择左侧待办任务以查看单据明细与审批轨迹</p>
        </div>

        <div v-else class="detail-content">
          <!-- 单据表头总览 -->
          <div class="doc-header-card">
            <div class="doc-title-row">
              <div class="doc-main-info">
                <h2>{{ taskDetail.instance?.title }}</h2>
                <div class="doc-tags">
                  <span class="tag-item">业务对象: <strong>{{ taskDetail.instance?.objectCode }}</strong></span>
                  <span class="tag-item">单据Key: <strong>{{ taskDetail.instance?.objectKey }}</strong></span>
                  <span class="tag-item">提交人: <strong>{{ taskDetail.instance?.submitterName || taskDetail.instance?.submitterCode }}</strong></span>
                </div>
              </div>

              <!-- 状态徽章 -->
              <div class="doc-status-box">
                <span :class="['badge', 'badge-' + (taskDetail.instance?.status?.toLowerCase() || 'pending')]">
                  {{ taskDetail.instance?.status }}
                </span>
              </div>
            </div>

            <!-- 防篡改 SHA-256 指纹徽章 -->
            <div class="sha-box">
              <ShieldCheck class="w-4 h-4 text-emerald-600" />
              <span class="sha-label">不可变规范化快照签名 (SHA-256):</span>
              <code class="sha-val">{{ taskDetail.snapshot?.dataSha256 }}</code>
            </div>

            <!-- SAP 穿透黄箭头提示栏 (无源码黑盒解决方案) -->
            <div class="sap-penetrate-tip">
              <ExternalLink class="w-4 h-4 text-amber-600" />
              <span>
                <strong>SAP 客户端联动提示：</strong>
                在 SAP 审批工作台或单据窗口中，点击对应单号的<strong>【黄色箭头】</strong>即可直接穿透打开 SAP 原始单据。
              </span>
            </div>
          </div>

          <!-- 单据原始数据与明细行结构 (从快照反序列化渲染) -->
          <div class="section-block">
            <div class="section-title">
              <FileText class="w-4 h-4 text-blue-600" />
              <span>单据核心明细 (Service Layer 快照)</span>
            </div>

            <div class="json-preview-container">
              <pre class="json-preview">{{ JSON.stringify(JSON.parse(taskDetail.snapshot?.rawJson || '{}'), null, 2) }}</pre>
            </div>
          </div>

          <!-- 审批流转时序图 / 审计日志 -->
          <div class="section-block">
            <div class="section-title">
              <Clock class="w-4 h-4 text-blue-600" />
              <span>审批流转轨迹与审计证据链 (不可变追溯)</span>
            </div>

            <div class="timeline">
              <div v-for="log in taskDetail.auditLogs" :key="log.id" class="timeline-item">
                <div class="timeline-dot"></div>
                <div class="timeline-content">
                  <div class="timeline-top">
                    <span class="action-name">{{ log.action }} ({{ log.operatorName || log.operatorCode }})</span>
                    <span class="action-time">{{ new Date(log.actionTime).toLocaleString('zh-CN') }}</span>
                  </div>
                  <p class="action-comment">{{ log.comment || '无备注' }}</p>
                </div>
              </div>
            </div>
          </div>

          <!-- 审批动作控制台 (仅在待办状态呈现) -->
          <div v-if="activeTask.status === 'Pending'" class="action-console">
            <div class="console-title">
              <span>审批决策决定:</span>
            </div>
            <div class="comments-box">
              <textarea
                v-model="decisionComments"
                placeholder="请输入审批处理意见 (选填，默认系统根据决定自动生成)..."
                rows="2"
              ></textarea>
            </div>
            <div class="action-btns">
              <button
                class="btn btn-success"
                :disabled="submittingDecision"
                @click="handleDecision('Approve')"
              >
                <CheckCircle2 class="w-4 h-4" />
                <span>同意 (Approve & 放行)</span>
              </button>

              <button
                class="btn btn-danger"
                :disabled="submittingDecision"
                @click="handleDecision('Reject')"
              >
                <XCircle class="w-4 h-4" />
                <span>拒绝 (Reject & 终止)</span>
              </button>

              <button
                class="btn btn-secondary"
                :disabled="submittingDecision"
                @click="handleDecision('Return')"
              >
                <RefreshCw class="w-4 h-4" />
                <span>退回修改 (Return)</span>
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- 浮动消息提示 Toast -->
    <div v-if="messageToast" :class="['toast', messageToast.type]">
      {{ messageToast.text }}
    </div>
  </div>
</template>

<style scoped>
.workbench-container {
  display: flex;
  flex-direction: column;
  height: 100vh;
  padding: 16px;
  gap: 12px;
}

/* 顶部导航 */
.top-nav {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: #fff;
  padding: 12px 20px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border-color);
  box-shadow: var(--shadow-sm);
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
}

.brand-icon {
  background: linear-gradient(135deg, #2563eb, #1d4ed8);
  padding: 8px;
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
}

.brand-info h1 {
  font-size: 17px;
  font-weight: 700;
  color: #0f172a;
}

.brand-info .sub-text {
  font-size: 12px;
  color: var(--text-secondary);
}

.nav-controls {
  display: flex;
  align-items: center;
  gap: 16px;
}

.company-badge {
  display: flex;
  align-items: center;
  gap: 6px;
  background-color: #f1f5f9;
  padding: 6px 12px;
  border-radius: 9999px;
  font-size: 13px;
}

.indicator {
  width: 8px;
  height: 8px;
  background-color: #10b981;
  border-radius: 50%;
}

.user-selector {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
}

/* 演示栏 */
.demo-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #eff6ff;
  border: 1px dashed #93c5fd;
  border-radius: var(--radius-sm);
  padding: 8px 16px;
}

.demo-desc {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #1e40af;
  font-weight: 500;
}

.demo-actions {
  display: flex;
  gap: 8px;
}

/* 主体布局 */
.main-body {
  display: grid;
  grid-template-columns: 360px 1fr;
  gap: 16px;
  flex: 1;
  min-height: 0;
}

/* 左侧待办 */
.sidebar-panel {
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow: hidden;
}

.tab-header {
  display: flex;
  border-bottom: 1px solid var(--border-color);
  background-color: #f8fafc;
}

.tab-btn {
  flex: 1;
  padding: 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-secondary);
  background: transparent;
  border-bottom: 2px solid transparent;
}

.tab-btn.active {
  color: var(--primary);
  border-bottom-color: var(--primary);
  background-color: #fff;
}

.task-list {
  flex: 1;
  overflow-y: auto;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.task-card {
  background-color: #fff;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 12px;
  cursor: pointer;
  transition: all 0.15s;
}

.task-card:hover {
  border-color: #93c5fd;
  transform: translateY(-1px);
}

.task-card.selected {
  border-color: var(--primary);
  background-color: var(--primary-light);
}

.task-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}

.obj-code {
  font-size: 11px;
  font-weight: 700;
  color: #475569;
  background-color: #e2e8f0;
  padding: 2px 6px;
  border-radius: 4px;
}

.task-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 6px;
}

.task-meta {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 4px;
}

.task-time {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  color: var(--text-muted);
}

/* 右侧详情面板 */
.detail-panel {
  display: flex;
  flex-direction: column;
  overflow-y: auto;
}

.empty-detail {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-muted);
  gap: 12px;
}

.doc-header-card {
  border-bottom: 1px solid var(--border-color);
  padding-bottom: 16px;
  margin-bottom: 16px;
}

.doc-title-row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 10px;
}

.doc-main-info h2 {
  font-size: 18px;
  font-weight: 700;
  margin-bottom: 6px;
}

.doc-tags {
  display: flex;
  gap: 12px;
  font-size: 13px;
  color: var(--text-secondary);
}

.sha-box {
  display: flex;
  align-items: center;
  gap: 6px;
  background-color: #f8fafc;
  border: 1px solid var(--border-color);
  padding: 6px 12px;
  border-radius: var(--radius-sm);
  font-size: 12px;
  margin-top: 10px;
}

.sha-val {
  font-family: monospace;
  color: #047857;
  font-weight: 600;
}

.sap-penetrate-tip {
  display: flex;
  align-items: center;
  gap: 8px;
  background-color: #fffbeb;
  border: 1px solid #fef3c7;
  color: #92400e;
  font-size: 12px;
  padding: 8px 12px;
  border-radius: var(--radius-sm);
  margin-top: 8px;
}

.section-block {
  margin-bottom: 20px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 14px;
  font-weight: 700;
  margin-bottom: 8px;
  color: #334155;
}

.json-preview-container {
  background-color: #0f172a;
  border-radius: var(--radius-sm);
  padding: 12px;
  max-height: 220px;
  overflow-y: auto;
}

.json-preview {
  color: #38bdf8;
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
  line-height: 1.4;
}

/* 时间线 */
.timeline {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-left: 8px;
}

.timeline-item {
  display: flex;
  gap: 12px;
  position: relative;
}

.timeline-dot {
  width: 10px;
  height: 10px;
  background-color: var(--primary);
  border-radius: 50%;
  margin-top: 5px;
}

.timeline-content {
  flex: 1;
  background-color: #f8fafc;
  padding: 8px 12px;
  border-radius: var(--radius-sm);
}

.timeline-top {
  display: flex;
  justify-content: space-between;
  font-size: 13px;
  font-weight: 600;
}

.action-time {
  font-size: 11px;
  color: var(--text-muted);
  font-weight: normal;
}

.action-comment {
  font-size: 12px;
  color: var(--text-secondary);
  margin-top: 4px;
}

/* 审批动作控制台 */
.action-console {
  background-color: #f8fafc;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  padding: 16px;
  margin-top: auto;
}

.console-title {
  font-size: 13px;
  font-weight: 700;
  margin-bottom: 8px;
}

.comments-box textarea {
  width: 100%;
  resize: vertical;
  margin-bottom: 12px;
}

.action-btns {
  display: flex;
  gap: 12px;
}

/* Toast */
.toast {
  position: fixed;
  bottom: 24px;
  right: 24px;
  padding: 12px 20px;
  border-radius: var(--radius-sm);
  color: #fff;
  font-size: 14px;
  font-weight: 500;
  box-shadow: var(--shadow-lg);
  z-index: 1000;
}

.toast.success {
  background-color: var(--success);
}

.toast.error {
  background-color: var(--danger);
}
</style>
