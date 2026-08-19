<script setup lang="ts">
import { computed } from "vue";
import DashEmpty from "../components/DashEmpty.vue";
import StackedBars from "../components/StackedBars.vue";
import { useDashboard } from "../dashboard/context";

const { dash, range } = useDashboard();

const empty = computed(() => {
  const d = dash.value;
  if (!d) return false;
  return d.summary.totalTokens === 0 && d.sessionCount === 0;
});

const showDaily = computed(() => (dash.value?.timeline.length ?? 0) > 0);
const showMonthly = computed(() => !showDaily.value || range.value === "all");
</script>

<template>
  <template v-if="dash && empty">
    <DashEmpty :dash="dash" />
  </template>
  <template v-else-if="dash">
    <section v-if="showDaily" class="panel">
      <h2>日统计</h2>
      <StackedBars
        orientation="vertical"
        :items="dash.timeline.map((d) => ({ label: d.date.slice(5), metrics: d.metrics }))"
      />
    </section>

    <section v-if="showMonthly && dash.months.length" class="panel">
      <h2>月统计</h2>
      <StackedBars
        orientation="vertical"
        :items="dash.months.map((m) => ({ label: m.label, metrics: m.metrics }))"
      />
    </section>

    <div class="split-charts">
      <section class="panel">
        <h2>星期分布</h2>
        <StackedBars
          total-only
          orientation="vertical"
          :items="dash.weekdays.map((d) => ({ label: d.label, metrics: d.metrics }))"
        />
      </section>
      <section class="panel">
        <h2>小时分布</h2>
        <StackedBars
          total-only
          orientation="vertical"
          :items="dash.hours.map((d) => ({ label: d.label, metrics: d.metrics }))"
        />
      </section>
    </div>
  </template>
</template>
