<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { api, type AgentItem } from "../api";

const props = defineProps<{
  agent: AgentItem | null;
  choices?: AgentItem[];
}>();
const emit = defineEmits<{ close: []; saved: [] }>();
const path = ref("");
const saving = ref(false);
const selectedId = ref("");

const current = computed(
  () => (props.choices ?? []).find((a) => a.agentId === selectedId.value) ?? props.agent
);
const showPicker = computed(() => (props.choices?.length ?? 0) > 1);

watch(
  () => props.agent,
  (agent) => {
    selectedId.value = agent?.agentId ?? "";
    path.value = agent?.resolvedPath ?? "";
  }
);

watch(selectedId, (id) => {
  const choice = (props.choices ?? []).find((a) => a.agentId === id);
  if (choice) path.value = choice.resolvedPath ?? "";
});

async function save() {
  if (!current.value) return;
  saving.value = true;
  try {
    await api.setPath(current.value.agentId, path.value.trim());
    ElMessage.success("路径已保存");
    emit("saved");
    emit("close");
  } catch (err) {
    ElMessage.error(err instanceof Error ? err.message : "无法使用该路径");
  } finally {
    saving.value = false;
  }
}

async function restore() {
  if (!current.value) return;
  saving.value = true;
  try {
    await api.clearPath(current.value.agentId);
    ElMessage.success("已恢复自动探测");
    emit("saved");
    emit("close");
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <el-dialog
    :model-value="!!agent"
    :title="current ? `指定 ${current.displayName} 路径` : '设置路径'"
    width="32rem"
    @close="emit('close')"
  >
    <p class="hint">填入数据根目录。程序只读打开，不会修改其中的文件。</p>
    <el-select v-if="showPicker" v-model="selectedId" class="path-agent">
      <el-option
        v-for="item in choices"
        :key="item.agentId"
        :label="item.displayName"
        :value="item.agentId"
      />
    </el-select>
    <el-input v-model="path" placeholder="例如 C:\\Users\\me\\.local\\share\\opencode" />
    <p v-if="current?.candidateTried?.length" class="hint tried">
      已尝试：{{ current.candidateTried.join(" · ") }}
    </p>
    <template #footer>
      <el-button :disabled="saving" @click="restore">恢复自动探测</el-button>
      <el-button type="primary" :loading="saving" @click="save">保存</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.path-agent { width: 100%; margin-bottom: 0.7rem; }
.tried { margin-top: 0.7rem; word-break: break-all; }
</style>
