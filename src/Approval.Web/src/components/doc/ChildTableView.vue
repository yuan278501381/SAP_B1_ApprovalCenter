<template>
  <div class="collection-table-box">
    <div class="table-scroll-container">
      <table class="modern-grid-table" :class="['density-' + tableDensity]">
        <thead>
          <tr>
            <th class="th-seq-col">#</th>
            <th v-for="col in collection.visibleColumns" :key="col" :title="col">
              <div class="th-label-wrap">
                <span>{{ collection.columnLabels[col] || col }}</span>
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(r, rowIdx) in collection.processedRows" :key="r.rIdx" class="grid-row">
            <td class="td-seq-col font-mono">{{ Number(rowIdx) + 1 }}</td>
            <td
              v-for="col in collection.visibleColumns"
              :key="col"
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
            <td :colspan="collection.visibleColumns.length + 1" class="text-center py-8 text-slate-400">
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
