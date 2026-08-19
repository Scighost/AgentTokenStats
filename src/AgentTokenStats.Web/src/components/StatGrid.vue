<script setup lang="ts">
import { formatTokens, type Metrics } from "../api";

defineProps<{
  summary: Metrics;
  sessionCount: number;
}>();
</script>

<template>
  <dl class="stat-grid">
    <div class="stat">
      <dt>总量</dt>
      <dd>{{ formatTokens(summary.totalTokens) }}</dd>
    </div>
    <div class="stat">
      <dt>输入</dt>
      <dd>{{ formatTokens(summary.inputTokens) }}</dd>
    </div>
    <div class="stat">
      <dt>读缓存</dt>
      <dd>{{ formatTokens(summary.cacheReadTokens) }}</dd>
    </div>
    <div class="stat">
      <dt>输出</dt>
      <dd>{{ formatTokens(summary.outputTokens) }}</dd>
    </div>
    <div class="stat">
      <dt>写缓存</dt>
      <dd>{{ formatTokens(summary.cacheWriteTokens) }}</dd>
    </div>
    <div class="stat">
      <dt>推理</dt>
      <dd>{{ formatTokens(summary.reasoningTokens) }}</dd>
    </div>
    <div class="stat">
      <dt>会话数</dt>
      <dd>{{ sessionCount }}</dd>
    </div>
    <div class="stat">
      <dt>消息数</dt>
      <dd>{{ summary.messageCount }}</dd>
    </div>
  </dl>
</template>
