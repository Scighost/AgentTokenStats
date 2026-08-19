<script setup lang="ts">
import type { CombinedDashboard } from "../api";

defineProps<{
  dash: CombinedDashboard;
}>();
</script>

<template>
  <div class="empty">
    <h1>{{ dash.found && !dash.canScan ? "扫描适配尚未就绪" : "还没有可统计的数据" }}</h1>
    <p class="hint">
      {{
        dash.canScan
          ? "在首页为数据源指定含会话存储的路径后再刷新。"
          : "所选数据源暂时无法扫描。可在首页指定路径。"
      }}
    </p>
    <p v-if="dash.error" class="hint">{{ dash.error }}</p>
  </div>
</template>
