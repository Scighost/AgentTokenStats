<script setup lang="ts">
import { computed, ref } from "vue";
import { emptyMetrics, formatShare, formatTokens, mergeMetrics } from "../api";
import DashEmpty from "../components/DashEmpty.vue";
import StackedBars from "../components/StackedBars.vue";
import { useDashboard } from "../dashboard/context";

const { dash } = useDashboard();
const query = ref("");

const empty = computed(() => {
  const d = dash.value;
  if (!d) return false;
  return d.summary.totalTokens === 0 && d.sessionCount === 0;
});

const models = computed(() => dash.value?.models ?? []);

const filtered = computed(() => {
  const list = models.value;
  const q = query.value.trim().toLowerCase();
  if (!q) return list;
  return list.filter((m) => {
    const name = (m.modelId || m.normalizedModelKey).toLowerCase();
    const provider = (m.providerId ?? "").toLowerCase();
    return name.includes(q) || provider.includes(q);
  });
});

const chartItems = computed(() => {
  const rolled = new Map<string, ReturnType<typeof emptyMetrics>>();
  for (const model of models.value) {
    const label = model.modelId || model.normalizedModelKey;
    rolled.set(label, mergeMetrics(rolled.get(label) ?? emptyMetrics(), model.metrics));
  }
  return [...rolled.entries()]
    .map(([label, metrics]) => ({ label, metrics }))
    .sort((a, b) => b.metrics.totalTokens - a.metrics.totalTokens)
    .slice(0, 10);
});

const providerItems = computed(() => dash.value?.providers ?? []);
</script>

<template>
  <template v-if="dash && empty">
    <DashEmpty :dash="dash" />
  </template>
  <template v-else-if="dash">
    <section class="panel">
      <h2>模型用量</h2>
      <StackedBars :items="chartItems" />
    </section>

    <section class="panel">
      <h2>供应商用量</h2>
      <StackedBars :items="providerItems" />
    </section>

    <section class="panel">
      <div class="panel-head">
        <h2>模型明细</h2>
        <el-input v-model="query" clearable placeholder="筛选模型" class="filter-input" />
      </div>
      <el-table :data="filtered" empty-text="没有模型数据">
        <el-table-column label="提供商" min-width="120" show-overflow-tooltip>
          <template #default="{ row }">{{ row.providerId || "—" }}</template>
        </el-table-column>
        <el-table-column label="模型" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">{{ row.modelId || row.normalizedModelKey }}</template>
        </el-table-column>
        <el-table-column label="消息" width="90">
          <template #default="{ row }">{{ row.metrics.messageCount }}</template>
        </el-table-column>
        <el-table-column label="输入" width="110">
          <template #default="{ row }">{{ formatTokens(row.metrics.inputTokens) }}</template>
        </el-table-column>
        <el-table-column label="输出" width="110">
          <template #default="{ row }">{{ formatTokens(row.metrics.outputTokens) }}</template>
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
