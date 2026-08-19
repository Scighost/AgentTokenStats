<script setup lang="ts">
import opencode from "../assets/logos/opencode.svg";
import codex from "../assets/logos/codex.svg";
import pi from "../assets/logos/pi.svg";
import claudeCode from "../assets/logos/claude-code.svg";

const logos: Record<string, string> = {
  opencode,
  codex,
  pi,
  "claude-code": claudeCode
};

defineProps<{
  agentId: string;
  name?: string;
  compact?: boolean;
}>();
</script>

<template>
  <span class="agent-logo" :class="[agentId, { compact }]" :title="name ?? agentId">
    <img v-if="logos[agentId]" :src="logos[agentId]" :alt="name ?? agentId" />
  </span>
</template>
