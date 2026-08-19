<script setup lang="ts">
import { computed, ref } from "vue";
import { formatTokens } from "../api";

const props = defineProps<{
  max: number;
  value?: number | null;
}>();

const track = ref<HTMLElement | null>(null);
const hover = ref<number | null>(null);

const marker = computed(() => {
  const v = hover.value ?? props.value;
  if (v == null || !(props.max > 0)) return null;
  const t = Math.min(1, Math.max(0, v / props.max));
  return { pct: t * 100, text: formatTokens(v) };
});

function onMove(event: MouseEvent) {
  const el = track.value;
  if (!el) return;
  const rect = el.getBoundingClientRect();
  if (rect.width <= 0) return;
  const t = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
  hover.value = t * props.max;
}

function onLeave() {
  hover.value = null;
}
</script>

<template>
  <div class="heat-legend">
    <span>低</span>
    <div
      ref="track"
      class="heat-legend-track"
      @mousemove="onMove"
      @mouseleave="onLeave"
    >
      <div
        v-if="marker"
        class="heat-legend-pin"
        :style="{ left: `${marker.pct}%` }"
      />
      <div
        v-if="marker"
        class="heat-legend-tip"
        :style="{ left: `${marker.pct}%` }"
      >
        {{ marker.text }}
      </div>
    </div>
    <span>高</span>
  </div>
</template>
