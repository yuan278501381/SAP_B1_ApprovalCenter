import { computed } from 'vue'
import api from '../config/request'
import axios from 'axios'

export function useWorkbench(
  tasks: any,
  activeTask: any,
  taskDetail: any,
  loading: any,
  currentScope: any,
  launchCompanyId: any,
  launchObjectCode: any,
  launchObjectKey: any,
  showToast: any,
  taskSearchQuery: any,
  getObjectTypeName: any,
  submittingDecision: any,
  decisionComments: any,
  loadNotifications: any,
  showShortcutModal: any
) {

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
      const found = tasks.value.find((t: any) => t.taskId === activeTask.value?.taskId)
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
  return tasks.value.filter((t: any) =>
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
  const currIdx = list.findIndex((t: any) => t.taskId === activeTask.value?.taskId)
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



  return {
    filteredTasks,
    loadTasks,
    selectTask,
    handleDecision,
    navigateTask,
    onGlobalKeyDown
  }
}
