<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { api, sortAgents, type AgentItem, type Meta } from "../api";
import AgentLogo from "../components/AgentLogo.vue";
import PathDialog from "../components/PathDialog.vue";

const agents = ref<AgentItem[]>([]);
const meta = ref<Meta | null>(null);
const pathAgent = ref<AgentItem | null>(null);

const agentGroups = computed(() => {
  const sorted = sortAgents(agents.value);
  const ready = sorted.filter((a) => a.found);
  const unavailable = sorted.filter((a) => !a.found);
  return [ready, unavailable].filter((group) => group.length > 0);
});

onMounted(async () => {
  meta.value = await api.meta();
  agents.value = await api.agents();
});

function isStaticGroup(group: AgentItem[]) {
  return group.length > 0 && group.every((agent) => agent.found);
}

function openPath(agent: AgentItem) {
  pathAgent.value = agent;
}

async function reload() {
  agents.value = await api.agents();
}
</script>

<template>
  <div class="page home-page">
    <section class="hero">
      <h1>{{ meta?.product ?? "Agent Token Stats" }}</h1>
      <p class="tag">基于 Agent 本地会话历史记录统计 Token 用量</p>
      <div class="hero-actions">
        <router-link class="btn btn-solid" to="/dashboard/overview">立即查看 →</router-link>
        <router-link class="btn btn-ghost" to="/about">关于产品 →</router-link>
      </div>
    </section>

    <section class="section">
      <h2>数据源</h2>
      <div class="agent-boards" aria-label="数据源">
        <div v-for="(group, index) in agentGroups" :key="index" class="agent-row">
          <template v-if="isStaticGroup(group)">
            <div
              v-for="agent in group"
              :key="agent.agentId"
              class="agent-tile is-static"
            >
              <AgentLogo :agent-id="agent.agentId" :name="agent.displayName" />
              <span>{{ agent.displayName }}</span>
            </div>
          </template>
          <button
            v-else
            v-for="agent in group"
            :key="agent.agentId"
            class="agent-tile is-off"
            type="button"
            @click="openPath(agent)"
          >
            <AgentLogo :agent-id="agent.agentId" :name="agent.displayName" />
            <span>{{ agent.displayName }}</span>
          </button>
        </div>
      </div>
    </section>

    <PathDialog :agent="pathAgent" @close="pathAgent = null" @saved="reload" />
  </div>
</template>
