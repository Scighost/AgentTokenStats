<script setup lang="ts">
import { computed, ref } from "vue";
import { use } from "echarts/core";
import { HeatmapChart } from "echarts/charts";
import { CalendarComponent, TooltipComponent, VisualMapComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import type { EChartsOption } from "echarts";
import VChart from "vue-echarts";
import "vue-echarts/style.css";
import { formatTokens, type DayPoint } from "../api";
import HeatLegend from "./HeatLegend.vue";

use([CanvasRenderer, HeatmapChart, CalendarComponent, TooltipComponent, VisualMapComponent]);

type Pt = [number, number];

const props = defineProps<{
  days: DayPoint[];
}>();

const reduceMotion =
  typeof window !== "undefined" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const hoverValue = ref<number | null>(null);
const chartRef = ref<{ convertToPixel: (finder: unknown, value: unknown) => unknown; getWidth: () => number; getHeight: () => number } | null>(null);
const monthPaths = ref<string[]>([]);
const viewBox = ref("0 0 1 1");
const legendMax = computed(() => Math.max(1, ...props.days.map((d) => d.metrics.totalTokens)));

function onCellOver(params: { value?: unknown }) {
  const value = params.value;
  if (!Array.isArray(value) || value.length < 2) return;
  const n = Number(value[1]);
  hoverValue.value = Number.isFinite(n) ? n : null;
}

function onChartOut() {
  hoverValue.value = null;
}

function parseDay(text: string) {
  const [y, m, d] = text.split("-").map(Number);
  return new Date(y, m - 1, d, 12, 0, 0);
}

function ymd(date: Date) {
  const m = `${date.getMonth() + 1}`.padStart(2, "0");
  const d = `${date.getDate()}`.padStart(2, "0");
  return `${date.getFullYear()}-${m}-${d}`;
}

function shift(text: string, days: number) {
  const date = parseDay(text);
  date.setDate(date.getDate() + days);
  return ymd(date);
}

function snap(n: number) {
  return Math.round(n * 10) / 10;
}

function ptKey(p: Pt) {
  return `${p[0]},${p[1]}`;
}

function edgeKey(a: Pt, b: Pt) {
  return [ptKey(a), ptKey(b)].sort().join("|");
}

function roundPath(points: Pt[], radius: number) {
  if (points.length < 2) return "";
  if (points.length === 2) return `M ${points[0][0]} ${points[0][1]} L ${points[1][0]} ${points[1][1]}`;

  const parts = [`M ${points[0][0]} ${points[0][1]}`];
  for (let i = 1; i < points.length - 1; i++) {
    const prev = points[i - 1];
    const cur = points[i];
    const next = points[i + 1];
    const inX = cur[0] - prev[0];
    const inY = cur[1] - prev[1];
    const outX = next[0] - cur[0];
    const outY = next[1] - cur[1];
    const inLen = Math.hypot(inX, inY);
    const outLen = Math.hypot(outX, outY);
    if (inLen < 0.1 || outLen < 0.1) continue;
    const collinear = Math.abs(inX * outY - inY * outX) < 0.01;
    if (collinear) {
      parts.push(`L ${cur[0]} ${cur[1]}`);
      continue;
    }
    const r = Math.min(radius, inLen / 2, outLen / 2);
    const ax = cur[0] - (inX / inLen) * r;
    const ay = cur[1] - (inY / inLen) * r;
    const bx = cur[0] + (outX / outLen) * r;
    const by = cur[1] + (outY / outLen) * r;
    parts.push(`L ${ax} ${ay} Q ${cur[0]} ${cur[1]} ${bx} ${by}`);
  }
  const last = points[points.length - 1];
  parts.push(`L ${last[0]} ${last[1]}`);
  return parts.join(" ");
}

function updateMonthLines() {
  const chart = chartRef.value;
  const days = props.days;
  if (!chart || days.length < 8) {
    monthPaths.value = [];
    return;
  }

  const pixel = (date: string) => {
    const raw = chart.convertToPixel({ calendarIndex: 0 }, date);
    if (!Array.isArray(raw) || raw.length < 2) return null;
    const x = Number(raw[0]);
    const y = Number(raw[1]);
    if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
    return [x, y] as Pt;
  };

  const first = pixel(days[0].date);
  const nextDay = pixel(days[1].date);
  const nextWeek = pixel(days[7].date);
  if (!first || !nextDay || !nextWeek) {
    monthPaths.value = [];
    return;
  }

  const sw = Math.abs(nextWeek[0] - first[0]);
  const sh = Math.abs(nextDay[1] - first[1]);
  if (sw < 1 || sh < 1) {
    monthPaths.value = [];
    return;
  }

  const centers = new Map<string, Pt>();
  for (const day of days) {
    const p = pixel(day.date);
    if (p) centers.set(day.date, p);
  }

  const adj = new Map<string, Pt[]>();
  const addSeg = (a: Pt, b: Pt) => {
    if (ptKey(a) === ptKey(b)) return;
    const ka = ptKey(a);
    const kb = ptKey(b);
    adj.set(ka, [...(adj.get(ka) ?? []), b]);
    adj.set(kb, [...(adj.get(kb) ?? []), a]);
  };

  for (const day of days) {
    const cur = centers.get(day.date);
    if (!cur) continue;
    const rightDate = shift(day.date, 7);
    const right = centers.get(rightDate);
    if (right && parseDay(day.date).getMonth() !== parseDay(rightDate).getMonth()) {
      const x = snap((cur[0] + right[0]) / 2);
      addSeg([x, snap(cur[1] - sh / 2)], [x, snap(cur[1] + sh / 2)]);
    }
    if (parseDay(day.date).getDay() !== 0) {
      const downDate = shift(day.date, 1);
      const down = centers.get(downDate);
      if (down && parseDay(day.date).getMonth() !== parseDay(downDate).getMonth()) {
        const y = snap((cur[1] + down[1]) / 2);
        addSeg([snap(cur[0] - sw / 2), y], [snap(cur[0] + sw / 2), y]);
      }
    }
  }

  const used = new Set<string>();
  const paths: string[] = [];
  const starts: Pt[] = [];
  for (const [k, nodes] of adj) {
    const [x, y] = k.split(",").map(Number) as Pt;
    if (nodes.length === 1) starts.push([x, y]);
  }
  if (!starts.length) {
    for (const k of adj.keys()) {
      const [x, y] = k.split(",").map(Number) as Pt;
      starts.push([x, y]);
    }
  }

  const walk = (start: Pt) => {
    const line: Pt[] = [start];
    let prev: string | null = null;
    let cur = start;
    while (true) {
      const nexts = (adj.get(ptKey(cur)) ?? []).filter((n) => {
        const e = edgeKey(cur, n);
        if (used.has(e)) return false;
        return ptKey(n) !== prev;
      });
      if (!nexts.length) break;
      const n = nexts[0];
      used.add(edgeKey(cur, n));
      line.push(n);
      prev = ptKey(cur);
      cur = n;
    }
    return line;
  };

  for (const start of starts) {
    const pending = (adj.get(ptKey(start)) ?? []).some((n) => !used.has(edgeKey(start, n)));
    if (!pending) continue;
    const line = walk(start);
    if (line.length >= 2) paths.push(roundPath(line, Math.min(6, sw / 3, sh / 3)));
  }

  monthPaths.value = paths;
  viewBox.value = `0 0 ${chart.getWidth()} ${chart.getHeight()}`;
}

const option = computed<EChartsOption>(() => {
  const days = props.days;
  const start = days[0]?.date;
  const end = days.at(-1)?.date;
  const max = legendMax.value;
  const data = days.map((d) => [d.date, d.metrics.totalTokens] as [string, number]);

  return {
    animation: !reduceMotion,
    tooltip: {
      backgroundColor: "#ffffff",
      borderColor: "#ececec",
      textStyle: { color: "#111111", fontSize: 12 },
      formatter: (raw) => {
        const params = raw as { value?: [string, number] };
        const pair = params.value;
        if (!pair) return "";
        return `${pair[0]}<br/>${formatTokens(Number(pair[1]) || 0)}`;
      }
    },
    visualMap: {
      show: false,
      min: 0,
      max,
      inRange: { color: ["#f4f0ec", "#ffd4b8", "#ff9a4d", "#ff6900", "#c2410c"] }
    },
    calendar: {
      top: 28,
      left: 36,
      right: 12,
      bottom: 8,
      cellSize: ["auto", 13],
      range: start && end ? [start, end] : undefined,
      itemStyle: {
        borderWidth: 3,
        borderColor: "#fcfaf8",
        borderRadius: 4,
        color: "#f4f0ec"
      },
      splitLine: { show: false },
      yearLabel: { show: false },
      monthLabel: {
        nameMap: "ZH",
        color: "#737373",
        fontSize: 11
      },
      dayLabel: {
        firstDay: 1,
        nameMap: ["日", "一", "二", "三", "四", "五", "六"],
        color: "#737373",
        fontSize: 11
      }
    },
    series: [
      {
        type: "heatmap",
        coordinateSystem: "calendar",
        itemStyle: {
          borderRadius: 4,
          borderColor: "#fcfaf8",
          borderWidth: 2
        },
        data
      }
    ]
  };
});
</script>

<template>
  <div v-if="days.length" class="cal-heat">
    <div class="cal-heat-chart">
      <VChart
        ref="chartRef"
        :option="option"
        autoresize
        @mouseover="onCellOver"
        @globalout="onChartOut"
        @finished="updateMonthLines"
      />
      <svg class="cal-month-lines" :viewBox="viewBox" aria-hidden="true">
        <path v-for="(d, i) in monthPaths" :key="i" :d="d" />
      </svg>
    </div>
    <HeatLegend :max="legendMax" :value="hoverValue" />
  </div>
  <p v-else class="hint">没有可展示的数据。</p>
</template>
