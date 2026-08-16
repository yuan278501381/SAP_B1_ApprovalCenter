<template>
  <div class="collection-table-box">
    <div class="table-scroll-container custom-scrollbar">
      <table class="modern-grid-table" :class="['density-' + tableDensity]">
        <thead>
          <tr>
            <th class="th-seq-col">#</th>
            <th
              v-for="col in collection.visibleColumns"
              :key="col"
              :title="col"
              class="th-cell"
            >
              <div class="th-label-wrap">
                <span class="th-title">{{ collection.columnLabels[col] || col }}</span>
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(r, rowIdx) in collection.processedRows"
            :key="r.rIdx"
            class="grid-row"
          >
            <td class="td-seq-col font-mono">{{ Number(rowIdx) + 1 }}</td>
            <td
              v-for="col in collection.visibleColumns"
              :key="col"
              class="td-cell"
              :class="[
                r.cells[col]?.isNum ? 'align-right font-mono' : '',
                r.cells[col]?.isItemCode ? 'font-mono font-bold text-blue-700' : '',
                r.cells[col]?.isClosed ? 'status-cell-closed' : ''
              ]"
            >
              <span v-if="r.cells[col]?.isTranslated" class="cell-translated-badge">
                {{ r.cells[col]?.display }}
              </span>
              <span v-else>
                {{ r.cells[col]?.display }}
              </span>
            </td>
          </tr>
          <tr v-if="collection.processedRows.length === 0" class="empty-table-row">
            <td :colspan="collection.visibleColumns.length + 1" class="empty-cell">
              未匹配到任何明细行数据
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts">
const props = defineProps<{
  collection: any
  tableDensity: string
}>()
</script>

<style scoped>
.collection-table-box {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  width: 100%;
  height: 100%;
  background: #ffffff;
}

.table-scroll-container {
  flex: 1;
  min-height: 0;
  overflow: auto;
  position: relative;
  border-radius: 4px;
}

/* 现代企业级数据表格设计 */
.modern-grid-table {
  width: max-content;
  min-width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: var(--font-size-sm, 12px);
  text-align: left;
  line-height: 1.4;
}

/* Sticky 固定表头 */
.modern-grid-table thead {
  position: sticky;
  top: 0;
  z-index: 10;
}

.modern-grid-table thead th {
  background: #f8fafc;
  color: #334155;
  font-weight: 600;
  font-size: var(--font-size-xs, 11.5px);
  padding: 8px 12px;
  border-bottom: 2px solid #cbd5e1;
  border-right: 1px solid #e2e8f0;
  white-space: nowrap;
  user-select: none;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.th-label-wrap {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.th-title {
  color: #1e293b;
  font-weight: 600;
}

/* 序号列固定居中 */
.th-seq-col, .td-seq-col {
  width: 44px;
  min-width: 44px;
  max-width: 44px;
  text-align: center;
  color: #64748b;
  background-color: #f8fafc;
  border-right: 1px solid #e2e8f0 !important;
}

/* 表格数据行与交互 */
.grid-row td {
  border-bottom: 1px solid #f1f5f9;
  border-right: 1px solid #f8fafc;
  padding: 6px 12px;
  color: #1e293b;
  font-size: var(--font-size-sm, 12px);
  white-space: nowrap;
  transition: background-color 0.1s ease;
}

.grid-row:nth-child(even) td {
  background-color: #fafbfc;
}

.grid-row:hover td {
  background-color: #eff6ff !important;
}

/* 密度控制 */
.modern-grid-table.density-compact thead th {
  padding: 5px 8px;
  font-size: 11px;
}
.modern-grid-table.density-compact td {
  padding: 3px 8px;
  font-size: 11px;
}

.modern-grid-table.density-normal thead th {
  padding: 7px 12px;
  font-size: 11.5px;
}
.modern-grid-table.density-normal td {
  padding: 6px 12px;
  font-size: 12px;
}

.modern-grid-table.density-comfortable thead th {
  padding: 10px 14px;
  font-size: 12px;
}
.modern-grid-table.density-comfortable td {
  padding: 9px 14px;
  font-size: 12.5px;
}

/* 数字靠右对齐与等宽字体 */
.align-right {
  text-align: right !important;
}

.font-mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  font-variant-numeric: tabular-nums;
}

/* 特殊单元格样式 */
.cell-translated-badge {
  color: #0369a1;
  font-weight: 500;
}

.status-cell-closed {
  background: #fef3c7 !important;
  color: #92400e !important;
  font-weight: 600;
  border-radius: 2px;
  padding: 2px 4px;
}

.empty-cell {
  text-align: center;
  padding: 32px 16px;
  color: #94a3b8;
  font-size: 13px;
}

/* 极细轻量滚动条 */
.custom-scrollbar::-webkit-scrollbar {
  width: 6px;
  height: 6px;
}

.custom-scrollbar::-webkit-scrollbar-track {
  background: #f1f5f9;
  border-radius: 3px;
}

.custom-scrollbar::-webkit-scrollbar-thumb {
  background: #cbd5e1;
  border-radius: 3px;
}

.custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background: #94a3b8;
}
</style>
