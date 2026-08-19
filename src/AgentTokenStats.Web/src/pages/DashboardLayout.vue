<script setup lang="ts">
import { computed, onMounted, onUnmounted, provide, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ElMessage } from "element-plus";
import {
  api,
  agentsParam,
  type AgentItem,
  type CombinedDashboard
} from "../api";
import { Refresh, Menu } from "@element-plus/icons-vue";
import AgentLogo from "../components/AgentLogo.vue";
import {
  dashboardKey,
  disableFuture,
  parseAgentsQuery,
  rangePresets,
  toRangeQuery,
  type DashboardContext
} from "../dashboard/context";

const nav = [
  { name: "overview", label: "总览" },
  { name: "agents", label: "Agent 分析" },
  { name: "time", label: "时间分析" },
  { name: "models", label: "模型分析" },
  { name: "projects", label: "项目分析" }
] as const;

const route = useRoute();
const router = useRouter();
const agents = ref<AgentItem[]>([]);
const selectedAgentIds = ref<string[]>(parseAgentsQuery(route.query.agents));
const range = ref("all");
const customRange = ref<[string, string] | null>(null);
const loading = ref(false);
const dash = ref<CombinedDashboard | null>(null);
const ready = ref(false);
const navOpen = ref(false);
let narrowNav: MediaQueryList | undefined;

function closeNavIfWide() {
  if (narrowNav && !narrowNav.matches) navOpen.value = false;
}

const currentNav = computed(() => nav.find((item) => item.name === route.name) ?? nav[0]);

const showTimeRange = computed(() => route.name !== "overview");
const showSourceFilter = computed(() => route.name !== "agents");
const viewRange = computed(() =>
  showTimeRange.value ? toRangeQuery(range.value, customRange.value) : "all"
);
const availableAgents = computed(() => agents.value.filter((a) => a.found));
const selectedAgents = computed(() =>
  availableAgents.value.filter((a) => selectedAgentIds.value.includes(a.agentId))
);

function agentsQuery() {
  if (!showSourceFilter.value) return undefined;
  return agentsParam(selectedAgentIds.value);
}

async function loadAgents() {
  agents.value = await api.agents();
  const allowed = new Set(availableAgents.value.map((a) => a.agentId));
  selectedAgentIds.value = selectedAgentIds.value.filter((id) => allowed.has(id));
}

async function load() {
  if (viewRange.value === "custom") return;
  loading.value = true;
  try {
    dash.value = await api.stats(agentsQuery(), viewRange.value);
  } catch (err) {
    ElMessage.error(err instanceof Error ? err.message : "加载失败");
  } finally {
    loading.value = false;
  }
}

async function refresh() {
  loading.value = true;
  try {
    dash.value = await api.refresh(agentsQuery());
    if (viewRange.value !== "all") {
      dash.value = await api.stats(agentsQuery(), viewRange.value);
    }
    ElMessage.success("已重新扫描");
  } catch (err) {
    ElMessage.error(err instanceof Error ? err.message : "刷新失败");
  } finally {
    loading.value = false;
  }
}

function setRange(id: string) {
  range.value = id;
}

onMounted(async () => {
  narrowNav = window.matchMedia("(max-width: 860px)");
  narrowNav.addEventListener("change", closeNavIfWide);
  await loadAgents();
  await load();
  ready.value = true;
});

onUnmounted(() => {
  narrowNav?.removeEventListener("change", closeNavIfWide);
});

watch(selectedAgentIds, (ids) => {
  const next = agentsParam(ids);
  const cur = typeof route.query.agents === "string" ? route.query.agents : undefined;
  if (next === cur) return;
  void router.replace({ query: { ...route.query, agents: next } });
});

watch(
  () => route.query.agents,
  (value) => {
    const parsed = parseAgentsQuery(value);
    if (parsed.join(",") === selectedAgentIds.value.join(",")) return;
    selectedAgentIds.value = parsed;
  }
);

watch(
  () => route.name,
  () => {
    navOpen.value = false;
  }
);

