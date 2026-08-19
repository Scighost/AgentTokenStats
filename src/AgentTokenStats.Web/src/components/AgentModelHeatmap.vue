<script setup lang="ts">
import { computed } from "vue";
import { use } from "echarts/core";
import { HeatmapChart } from "echarts/charts";
import { GridComponent, TooltipComponent, VisualMapComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import type { EChartsOption } from "echarts";
import VChart from "vue-echarts";
import "vue-echarts/style.css";
import { formatTokens, type AgentModelCell } from "../api";

use([CanvasRenderer, HeatmapChart, GridComponent, TooltipComponent, VisualMapComponent]);

const props = defineProps<{
  cells: AgentModelCell[];
}>();

const reduceMotion =
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const matrix = computed(() => {
  const agents: string[] = [];
  const models: string[] = [];
  const agentTotals = new Map<string, number>();
  const modelTotals = new Map<string, number>();

  for (const cell of props.cells) {
    agentTotals.set(cell.agentDisplayName, (agentTotals.get(cell.agentDisplayName) ?? 0) + cell.totalTokens);
    modelTotals.set(cell.modelId, (modelTotals.get(cell.modelId) ?? 0) + cell.totalTokens);
  }

  const rankedAgents = [...agentTotals.entries()].sort((a, b) => b[1] - a[1]);
  for (const [name] of rankedAgents) agents.push(name);

  const ranked = [...modelTotals.entries()].sort((a, b) => b[1] - a[1]).slice(0, 12);
  for (const [name] of ranked) models.push(name);
  const modelIndex = new Map(models.map((name, i) => [name, i]));
  const agentIndex = new Map(agents.map((name, i) => [name, i]));
  const kept = new Set(models);

  const data: [number, number, number][] = [];
  let max = 1;
  for (const cell of props.cells) {
    if (!kept.has(cell.modelId)) continue;
    const x = agentIndex.get(cell.agentDisplayName);
    const y = modelIndex.get(cell.modelId);
    if (x == null || y == null) continue;
    data.push([x, y, cell.totalTokens]);
    if (cell.totalTokens > max) max = cell.totalTokens;
  }

  return { agents, models, data, max };
});

const chartHeight = computed(() => Math.max(220, 72 + matrix.value.models.length * 28));

const option = computed<EChartsOption>(() => {
  const { agents, models, data, max } = matrix.value;
  return {
    animation: !reduceMotion,
    tooltip: {
      backgroundColor: "#ffffff",
      borderColor: "#ececec",
      textStyle: { color: "#111111", fontSize: 12 },
      formatter: (raw) => {
        const params = raw as { value?: [number, number, number] };
        const triple = params.value;
        if (!triple) return "";
        return `${agents[triple[0]]} · ${models[triple[1]]}<br/>${formatTokens(triple[2])}`;
      }
    },
    grid: {
      top: 8,
      right: 16,
      bottom: 8,
      left: 8,
      containLabel: true
    },
    xAxis: {
      type: "category",
      data: agents,
      splitArea: { show: true },
      axisTick: { show: false },
      axisLine: { lineStyle: { color: "#ececec" } },
      axisLabel: {
        color: "#737373",
        fontSize: 11,
        interval: 0
      }
    },
    yAxis: {
      type: "category",
      data: models,
      inverse: true,
      splitArea: { show: true },
      axisTick: { show: false },
      axisLine: { lineStyle: { color: "#ececec" } },
      axisLabel: {
        color: "#737373",
        fontSize: 11,
        width: 140,
        overflow: "truncate"
      }
    },
    visualMap: {
      show: false,
      min: 0,
      max,
      inRange: { color: ["#f4f0ec", "#ffd4b8", "#ff9a4d", "#ff6900", "#c2410c"] }
    },
    series: [
      {
        type: "heatmap",
        data,
        itemStyle: { borderColor: "#fcfaf8", borderWidth: 2, borderRadius: 4 },
        emphasis: { itemStyle: { shadowBlur: 0 } }
      }
    ]
  };
});
</script>

<template>
  <div v-if="cells.length" class="match-heat">
    <div class="match-heat-chart" :style="{ height: chartHeight + 'px' }">
      <VChart :option="option" autoresize />
    </div>
  </div>
  <p v-else class="hint">没有可展示的数据。</p>
</template>
