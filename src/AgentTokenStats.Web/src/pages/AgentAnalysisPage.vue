<script setup lang="ts">
import { computed } from "vue";
import { formatShare, formatTokens } from "../api";
import AgentLogo from "../components/AgentLogo.vue";
import AgentModelHeatmap from "../components/AgentModelHeatmap.vue";
import DashEmpty from "../components/DashEmpty.vue";
import StackedBars from "../components/StackedBars.vue";
import TokenRing from "../components/TokenRing.vue";
import { useDashboard } from "../dashboard/context";

const { dash } = useDashboard();

const empty = computed(() => {
  const d = dash.value;
  if (!d) return false;
  return d.summary.totalTokens === 0 && d.sessionCount === 0;
});

const chartItems = computed(() =>
  (dash.value?.agents ?? [])
    .filter((a) => a.found && a.metrics.totalTokens > 0)
    .map((a) => ({ label: a.displayName, metrics: a.metrics }))
);

const agentRows = computed(() => (dash.value?.agents ?? []).filter((a) => a.found));
</script>

<template>
  <template v-if="dash && empty">
    <DashEmpty :dash="dash" />
  </template>
  <template v-else-if="dash">
    <section class="panel">
      <h2>Token 消耗</h2>
      <div class="agent-charts">
        <StackedBars :items="chartItems" />
        <TokenRing :items="chartItems" />
      </div>
    </section>

    <section class="panel">
      <h2>Agent 与模型</h2>
      <AgentModelHeatmap :cells="dash.agentModels" />
    </section>

    <section class="panel">
      <h2>Agent 明细</h2>
      <el-table :data="agentRows" empty-text="没有 Agent 数据">
        <el-table-column label="Agent" min-width="160">
          <template #default="{ row }">
            <span class="agent-cell">
              <AgentLogo :agent-id="row.agentId" :name="row.displayName" />
              <span>{{ row.displayName }}</span>
            </span>
          </template>
        </el-table-column>
        <el-table-column label="会话" width="90">
          <template #default="{ row }">{{ row.sessionCount }}</template>
        </el-table-column>
        <el-table-column label="消息" width="90">
          <template #default="{ row }">{{ row.metrics.messageCount }}</template>
        </el-table-column>
        <el-table-column label="Token" width="120">
          <template #default="{ row }">{{ formatTokens(row.metrics.totalTokens) }}</template>
        </el-table-column>
        <el-table-column label="占比" width="90">
          <template #default="{ row }">{{ formatShare(row.metrics.totalTokens, dash.summary.totalTokens) }}</template>
        </el-table-column>
      </el-table>
    </section>
  </template>
</template>
