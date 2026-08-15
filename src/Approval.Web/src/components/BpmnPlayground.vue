<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, nextTick } from 'vue'
import BpmnModeler from 'bpmn-js/lib/Modeler'
import 'bpmn-js/dist/assets/diagram-js.css'
import 'bpmn-js/dist/assets/bpmn-js.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn-codes.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn-embedded.css'

import LogicFlow from '@logicflow/core'
import {
  BpmnElement,
  BpmnAdapter,
  Menu,
  MiniMap,
  Snapshot,
  DndPanel,
  Control
} from '@logicflow/extension'
import '@logicflow/core/dist/index.css'
import '@logicflow/extension/dist/index.css'

import {
  Play,
  RotateCcw,
  Download,
  Copy,
  ExternalLink,
  CheckCircle,
  FileCode,
  Layers,
  ZoomIn,
  ZoomOut,
  Maximize2,
  Sparkles,
  Server,
  Zap,
  Lightbulb,
  Palette,
  ClipboardList,
  BarChart3,
  Users
} from 'lucide-vue-next'

// 默认标准 SAP B1 型号订单审批 BPMN 2.0 XML
const DEFAULT_BPMN_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn2:definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                   xmlns:bpmn2="http://www.omg.org/spec/BPMN/20100524/MODEL"
                   xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                   xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                   xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                   id="Definitions_CHORDR"
                   targetNamespace="http://bpmn.io/schema/bpmn"
                   xsi:schemaLocation="http://www.omg.org/spec/BPMN/20100524/MODEL BPMN20.xsd">
  <bpmn2:process id="Process_Sap_CHORDR_Approval" name="SAP B1 型号订单多级审批流" isExecutable="true">
    <bpmn2:startEvent id="StartEvent_1" name="销售员提交单据">
      <bpmn2:outgoing>Flow_1</bpmn2:outgoing>
    </bpmn2:startEvent>
    <bpmn2:userTask id="Task_DeptManager" name="部门经理初审">
      <bpmn2:incoming>Flow_1</bpmn2:incoming>
      <bpmn2:outgoing>Flow_2</bpmn2:outgoing>
    </bpmn2:userTask>
    <bpmn2:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="Task_DeptManager" />
    <bpmn2:exclusiveGateway id="Gateway_AmountCheck" name="订单金额 &gt; 50,000?">
      <bpmn2:incoming>Flow_2</bpmn2:incoming>
      <bpmn2:outgoing>Flow_HighAmount</bpmn2:outgoing>
      <bpmn2:outgoing>Flow_NormalAmount</bpmn2:outgoing>
    </bpmn2:exclusiveGateway>
    <bpmn2:sequenceFlow id="Flow_2" sourceRef="Task_DeptManager" targetRef="Gateway_AmountCheck" />
    <bpmn2:userTask id="Task_Director" name="销售总监加签终审">
      <bpmn2:incoming>Flow_HighAmount</bpmn2:incoming>
      <bpmn2:outgoing>Flow_ToFinance1</bpmn2:outgoing>
    </bpmn2:userTask>
    <bpmn2:sequenceFlow id="Flow_HighAmount" name="是 (金额 &gt; 5万)" sourceRef="Gateway_AmountCheck" targetRef="Task_Director" />
    <bpmn2:userTask id="Task_Finance" name="财务合规复核">
      <bpmn2:incoming>Flow_NormalAmount</bpmn2:incoming>
      <bpmn2:incoming>Flow_ToFinance1</bpmn2:incoming>
      <bpmn2:outgoing>Flow_ToEnd</bpmn2:outgoing>
    </bpmn2:userTask>
    <bpmn2:sequenceFlow id="Flow_NormalAmount" name="否 (≤ 5万)" sourceRef="Gateway_AmountCheck" targetRef="Task_Finance" />
    <bpmn2:sequenceFlow id="Flow_ToFinance1" sourceRef="Task_Director" targetRef="Task_Finance" />
    <bpmn2:endEvent id="EndEvent_1" name="审批通过 (SAP 自动放行)">
      <bpmn2:incoming>Flow_ToEnd</bpmn2:incoming>
    </bpmn2:endEvent>
    <bpmn2:sequenceFlow id="Flow_ToEnd" sourceRef="Task_Finance" targetRef="EndEvent_1" />
  </bpmn2:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_Sap_CHORDR_Approval">
      <bpmndi:BPMNShape id="_BPMNShape_StartEvent_2" bpmnElement="StartEvent_1">
        <dc:Bounds x="160" y="192" width="36" height="36" />
        <bpmndi:BPMNLabel>
          <dc:Bounds x="140" y="235" width="77" height="14" />
        </bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="Activity_DeptManager_di" bpmnElement="Task_DeptManager">
        <dc:Bounds x="250" y="170" width="120" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
        <di:waypoint x="196" y="210" />
        <di:waypoint x="250" y="210" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNShape id="Gateway_AmountCheck_di" bpmnElement="Gateway_AmountCheck" isMarkerVisible="true">
        <dc:Bounds x="430" y="185" width="50" height="50" />
        <bpmndi:BPMNLabel>
          <dc:Bounds x="415" y="155" width="81" height="27" />
        </bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_2_di" bpmnElement="Flow_2">
        <di:waypoint x="370" y="210" />
        <di:waypoint x="430" y="210" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNShape id="Activity_Director_di" bpmnElement="Task_Director">
        <dc:Bounds x="540" y="90" width="120" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_HighAmount_di" bpmnElement="Flow_HighAmount">
        <di:waypoint x="455" y="185" />
        <di:waypoint x="455" y="130" />
        <di:waypoint x="540" y="130" />
        <bpmndi:BPMNLabel>
          <dc:Bounds x="460" y="135" width="70" height="14" />
        </bpmndi:BPMNLabel>
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNShape id="Activity_Finance_di" bpmnElement="Task_Finance">
        <dc:Bounds x="720" y="170" width="120" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_NormalAmount_di" bpmnElement="Flow_NormalAmount">
        <di:waypoint x="455" y="235" />
        <di:waypoint x="455" y="280" />
        <di:waypoint x="780" y="280" />
        <di:waypoint x="780" y="250" />
        <bpmndi:BPMNLabel>
          <dc:Bounds x="590" y="260" width="50" height="14" />
        </bpmndi:BPMNLabel>
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_ToFinance1_di" bpmnElement="Flow_ToFinance1">
        <di:waypoint x="660" y="130" />
        <di:waypoint x="780" y="130" />
        <di:waypoint x="780" y="170" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNShape id="Event_End_di" bpmnElement="EndEvent_1">
        <dc:Bounds x="900" y="192" width="36" height="36" />
        <bpmndi:BPMNLabel>
          <dc:Bounds x="878" y="235" width="81" height="27" />
        </bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_ToEnd_di" bpmnElement="Flow_ToEnd">
        <di:waypoint x="840" y="210" />
        <di:waypoint x="900" y="210" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn2:definitions>`

const activeLabTab = ref<'bpmnjs' | 'logicflow' | 'xml' | 'flowable'>('bpmnjs')

// bpmn-js 引用与状态
const bpmnContainerRef = ref<HTMLDivElement | null>(null)
let bpmnModeler: any = null
const currentSelectedElement = ref<any>(null)
const currentXmlContent = ref(DEFAULT_BPMN_XML)
const copySuccess = ref(false)

// 仿真模拟状态
const simActiveStep = ref<number>(0)
const simStepList = ['StartEvent_1', 'Task_DeptManager', 'Gateway_AmountCheck', 'Task_Director', 'Task_Finance', 'EndEvent_1']

// LogicFlow 引用与状态
const lfContainerRef = ref<HTMLDivElement | null>(null)
let lfInstance: any = null

// 初始化 bpmn-js
async function initBpmnJs() {
  if (!bpmnContainerRef.value) return
  if (bpmnModeler) {
    bpmnModeler.destroy()
  }

  bpmnModeler = new BpmnModeler({
    container: bpmnContainerRef.value,
    keyboard: {
      bindTo: window
    }
  })

  // 监听选择事件
  bpmnModeler.on('selection.changed', (e: any) => {
    const selection = e.newSelection
    if (selection && selection.length > 0) {
      const el = selection[0]
      currentSelectedElement.value = {
        id: el.id,
        type: el.type,
        name: el.businessObject?.name || '(无标题)',
        doc: el.businessObject?.documentation?.[0]?.text || '',
        assignee: el.businessObject?.assignee || '未指定'
      }
    } else {
      currentSelectedElement.value = null
    }
  })

  // 监听元素更新以同步 XML
  bpmnModeler.on('commandStack.changed', async () => {
    try {
      const { xml } = await bpmnModeler.saveXML({ format: true })
      if (xml) currentXmlContent.value = xml
    } catch (err) {
      console.error('XML 导出失败', err)
    }
  })

  await loadBpmnXml(currentXmlContent.value)
}

async function loadBpmnXml(xml: string) {
  if (!bpmnModeler) return
  try {
    await bpmnModeler.importXML(xml)
    const canvas = bpmnModeler.get('canvas')
    canvas.zoom('fit-viewport')
  } catch (err) {
    console.error('BPMN XML 导入失败', err)
  }
}

// 初始化 LogicFlow
function initLogicFlow() {
  if (!lfContainerRef.value) return
  if (lfInstance) {
    lfInstance.destroy()
    lfInstance = null
  }

  // 1. 创建 LogicFlow 实例
  lfInstance = new LogicFlow({
    container: lfContainerRef.value,
    grid: {
      size: 15,
      type: 'dot'
    },
    keyboard: {
      enabled: true
    },
    plugins: [BpmnElement, BpmnAdapter, Menu, MiniMap, Snapshot, DndPanel, Control]
  })

  // 2. 设置拖拽物料面板 (内置精美 Base64 矢量图标，100% 跨浏览器兼容)
  const ICON_START = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIzMiIgaGVpZ2h0PSIzMiIgdmlld0JveD0iMCAwIDMyIDMyIj48Y2lyY2xlIGN4PSIxNiIgY3k9IjE2IiByPSIxMiIgZmlsbD0iI2RjZmNlNyIgc3Ryb2tlPSIjMTZhMzRhIiBzdHJva2Utd2lkdGg9IjIuNSIvPjwvc3ZnPg=="
  const ICON_TASK = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIzMiIgaGVpZ2h0PSIzMiIgdmlld0JveD0iMCAwIDMyIDMyIj48cmVjdCB4PSIyIiB5PSI2IiB3aWR0aD0iMjgiIGhlaWdodD0iMjAiIHJ4PSI0IiBmaWxsPSIjZTBmMmZlIiBzdHJva2U9IiMwMjg0YzciIHN0cm9rZS13aWR0aD0iMiIvPjxjaXJjbGUgY3g9IjE2IiBjeT0iMTMiIHI9IjMiIGZpbGw9IiMwMjg0YzciLz48cGF0aCBkPSJNMTAgMjJjMC0yLjUgMi41LTMuNSA2LTMuNXM2IDEgNiAzLjUiIHN0cm9rZT0iIzAyODRjNyIgc3Ryb2tlLXdpZHRoPSIxLjUiIGZpbGw9Im5vbmUiLz48L3N2Zz4="
  const ICON_GATEWAY = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIzMiIgaGVpZ2h0PSIzMiIgdmlld0JveD0iMCAwIDMyIDMyIj48cG9seWdvbiBwb2ludHM9IjE2LDMgMjksMTYgMTYsMjkgMywxNiIgZmlsbD0iI2ZlZjNjNyIgc3Ryb2tlPSIjZDk3NzA2IiBzdHJva2Utd2lkdGg9IjIiLz48cGF0aCBkPSJNMTEgMTEgTDIxIDIxIE0yMSAxMSBMMTEgMjEiIHN0cm9rZT0iI2Q5NzcwNiIgc3Ryb2tlLXdpZHRoPSIyIi8+PC9zdmc+"
  const ICON_END = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIzMiIgaGVpZ2h0PSIzMiIgdmlld0JveD0iMCAwIDMyIDMyIj48Y2lyY2xlIGN4PSIxNiIgY3k9IjE2IiByPSIxMSIgZmlsbD0iI2ZlZTJlMiIgc3Ryb2tlPSIjZGMyNjI2IiBzdHJva2Utd2lkdGg9IjQiLz48L3N2Zz4="

  lfInstance.extension.dndPanel.setPatternItems([
    {
      type: 'bpmn:startEvent',
      text: '开始',
      label: '开始事件',
      icon: ICON_START
    },
    {
      type: 'bpmn:userTask',
      text: '用户审批任务',
      label: '用户任务',
      icon: ICON_TASK
    },
    {
      type: 'bpmn:exclusiveGateway',
      text: '条件判断',
      label: '排他网关',
      icon: ICON_GATEWAY
    },
    {
      type: 'bpmn:endEvent',
      text: '结束',
      label: '结束事件',
      icon: ICON_END
    }
  ])

  // 3. 渲染经典 SAP B1 型号订单审批流节点与连线
  lfInstance.render({
    nodes: [
      {
        id: 'start_1',
        type: 'bpmn:startEvent',
        x: 180,
        y: 220,
        text: '销售员提交单据'
      },
      {
        id: 'task_1',
        type: 'bpmn:userTask',
        x: 340,
        y: 220,
        text: '部门经理初审'
      },
      {
        id: 'gw_1',
        type: 'bpmn:exclusiveGateway',
        x: 500,
        y: 220,
        text: '金额 > 50,000?'
      },
      {
        id: 'task_2',
        type: 'bpmn:userTask',
        x: 680,
        y: 130,
        text: '销售总监加签终审'
      },
      {
        id: 'task_3',
        type: 'bpmn:userTask',
        x: 680,
        y: 310,
        text: '财务合规复核'
      },
      {
        id: 'end_1',
        type: 'bpmn:endEvent',
        x: 860,
        y: 220,
        text: '审批通过自动放行'
      }
    ],
    edges: [
      {
        id: 'edge_1',
        sourceNodeId: 'start_1',
        targetNodeId: 'task_1',
        type: 'bpmn:sequenceFlow'
      },
      {
        id: 'edge_2',
        sourceNodeId: 'task_1',
        targetNodeId: 'gw_1',
        type: 'bpmn:sequenceFlow'
      },
      {
        id: 'edge_3',
        sourceNodeId: 'gw_1',
        targetNodeId: 'task_2',
        type: 'bpmn:sequenceFlow',
        text: '是 (>5万)'
      },
      {
        id: 'edge_4',
        sourceNodeId: 'gw_1',
        targetNodeId: 'task_3',
        type: 'bpmn:sequenceFlow',
        text: '否 (≤5万)'
      },
      {
        id: 'edge_5',
        sourceNodeId: 'task_2',
        targetNodeId: 'task_3',
        type: 'bpmn:sequenceFlow'
      },
      {
        id: 'edge_6',
        sourceNodeId: 'task_3',
        targetNodeId: 'end_1',
        type: 'bpmn:sequenceFlow'
      }
    ]
  })

  // 监听图变动更新 XML
  lfInstance.on('history:change', () => {
    try {
      const data = lfInstance.getGraphRawData()
      if (typeof data === 'string' && data.includes('<?xml')) {
        currentXmlContent.value = data
      }
    } catch {}
  })
}

// 模拟流转步骤动画
function stepSimulation() {
  if (!bpmnModeler) return
  const canvas = bpmnModeler.get('canvas')
  const overlays = bpmnModeler.get('overlays')

  // 清除旧高亮
  simStepList.forEach(id => {
    try {
      canvas.removeMarker(id, 'highlight-node')
      canvas.removeMarker(id, 'highlight-active')
    } catch {}
  })
  overlays.clear()

  simActiveStep.value = (simActiveStep.value + 1) % (simStepList.length + 1)

  if (simActiveStep.value === 0) {
    return
  }

  // 高亮已走过的节点
  for (let i = 0; i < simActiveStep.value - 1; i++) {
    const id = simStepList[i]
    canvas.addMarker(id, 'highlight-node')
  }

  // 高亮当前活跃节点
  const activeId = simStepList[simActiveStep.value - 1]
  canvas.addMarker(activeId, 'highlight-active')

  // 添加动态 Badge
  overlays.add(activeId, {
    position: {
      top: -10,
      right: 0
    },
    html: `<div class="active-token-badge">审批处理中 (Token)</div>`
  })
}

// 重置模拟
function resetSimulation() {
  simActiveStep.value = 0
  if (bpmnModeler) {
    const canvas = bpmnModeler.get('canvas')
    const overlays = bpmnModeler.get('overlays')
    simStepList.forEach(id => {
      try {
        canvas.removeMarker(id, 'highlight-node')
        canvas.removeMarker(id, 'highlight-active')
      } catch {}
    })
    overlays.clear()
  }
}

// 导出与下载
async function downloadXml() {
  let xml = currentXmlContent.value
  if (bpmnModeler && activeLabTab.value === 'bpmnjs') {
    const res = await bpmnModeler.saveXML({ format: true })
    xml = res.xml || xml
  }
  const blob = new Blob([xml], { type: 'application/xml' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'SAP_B1_CHORDR_Workflow.bpmn20.xml'
  a.click()
  URL.revokeObjectURL(url)
}

function copyXml() {
  navigator.clipboard.writeText(currentXmlContent.value)
  copySuccess.value = true
  setTimeout(() => {
    copySuccess.value = false
  }, 2000)
}

// 缩放控制
function zoomIn() {
  if (activeLabTab.value === 'bpmnjs' && bpmnModeler) {
    bpmnModeler.get('zoomScroll').stepZoom(1)
  } else if (activeLabTab.value === 'logicflow' && lfInstance) {
    lfInstance.zoom(true)
  }
}

function zoomOut() {
  if (activeLabTab.value === 'bpmnjs' && bpmnModeler) {
    bpmnModeler.get('zoomScroll').stepZoom(-1)
  } else if (activeLabTab.value === 'logicflow' && lfInstance) {
    lfInstance.zoom(false)
  }
}

function fitView() {
  if (activeLabTab.value === 'bpmnjs' && bpmnModeler) {
    bpmnModeler.get('canvas').zoom('fit-viewport')
  } else if (activeLabTab.value === 'logicflow' && lfInstance) {
    lfInstance.resetZoom()
    lfInstance.resetTranslate()
  }
}

onMounted(async () => {
  await nextTick()
  await initBpmnJs()
})

onBeforeUnmount(() => {
  if (bpmnModeler) bpmnModeler.destroy()
  if (lfInstance) lfInstance.destroy()
})

async function switchTab(tab: 'bpmnjs' | 'logicflow' | 'xml' | 'flowable') {
  activeLabTab.value = tab
  await nextTick()
  if (tab === 'bpmnjs') {
    await initBpmnJs()
  } else if (tab === 'logicflow') {
    setTimeout(() => {
      initLogicFlow()
    }, 60)
  }
}
</script>

<template>
  <div class="bpmn-lab-root">
    <!-- 顶部工具栏 -->
    <header class="lab-header">
      <div class="header-left">
        <div class="brand-title">
          <Sparkles class="w-5 h-5 text-sky-400" />
          <span>BPMN 2.0 工业级开源生态实验室</span>
          <span class="sub-badge">v2026.08</span>
        </div>

        <div class="lab-tabs">
          <button
            :class="['lab-tab-btn', activeLabTab === 'bpmnjs' ? 'active' : '']"
            @click="switchTab('bpmnjs')"
          >
            <Layers class="w-4 h-4" />
            <span>1. bpmn-js (Camunda/工业标准)</span>
          </button>

          <button
            :class="['lab-tab-btn', activeLabTab === 'logicflow' ? 'active' : '']"
            @click="switchTab('logicflow')"
          >
            <Zap class="w-4 h-4" />
            <span>2. Didi LogicFlow (滴滴/中国式审批)</span>
          </button>

          <button
            :class="['lab-tab-btn', activeLabTab === 'xml' ? 'active' : '']"
            @click="switchTab('xml')"
          >
            <FileCode class="w-4 h-4" />
            <span>3. 标准 XML 结构透视</span>
          </button>

          <button
            :class="['lab-tab-btn', activeLabTab === 'flowable' ? 'active' : '']"
            @click="switchTab('flowable')"
          >
            <Server class="w-4 h-4 text-emerald-400" />
            <span>4. Flowable 引擎实战管理台</span>
            <span class="pulse-dot"></span>
          </button>
        </div>
      </div>

      <div class="header-right">
        <!-- 仿真流转按钮 (仅在 bpmn-js 下可用) -->
        <template v-if="activeLabTab === 'bpmnjs'">
          <button class="action-btn sim-btn" @click="stepSimulation">
            <Play class="w-4 h-4" />
            <span>模拟流转 (第 {{ simActiveStep }}/{{ simStepList.length }} 步)</span>
          </button>
          <button class="action-btn" title="重置模拟" @click="resetSimulation">
            <RotateCcw class="w-4 h-4" />
          </button>
        </template>

        <!-- 通用缩放按钮 -->
        <button class="action-btn" title="放大" @click="zoomIn">
          <ZoomIn class="w-4 h-4" />
        </button>
        <button class="action-btn" title="缩小" @click="zoomOut">
          <ZoomOut class="w-4 h-4" />
        </button>
        <button class="action-btn" title="自适应视图" @click="fitView">
          <Maximize2 class="w-4 h-4" />
        </button>

        <div class="divider-v"></div>

        <button class="action-btn" @click="copyXml">
          <Copy class="w-4 h-4" />
          <span>{{ copySuccess ? '已复制 XML!' : '复制 XML' }}</span>
        </button>

        <button class="action-btn primary-btn" @click="downloadXml">
          <Download class="w-4 h-4" />
          <span>导出 .bpmn 文件</span>
        </button>
      </div>
    </header>

    <!-- 主展示区 -->
    <main class="lab-main-area">
      <!-- 视图 1: bpmn-js 官方设计器 -->
      <div v-show="activeLabTab === 'bpmnjs'" class="view-pane bpmn-view-pane">
        <div ref="bpmnContainerRef" class="bpmn-canvas-wrapper"></div>

        <!-- 右侧轻量属性检查器 -->
        <aside class="bpmn-inspector">
          <div class="inspector-header">
            <span class="title">BPMN 节点属性面板</span>
            <span v-if="currentSelectedElement" class="node-type-badge">{{ currentSelectedElement.type.replace('bpmn:', '') }}</span>
          </div>

          <div v-if="currentSelectedElement" class="inspector-body">
            <div class="prop-item">
              <label>节点 ID</label>
              <input type="text" :value="currentSelectedElement.id" readonly class="prop-input" />
            </div>
            <div class="prop-item">
              <label>节点名称 (Name)</label>
              <input type="text" :value="currentSelectedElement.name" readonly class="prop-input" />
            </div>
            <div class="prop-item">
              <label>指定审批人 / 角色</label>
              <input type="text" :value="currentSelectedElement.assignee" readonly class="prop-input" />
            </div>
            <div class="prop-item">
              <label>BPMN 2.0 节点定义类型</label>
              <div class="type-desc">{{ currentSelectedElement.type }}</div>
            </div>

            <div class="tip-box">
              <Info class="w-4 h-4 text-sky-400 shrink-0" />
              <span><b>体验提示</b>：您可以在左侧面板拖出新任务、排他网关或事件，点击连线并在画布上自由排版。点击顶部【模拟流转】可直观查看 Token 推进动效！</span>
            </div>
          </div>
          <div v-else class="empty-inspector">
            <Layers class="w-8 h-8 text-slate-500 mb-2" />
            <p>在左侧画布上点击任意任务、网关或事件节点查看 BPMN 2.0 属性详情。</p>
          </div>
        </aside>
      </div>

      <!-- 视图 2: Didi LogicFlow 设计器 -->
      <div v-show="activeLabTab === 'logicflow'" class="view-pane lf-view-pane">
        <div class="lf-top-hint">
          <Palette class="w-4 h-4 text-purple-400 shrink-0" />
          <span><b>LogicFlow BPMN 模式</b>：滴滴出品，针对中国式审批的易用拖拽设计器。支持对齐吸附线、缩略图、快捷菜单与紧凑卡片。</span>
        </div>
        <div ref="lfContainerRef" class="lf-canvas-wrapper"></div>
      </div>

      <!-- 视图 3: BPMN 2.0 XML 结构透视 -->
      <div v-show="activeLabTab === 'xml'" class="view-pane xml-view-pane">
        <div class="xml-meta-bar">
          <div class="meta-info">
            <FileCode class="w-5 h-5 text-sky-400" />
            <span>符合 <b>OMG BPMN 2.0 国际规范</b> 的标准 XML 描述文档（已包含 BPMNDiagram 坐标与拓扑数据）</span>
          </div>
          <button class="action-btn" @click="copyXml">
            <Copy class="w-4 h-4" />
            <span>{{ copySuccess ? '已复制！' : '一键复制' }}</span>
          </button>
        </div>
        <pre class="xml-code-block"><code>{{ currentXmlContent }}</code></pre>
      </div>

      <!-- 视图 4: Flowable 引擎实战管理台 -->
      <div v-show="activeLabTab === 'flowable'" class="view-pane flowable-view-pane">
        <div class="flowable-dashboard">
          <div class="flowable-hero-card">
            <div class="hero-header">
              <div class="server-badge">
                <span class="status-indicator live"></span>
                <span>Flowable UI 6.8.0 独立服务已就绪</span>
              </div>
              <a href="http://localhost:8088/flowable-ui" target="_blank" class="launch-btn">
                <span>打开 Flowable 管理控制台</span>
                <ExternalLink class="w-4 h-4" />
              </a>
            </div>

            <p class="hero-desc">
              Flowable 是目前企业级市场占有率第一的 BPMN 2.0 / CMMN / DMN 工作流调度引擎。在本地已为您拉起独立的完整应用套件。
            </p>

            <div class="creds-bar">
              <span class="cred-label">默认管理员账号:</span> <code class="cred-code">admin</code>
              <span class="cred-label ml-4">默认登录密码:</span> <code class="cred-code">test</code>
              <span class="cred-label ml-4">服务端口:</span> <code class="cred-code">:8088</code>
            </div>
          </div>

          <div class="flowable-apps-grid">
            <a href="http://localhost:8088/flowable-ui/#/processes" target="_blank" class="app-card">
              <div class="app-icon modeler"><Palette class="w-6 h-6 text-purple-400" /></div>
              <div class="app-info">
                <h3>Flowable Modeler (流程设计器)</h3>
                <p>在线绘制 BPMN 流程模型，可直接导入刚才导出的 <code>.bpmn</code> 文件并一键发布部署。</p>
              </div>
            </a>

            <a href="http://localhost:8088/flowable-ui/#/tasks" target="_blank" class="app-card">
              <div class="app-icon task"><ClipboardList class="w-6 h-6 text-blue-400" /></div>
              <div class="app-info">
                <h3>Flowable Task (任务审批中心)</h3>
                <p>模拟发起流程实例、认领待办、填写审批意见并完成审批流转（支持会签/转办）。</p>
              </div>
            </a>

            <a href="http://localhost:8088/flowable-ui/#/admin" target="_blank" class="app-card">
              <div class="app-icon admin"><BarChart3 class="w-6 h-6 text-emerald-400" /></div>
              <div class="app-info">
                <h3>Flowable Admin (运行监控与运维)</h3>
                <p>实时查看执行中的流程实例（Execution）、挂起实例、死信任务排查与历史审计链路。</p>
              </div>
            </a>

            <a href="http://localhost:8088/flowable-ui/#/idm" target="_blank" class="app-card">
              <div class="app-icon idm"><Users class="w-6 h-6 text-amber-400" /></div>
              <div class="app-info">
                <h3>Flowable IDM (用户与权限管理)</h3>
                <p>配置用户、用户组（候选组）、审批权限与系统集成密钥。</p>
              </div>
            </a>
          </div>

          <!-- 对接架构说明卡片 -->
          <div class="arch-card">
            <div class="arch-title-row">
              <Lightbulb class="w-4 h-4 text-amber-400 mr-2" />
              <h4>与 .NET 8 / SAP B1 审批中心集成建议</h4>
            </div>
            <div class="arch-points">
              <div class="point-item">
                <CheckCircle class="w-4 h-4 text-emerald-400 mt-1" />
                <div>
                  <strong>模式 A：外部 REST / 消息中台模式</strong>
                  <p>使用 .NET 8 WebAPI 调用 Flowable REST API (<code>/process-api/runtime/process-instances</code>)，Flowable 负责状态流转，Outbox 负责可靠回写 SAP。</p>
                </div>
              </div>
              <div class="point-item">
                <CheckCircle class="w-4 h-4 text-sky-400 mt-1" />
                <div>
                  <strong>模式 B：前端 bpmn-js 建模 + .NET 8 轻量状态机</strong>
                  <p>使用当前前端的 <code>bpmn-js</code> 设计器导出流程定义，在 .NET 8 中解析节点流转规则，免去部署独立 Java 容器的运维成本。</p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </main>
  </div>
</template>

<style scoped>
.bpmn-lab-root {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: #0f172a;
  color: #f1f5f9;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
}

/* 顶部导航与工具条 */
.lab-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 18px;
  background: #1e293b;
  border-bottom: 1px solid #334155;
  box-shadow: 0 2px 6px rgba(0,0,0,0.2);
  z-index: 20;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 20px;
}

.brand-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  font-weight: 700;
  color: #f8fafc;
}

.sub-badge {
  font-size: 10px;
  background: #0284c7;
  color: #fff;
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 600;
}

.lab-tabs {
  display: flex;
  background: #0f172a;
  padding: 3px;
  border-radius: 8px;
  gap: 4px;
}

.lab-tab-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 14px;
  background: transparent;
  border: none;
  border-radius: 6px;
  color: #94a3b8;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
  position: relative;
}

.lab-tab-btn:hover {
  color: #f8fafc;
  background: rgba(255,255,255,0.05);
}

.lab-tab-btn.active {
  color: #38bdf8;
  background: #1e293b;
  box-shadow: 0 1px 3px rgba(0,0,0,0.3);
}

.pulse-dot {
  width: 6px;
  height: 6px;
  background: #10b981;
  border-radius: 50%;
  box-shadow: 0 0 6px #10b981;
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0% { transform: scale(0.9); opacity: 0.8; }
  50% { transform: scale(1.3); opacity: 1; }
  100% { transform: scale(0.9); opacity: 0.8; }
}

.header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.action-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  background: #334155;
  border: 1px solid #475569;
  border-radius: 6px;
  color: #f1f5f9;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s ease;
}

.action-btn:hover {
  background: #475569;
  border-color: #64748b;
}

.action-btn.sim-btn {
  background: #0369a1;
  border-color: #0284c7;
  color: #fff;
}
.action-btn.sim-btn:hover {
  background: #0284c7;
}

.action-btn.primary-btn {
  background: #2563eb;
  border-color: #3b82f6;
  color: #fff;
}
.action-btn.primary-btn:hover {
  background: #1d4ed8;
}

.divider-v {
  width: 1px;
  height: 18px;
  background: #475569;
  margin: 0 4px;
}

/* 主展示区域 */
.lab-main-area {
  flex: 1;
  min-height: 0;
  position: relative;
  overflow: hidden;
}

.view-pane {
  width: 100%;
  height: 100%;
  display: flex;
}

/* bpmn-js 视图样式 */
.bpmn-view-pane {
  background: #ffffff;
}

.bpmn-canvas-wrapper {
  flex: 1;
  height: 100%;
  background: #f8fafc;
}

.bpmn-inspector {
  width: 320px;
  height: 100%;
  background: #1e293b;
  border-left: 1px solid #334155;
  display: flex;
  flex-direction: column;
  color: #f8fafc;
}

.inspector-header {
  padding: 14px 16px;
  border-bottom: 1px solid #334155;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.inspector-header .title {
  font-size: 13px;
  font-weight: 700;
}

.node-type-badge {
  font-size: 11px;
  background: #3b82f6;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 600;
}

.inspector-body {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  overflow-y: auto;
}

.prop-item {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.prop-item label {
  font-size: 11px;
  font-weight: 600;
  color: #94a3b8;
  text-transform: uppercase;
}

.prop-input {
  background: #0f172a;
  border: 1px solid #334155;
  border-radius: 6px;
  padding: 8px 10px;
  color: #f8fafc;
  font-size: 12px;
  outline: none;
}

.type-desc {
  font-size: 11px;
  color: #38bdf8;
  background: #0f172a;
  padding: 6px 10px;
  border-radius: 6px;
  border: 1px solid #1e293b;
  font-family: monospace;
}

.tip-box {
  background: rgba(56, 189, 248, 0.08);
  border: 1px solid rgba(56, 189, 248, 0.2);
  border-radius: 8px;
  padding: 12px;
  font-size: 12px;
  color: #bae6fd;
  line-height: 1.5;
}

.empty-inspector {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px;
  text-align: center;
  color: #64748b;
  font-size: 12px;
}

/* LogicFlow 视图样式 */
.lf-view-pane {
  flex-direction: column;
  background: #ffffff;
}

.lf-top-hint {
  padding: 8px 16px;
  background: #f1f5f9;
  border-bottom: 1px solid #e2e8f8;
  color: #334155;
  font-size: 12px;
}

.lf-canvas-wrapper {
  flex: 1;
  height: 100%;
}

/* XML 视图样式 */
.xml-view-pane {
  flex-direction: column;
  background: #0b1120;
  padding: 16px;
  gap: 12px;
}

.xml-meta-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  background: #1e293b;
  border-radius: 8px;
  border: 1px solid #334155;
}

.meta-info {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  color: #e2e8f0;
}

.xml-code-block {
  flex: 1;
  margin: 0;
  padding: 16px;
  background: #020617;
  border: 1px solid #1e293b;
  border-radius: 8px;
  overflow: auto;
  font-family: 'Consolas', 'Fira Code', Monaco, monospace;
  font-size: 12px;
  line-height: 1.6;
  color: #38bdf8;
}

/* Flowable 管理视图样式 */
.flowable-view-pane {
  background: #0f172a;
  overflow-y: auto;
  padding: 24px;
}

.flowable-dashboard {
  max-width: 1100px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 20px;
  width: 100%;
}

.flowable-hero-card {
  background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%);
  border: 1px solid #334155;
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 4px 12px rgba(0,0,0,0.3);
}

.hero-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.server-badge {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 16px;
  font-weight: 700;
  color: #f8fafc;
}

.status-indicator.live {
  width: 10px;
  height: 10px;
  background: #10b981;
  border-radius: 50%;
  box-shadow: 0 0 10px #10b981;
}

.launch-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  background: #10b981;
  color: #fff;
  text-decoration: none;
  padding: 8px 16px;
  border-radius: 8px;
  font-size: 13px;
  font-weight: 600;
  transition: all 0.2s ease;
}

.launch-btn:hover {
  background: #059669;
  transform: translateY(-1px);
}

.hero-desc {
  font-size: 13px;
  color: #94a3b8;
  line-height: 1.6;
  margin-bottom: 16px;
}

.creds-bar {
  display: flex;
  align-items: center;
  background: #090e17;
  padding: 10px 16px;
  border-radius: 8px;
  border: 1px solid #1e293b;
  font-size: 12px;
}

.cred-label {
  color: #64748b;
  font-weight: 600;
}

.cred-code {
  background: #1e293b;
  color: #38bdf8;
  padding: 2px 8px;
  border-radius: 4px;
  font-family: monospace;
  margin-left: 6px;
}

.flowable-apps-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 16px;
}

.app-card {
  display: flex;
  gap: 16px;
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 10px;
  padding: 18px;
  text-decoration: none;
  color: inherit;
  transition: all 0.2s ease;
}

.app-card:hover {
  border-color: #38bdf8;
  transform: translateY(-2px);
  background: #243248;
}

.app-icon {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 22px;
  background: #0f172a;
}

.app-info h3 {
  margin: 0 0 6px 0;
  font-size: 14px;
  font-weight: 700;
  color: #f8fafc;
}

.app-info p {
  margin: 0;
  font-size: 12px;
  color: #94a3b8;
  line-height: 1.5;
}

.arch-card {
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 10px;
  padding: 20px;
}

.arch-card h4 {
  margin: 0 0 14px 0;
  font-size: 14px;
  font-weight: 700;
  color: #f8fafc;
}

.arch-points {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.point-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  font-size: 13px;
}

.point-item strong {
  color: #f1f5f9;
}

.point-item p {
  margin: 4px 0 0 0;
  font-size: 12px;
  color: #94a3b8;
}
</style>

<!-- 全局覆盖 bpmn-js 节点高亮样式 -->
<style>
.highlight-node:not(.djs-connection) .djs-visual > :nth-child(1) {
  stroke: #10b981 !important;
  fill: rgba(16, 185, 129, 0.12) !important;
  stroke-width: 2.5px !important;
}

.highlight-active:not(.djs-connection) .djs-visual > :nth-child(1) {
  stroke: #0ea5e9 !important;
  fill: rgba(14, 165, 233, 0.22) !important;
  stroke-width: 3px !important;
  filter: drop-shadow(0 0 8px rgba(14, 165, 233, 0.6));
}

.active-token-badge {
  background: #0284c7;
  color: white;
  font-size: 10px;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 12px;
  white-space: nowrap;
  box-shadow: 0 2px 6px rgba(0,0,0,0.3);
  animation: bounce 1.2s infinite ease-in-out;
}

@keyframes bounce {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-4px); }
}

/* ===================================================
   LogicFlow Dnd 侧边物料拖拽面板与全局控件企业级优化
   =================================================== */
.lf-dndpanel {
  display: flex !important;
  flex-direction: column !important;
  gap: 10px !important;
  background: #ffffff !important;
  padding: 12px 10px !important;
  border-radius: 10px !important;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12), 0 2px 6px rgba(0, 0, 0, 0.04) !important;
  border: 1px solid #e2e8f0 !important;
  top: 70px !important;
  left: 20px !important;
  z-index: 100 !important;
  min-width: 84px !important;
}

.lf-dnd-item {
  display: flex !important;
  flex-direction: column !important;
  align-items: center !important;
  justify-content: center !important;
  width: 76px !important;
  height: 66px !important;
  background: #f8fafc !important;
  border: 1px solid #e2e8f0 !important;
  border-radius: 8px !important;
  cursor: grab !important;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1) !important;
  margin: 0 !important;
  padding: 6px 4px !important;
  box-sizing: border-box !important;
}

.lf-dnd-item:hover {
  background: #f0f9ff !important;
  border-color: #0ea5e9 !important;
  transform: translateY(-2px) !important;
  box-shadow: 0 4px 12px rgba(14, 165, 233, 0.18) !important;
}

.lf-dnd-shape {
  width: 32px !important;
  height: 32px !important;
  background-size: contain !important;
  background-repeat: no-repeat !important;
  background-position: center !important;
  margin-bottom: 4px !important;
}

.lf-dnd-text {
  font-size: 11px !important;
  font-weight: 600 !important;
  color: #1e293b !important;
  text-align: center !important;
  white-space: nowrap !important;
  line-height: 1.2 !important;
  user-select: none !important;
}

/* 控制按钮栏与小地图美化 */
.lf-control {
  border-radius: 8px !important;
  overflow: hidden !important;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.1) !important;
  border: 1px solid #e2e8f0 !important;
  background: #ffffff !important;
  top: 70px !important;
  right: 20px !important;
  z-index: 100 !important;
}

.lf-mini-map {
  border-radius: 10px !important;
  overflow: hidden !important;
  box-shadow: 0 6px 20px rgba(0, 0, 0, 0.12) !important;
  border: 1px solid #cbd5e1 !important;
  background: #ffffff !important;
  bottom: 20px !important;
  right: 20px !important;
  z-index: 100 !important;
}
</style>
