<script setup lang="ts">
import { computed } from "vue";
import { formatTokens, type Metrics } from "../api";
import { TOKEN_PARTS, tokenPartValue } from "../tokenParts";

const props = defineProps<{
  metrics: Metrics;
  max: number;
}>();

const segments = computed(() => {
  const scale = props.max > 0 ? props.max : 1;
  return TOKEN_PARTS.map((part) => ({
    ...part,
    value: tokenPartValue(props.metrics, part.key),
    width: (tokenPartValue(props.metrics, part.key) / scale) * 100
  })).filter((part) => part.value > 0);
});

const tip = computed(() => {
  const lines = TOKEN_PARTS.map((part) => {
    const value = tokenPartValue(props.metrics, part.key);
    if (value <= 0) return null;
    return `${part.label} ${formatTokens(value)}`;
  }).filter((line): line is string => line !== null);
  lines.push(`合计 ${formatTokens(props.metrics.totalTokens)}`);
  return lines.join("\n");
});
</script>

<template>
  <div class="token-bar" :title="tip">
    <div class="token-bar-track">
      <span
        v-for="part in segments"
        :key="part.key"
        :style="{ width: `${part.width}%`, background: part.color }"
      />
    </div>
    <span class="token-bar-total">{{ formatTokens(metrics.totalTokens) }}</span>
  </div>
</template>
