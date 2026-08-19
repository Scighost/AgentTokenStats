<script setup lang="ts">
import { computed, ref } from "vue";
import { formatTokens } from "../api";
import CalendarHeatmap from "../components/CalendarHeatmap.vue";
import DashEmpty from "../components/DashEmpty.vue";
import StackedBars from "../components/StackedBars.vue";
import StatGrid from "../components/StatGrid.vue";
import { projectName, redact, useDashboard } from "../dashboard/context";

const { dash } = useDashboard();
const hideInfo = ref(false);

const empty = computed(() => {
  const d = dash.value;
  if (!d) return false;
  return d.summary.totalTokens === 0 && d.sessionCount === 0;
});
</script>

<template>
  <template v-if="dash && empty">
    <DashEmpty :dash="dash" />
  </template>
  <template v-else-if="dash">
    <StatGrid :summary="dash.summary" :session-count="dash.sessionCount" />

    <section class="panel">
      <h2>近一年</h2>
      <CalendarHeatmap :days="dash.calendarDays" />
    </section>

    <section class="panel">
      <h2>最近 14 天</h2>
      <StackedBars
        orientation="vertical"
        :items="dash.recent14Days.map((d) => ({ label: d.date.slice(5), metrics: d.metrics }))"
      />
    </section>

    <section class="panel">
      <h2>Top 7 天</h2>
      <StackedBars :items="dash.top7Days.map((d) => ({ label: d.date, metrics: d.metrics }))" />
    </section>

    <section class="panel">
      <h2>热门模型</h2>
      <StackedBars
        :items="dash.hotModels.map((m) => ({
          label: m.modelId || m.normalizedModelKey,
          metrics: m.metrics
        }))"
      />
    </section>

    <section class="panel">
      <div class="panel-head">
        <h2>高消耗会话</h2>
        <div class="session-toggles">
          <el-checkbox v-model="hideInfo">隐藏信息</el-checkbox>
        </div>
      </div>
      <el-table :data="dash.topSessions" empty-text="没有会话">
        <el-table-column label="项目" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">{{ hideInfo ? redact() : projectName(row.cwd) }}</template>
        </el-table-column>
        <el-table-column label="标题" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">{{ hideInfo ? redact() : (row.title || "—") }}</template>
        </el-table-column>
        <el-table-column label="供应商/模型" min-width="200" show-overflow-tooltip>
          <template #default="{ row }">{{ row.providerModel || "—" }}</template>
        </el-table-column>
        <el-table-column label="时间" min-width="120">
          <template #default="{ row }">{{ row.endedAt.slice(0, 10) }}</template>
        </el-table-column>
        <el-table-column label="Token" width="120">
          <template #default="{ row }">{{ formatTokens(row.metrics.totalTokens) }}</template>
        </el-table-column>
      </el-table>
    </section>
  </template>
</template>