watch([viewRange, selectedAgentIds, showSourceFilter], () => {
  if (!ready.value) return;
  void load();
});

provide(dashboardKey, {
  agents,
  selectedAgentIds,
  range,
  customRange,
  loading,
  dash,
  viewRange,
  showTimeRange,
  showSourceFilter,
  load,
  refresh
} satisfies DashboardContext);
</script>

<template>
  <div class="dash-shell">
    <header class="dash-brand">
      <button class="dash-menu btn btn-ghost btn-icon" type="button" aria-label="打开导航" @click="navOpen = true">
        <el-icon :size="18"><Menu /></el-icon>
      </button>
      <router-link class="dash-brand-name" to="/">Agent Token Stats</router-link>
      <span class="dash-brand-page">{{ currentNav.label }}</span>
      <button class="dash-brand-refresh btn btn-ghost btn-icon" type="button" :disabled="loading" aria-label="刷新" @click="refresh">
        <el-icon :size="18"><Refresh /></el-icon>
      </button>
    </header>

    <nav class="dash-nav dash-nav-side" aria-label="分析">
      <p class="dash-nav-label">分析</p>
      <router-link
        v-for="item in nav"
        :key="item.name"
        class="nav-item"
        :class="{ on: route.name === item.name }"
        :to="{ name: item.name, query: route.query }"
      >
        {{ item.label }}
      </router-link>
    </nav>

    <el-drawer
      v-model="navOpen"
      direction="ltr"
      size="280px"
      :show-close="false"
      :with-header="false"
      append-to-body
      class="dash-drawer"
    >
      <nav class="dash-nav dash-nav-drawer" aria-label="分析">
        <router-link class="dash-drawer-home" to="/" @click="navOpen = false">Agent Token Stats</router-link>
        <p class="dash-nav-label">分析</p>
        <router-link
          v-for="item in nav"
          :key="item.name"
          class="nav-item"
          :class="{ on: route.name === item.name }"
          :to="{ name: item.name, query: route.query }"
          @click="navOpen = false"
        >
          {{ item.label }}
        </router-link>
      </nav>
    </el-drawer>

    <div class="dash">
      <div class="dash-inner">
        <div class="toolbar">
          <el-select
            v-if="showSourceFilter"
            v-model="selectedAgentIds"
            multiple
            clearable
            placeholder="全部数据源"
            class="source-select"
          >
            <template #tag>
              <span class="source-picked">
                <AgentLogo
                  v-for="agent in selectedAgents"
                  :key="agent.agentId"
                  compact
                  :agent-id="agent.agentId"
                  :name="agent.displayName"
                />
              </span>
            </template>
            <el-option
              v-for="agent in availableAgents"
              :key="agent.agentId"
              :label="agent.displayName"
              :value="agent.agentId"
            >
              <span class="source-option">
                <AgentLogo compact :agent-id="agent.agentId" :name="agent.displayName" />
                <span>{{ agent.displayName }}</span>
              </span>
            </el-option>
          </el-select>
          <div v-if="showTimeRange" class="seg" role="radiogroup" aria-label="时间范围">
            <button
              v-for="preset in rangePresets"
              :key="preset.id"
              class="seg-btn"
              :class="{ on: range === preset.id }"
              type="button"
              role="radio"
              :aria-checked="range === preset.id"
              @click="setRange(preset.id)"
            >
              {{ preset.label }}
            </button>
          </div>
          <div v-if="showTimeRange && range === 'custom'" class="range-picker-wrap">
            <el-date-picker
              v-model="customRange"
              type="daterange"
              unlink-panels
              range-separator="至"
              start-placeholder="开始"
              end-placeholder="结束"
              value-format="YYYY-MM-DD"
              :disabled-date="disableFuture"
              class="range-picker"
            />
          </div>
          <div class="toolbar-end">
            <button class="btn btn-ghost btn-icon" type="button" :disabled="loading" aria-label="刷新" @click="refresh">
              <el-icon :size="18"><Refresh /></el-icon>
            </button>
          </div>
        </div>
        <router-view />
      </div>
    </div>
  </div>
</template>
