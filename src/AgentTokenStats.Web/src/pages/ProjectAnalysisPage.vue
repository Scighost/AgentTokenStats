<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { api, agentsParam, formatTokens, type ProjectPage, type ProjectRow } from "../api";
import DashEmpty from "../components/DashEmpty.vue";
import { redact, useDashboard } from "../dashboard/context";

const { dash, selectedAgentIds, viewRange } = useDashboard();
const hideInfo = ref(false);
const query = ref("");
const sort = ref("tokens");
const page = ref(1);
const pageSize = 20;
const projects = ref<ProjectPage | null>(null);
const loading = ref(false);
const tableRef = ref<{ toggleRowExpansion: (row: ProjectRow) => void } | null>(null);

const empty = computed(() => {
  const d = dash.value;
  if (!d) return false;
  return d.summary.totalTokens === 0 && d.sessionCount === 0;
});

function projectKey(row: ProjectRow) {
  return row.cwd ?? "";
}

function onProjectRowClick(row: ProjectRow, column: { type?: string }) {
  if (column?.type === "expand") return;
  tableRef.value?.toggleRowExpansion(row);
}

async function loadProjects() {
  if (viewRange.value === "custom") return;
  loading.value = true;
  try {
    projects.value = await api.projects({
      agents: agentsParam(selectedAgentIds.value),
      range: viewRange.value,
      q: query.value.trim() || undefined,
      sort: sort.value,
      page: page.value,
      pageSize
    });
  } catch (err) {
    ElMessage.error(err instanceof Error ? err.message : "加载项目失败");
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void loadProjects();
});

watch([selectedAgentIds, viewRange, sort], () => {
  if (page.value !== 1) page.value = 1;
  else void loadProjects();
});

watch(page, () => {
  void loadProjects();
});

let timer = 0;
watch(query, () => {
  window.clearTimeout(timer);
  timer = window.setTimeout(() => {
    if (page.value !== 1) page.value = 1;
    else void loadProjects();
  }, 280);
});
</script>

<template>
  <template v-if="dash && empty && !query">
    <DashEmpty :dash="dash" />
  </template>
  <template v-else-if="dash">
    <div class="panel-head session-toolbar">
      <h2>项目</h2>
      <div class="session-tools">
        <el-input v-model="query" clearable placeholder="搜索项目" class="filter-input" />
        <div class="seg" role="radiogroup" aria-label="排序">
          <button class="seg-btn" :class="{ on: sort === 'tokens' }" type="button" @click="sort = 'tokens'">按 Token</button>
          <button class="seg-btn" :class="{ on: sort === 'time' }" type="button" @click="sort = 'time'">按时间</button>
        </div>
        <el-checkbox v-model="hideInfo">隐藏信息</el-checkbox>
      </div>
    </div>
    <el-table
      ref="tableRef"
      class="project-table"
      :data="projects?.items ?? []"
      :row-key="projectKey"
      empty-text="没有项目"
      v-loading="loading"
      @row-click="onProjectRowClick"
    >
      <el-table-column type="expand">
        <template #default="{ row }">
          <el-table :data="row.sessions" empty-text="没有会话" class="project-sessions">
            <el-table-column label="标题" min-width="160" show-overflow-tooltip>
              <template #default="{ row: session }">{{ hideInfo ? redact() : (session.title || "—") }}</template>
            </el-table-column>
            <el-table-column label="供应商/模型" min-width="180" show-overflow-tooltip>
              <template #default="{ row: session }">{{ session.providerModel || "—" }}</template>
            </el-table-column>
            <el-table-column label="消息" width="80">
              <template #default="{ row: session }">{{ session.metrics.messageCount }}</template>
            </el-table-column>
            <el-table-column label="Token" width="110">
              <template #default="{ row: session }">{{ formatTokens(session.metrics.totalTokens) }}</template>
            </el-table-column>
            <el-table-column label="输入" width="100">
              <template #default="{ row: session }">{{ formatTokens(session.metrics.inputTokens) }}</template>
            </el-table-column>
            <el-table-column label="输出" width="100">
              <template #default="{ row: session }">{{ formatTokens(session.metrics.outputTokens) }}</template>
            </el-table-column>
            <el-table-column label="推理" width="100">
              <template #default="{ row: session }">{{ formatTokens(session.metrics.reasoningTokens) }}</template>
            </el-table-column>
            <el-table-column label="缓存读" width="100">
              <template #default="{ row: session }">{{ formatTokens(session.metrics.cacheReadTokens) }}</template>
            </el-table-column>
            <el-table-column label="缓存写" width="100">
              <template #default="{ row: session }">{{ formatTokens(session.metrics.cacheWriteTokens) }}</template>
            </el-table-column>
          </el-table>
        </template>
      </el-table-column>
      <el-table-column label="项目" min-width="180" show-overflow-tooltip>
        <template #default="{ row }">{{ hideInfo ? redact() : row.name }}</template>
      </el-table-column>
      <el-table-column label="路径" min-width="220" show-overflow-tooltip>
        <template #default="{ row }">{{ hideInfo ? redact() : (row.cwd || "—") }}</template>
      </el-table-column>
      <el-table-column label="会话" width="90">
        <template #default="{ row }">{{ row.sessionCount }}</template>
      </el-table-column>
      <el-table-column label="最近" min-width="120">
        <template #default="{ row }">{{ row.lastSeen.slice(0, 10) }}</template>
      </el-table-column>
      <el-table-column label="消息" width="80">
        <template #default="{ row }">{{ row.metrics.messageCount }}</template>
      </el-table-column>
      <el-table-column label="Token" width="120">
        <template #default="{ row }">{{ formatTokens(row.metrics.totalTokens) }}</template>
      </el-table-column>
    </el-table>
    <div v-if="(projects?.total ?? 0) > pageSize" class="pager">
      <el-pagination
        v-model:current-page="page"
        :page-size="pageSize"
        :total="projects?.total ?? 0"
        layout="prev, pager, next, total"
      />
    </div>
  </template>
</template>
