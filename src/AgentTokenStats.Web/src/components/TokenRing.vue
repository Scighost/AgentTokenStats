<script setup lang="ts">
import { computed } from "vue";
import { use } from "echarts/core";
import { PieChart } from "echarts/charts";
import { LegendComponent, TooltipComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import type { EChartsOption } from "echarts";
import VChart from "vue-echarts";
import "vue-echarts/style.css";
import { formatShare, formatTokens, type Metrics } from "../api";

use([CanvasRenderer, PieChart, TooltipComponent, LegendComponent]);

const props = defineProps<{
  items: { label: string; metrics: Metrics }[];
}>();

const reduceMotion =
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const total = computed(() => props.items.reduce((sum, item) => sum + item.metrics.totalTokens, 0));

const option = computed<EChartsOption>(() => ({
  animation: !reduceMotion,
  tooltip: {
    trigger: "item",
    backgroundColor: "#ffffff",
    borderColor: "#ececec",
    textStyle: { color: "#111111", fontSize: 12 },
    formatter: (raw) => {
      const params = raw as { name?: string; value?: number };
      const value = Number(params.value) || 0;
      return `${params.name ?? ""}<br/>${formatTokens(value)} · ${formatShare(value, total.value)}`;
    }
  },
  legend: {
    orient: "vertical",
    right: 0,
    top: "middle",
    icon: "roundRect",
    itemWidth: 14,
    itemHeight: 10,
    itemGap: 12,
    textStyle: { color: "#737373", fontSize: 12 }
  },
  series: [
    {
      type: "pie",
      radius: ["46%", "66%"],
      center: ["34%", "50%"],
      avoidLabelOverlap: true,
      itemStyle: { borderColor: "#fcfaf8", borderWidth: 2 },
      label: { show: false },
      data: props.items.map((item) => ({
        name: item.label,
        value: item.metrics.totalTokens
      }))
    }
  ]
}));
</script>

<template>
  <div v-if="items.length" class="token-ring">
    <VChart :option="option" autoresize />
    <div class="token-ring-center">
      <strong>{{ formatTokens(total) }}</strong>
      <span>Token</span>
    </div>
  </div>
  <p v-else class="hint">没有可展示的数据。</p>
</template>
