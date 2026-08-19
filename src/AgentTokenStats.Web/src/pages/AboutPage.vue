<script setup lang="ts">
import { onMounted, ref } from "vue";
import { api, type Meta } from "../api";

const meta = ref<Meta | null>(null);
onMounted(async () => {
  meta.value = await api.meta();
});
</script>

<template>
  <div class="about">
    <h1>{{ meta?.product ?? "Agent Token Stats" }}</h1>
    <p>{{ meta?.version }}</p>
    <p class="hint">{{ meta?.privacy }}</p>
    <p class="hint">许可证：{{ meta?.license }}</p>
    <p class="hint">支持 OpenCode、Codex、Pi Agent、Claude Code。关闭本程序即停止服务。不注册系统服务，不写入 Agent 数据目录。</p>
  </div>
</template>
