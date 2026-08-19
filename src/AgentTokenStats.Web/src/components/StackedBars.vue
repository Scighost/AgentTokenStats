<script setup lang="ts">
import { computed } from "vue";
import { use } from "echarts/core";
import { BarChart } from "echarts/charts";
import { GridComponent, TooltipComponent, LegendComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import type { BarSeriesOption, EChartsOption } from "echarts";
import VChart from "vue-echarts";
import "vue-echarts/style.css";
import { formatTokens, type Metrics } from "../api";

use([CanvasRenderer, BarChart, GridComponent, TooltipComponent, LegendComponent]);

const props = defineProps<{
  items: { label: string; metrics: Metrics }[];
  orientation?: "horizontal" | "vertical";
  totalOnly?: boolean;
}>();

type TokenPartKey =
  | "inputTokens"
  | "outputTokens"
  | "reasoningTokens"
  | "cacheReadTokens"
  | "cacheWriteTokens";

const TOKEN_PARTS: { key: TokenPartKey; label: string }[] = [
  { key: "inputTokens", label: "输入" },
  { key: "outputTokens", label: "输出" },
  { key: "reasoningTokens", label: "推理" },
  { key: "cacheReadTokens", label: "缓存读" },
  { key: "cacheWriteTokens", label: "缓存写" }
];

function formatAxisTokens(n: number): string {
  const abs = Math.abs(n);
  if (abs >= 1_000_000_000) return `${Math.round(n / 1_000_000_000)}B`;
  if (abs >= 1_000_000) return `${Math.round(n / 1_000_000)}M`;
  if (abs >= 1_000) return `${Math.round(n / 1_000)}k`;
  return String(Math.round(n));
}

const vertical = computed(() => props.orientation === "vertical");
const chartHeight = computed(() =>
  vertical.value ? 280 : Math.max(180, 72 + props.items.length * 42)
);

const totalOnly = computed(() => props.totalOnly === true);

const reduceMotion =
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const axisText = {
  color: "#737373",
  fontSize: 11
};

type TipParam = {
  axisValueLabel?: string;
  name?: string;
  marker?: string;
  seriesName?: string;
  value?: unknown;
};

function tooltipHtml(params: TipParam | TipParam[]) {
  const list = Array.isArray(params) ? params : [params];
  if (!list.length) return "";
  const title = String(list[0].axisValueLabel ?? list[0].name ?? "");
  const lines = [`<strong>${title}</strong>`];
  let total = 0;
  for (const item of list) {
    const value = Number(item.value) || 0;
    if (value <= 0) continue;
    total += value;
    lines.push(`${item.marker ?? ""}${item.seriesName} ${formatTokens(value)}`);
  }
  if (!totalOnly.value) lines.push(`合计 ${formatTokens(total)}`);
  return lines.join("<br/>");
}

const option = computed<EChartsOption>(() => {
  const labels = props.items.map((item) => item.label);

  const series: BarSeriesOption[] = totalOnly.value
    ? [
        {
          name: "Token",
          type: "bar",
          barMaxWidth: vertical.value ? 28 : 18,
          data: props.items.map((item) => item.metrics.totalTokens)
        }
      ]
    : TOKEN_PARTS.map((part) => ({
        name: part.label,
        type: "bar",
        stack: "tokens",
        barMaxWidth: vertical.value ? 28 : 18,
        emphasis: { focus: "series" },
        data: props.items.map((item) => item.metrics[part.key])
      }));

  const categoryAxis = {
    type: "category" as const,
    data: labels,
    axisTick: { show: false },
    axisLine: { lineStyle: { color: "#ececec" } },
    axisLabel: {
      ...axisText,
      hideOverlap: true,
      width: vertical.value ? 48 : 120,
      overflow: "truncate" as const
    }
  };

  const valueAxis = {
    type: "value" as const,
    minInterval: 1,
    axisLabel: {
      ...axisText,
      formatter: (value: number) => formatAxisTokens(value)
    },
    splitLine: { lineStyle: { color: "#ececec", type: "dashed" as const } }
  };

  return {
    animation: !reduceMotion,
    tooltip: {
      trigger: "axis",
      axisPointer: { type: "shadow" },
      backgroundColor: "#ffffff",
      borderColor: "#ececec",
      textStyle: { color: "#111111", fontSize: 12 },
      formatter: tooltipHtml
    },
    legend: totalOnly.value
      ? { show: false }
      : {
          top: 0,
          left: 0,
          icon: "roundRect",
          itemWidth: 14,
          itemHeight: 10,
          itemGap: 16,
          textStyle: { color: "#737373", fontSize: 12 }
        },
    grid: {
      top: totalOnly.value ? 12 : 36,
      right: 12,
      bottom: 8,
      left: 8,
      containLabel: true
    },
    xAxis: vertical.value ? categoryAxis : valueAxis,
    yAxis: vertical.value ? valueAxis : { ...categoryAxis, inverse: true },
    series
  };
});
</script>

<template>
  <div v-if="items.length" class="stack-chart" :style="{ height: chartHeight + 'px' }">
    <VChart :option="option" autoresize />
  </div>
  <p v-else class="hint">没有可展示的数据。</p>
</template>
