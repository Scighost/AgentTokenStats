import { inject, type ComputedRef, type InjectionKey, type Ref } from "vue";
import type { AgentItem, CombinedDashboard } from "../api";

export type DashboardContext = {
  agents: Ref<AgentItem[]>;
  selectedAgentIds: Ref<string[]>;
  range: Ref<string>;
  customRange: Ref<[string, string] | null>;
  loading: Ref<boolean>;
  dash: Ref<CombinedDashboard | null>;
  viewRange: ComputedRef<string>;
  showTimeRange: ComputedRef<boolean>;
  showSourceFilter: ComputedRef<boolean>;
  load: () => Promise<void>;
  refresh: () => Promise<void>;
};

export const dashboardKey: InjectionKey<DashboardContext> = Symbol("dashboard");

export function useDashboard(): DashboardContext {
  const ctx = inject(dashboardKey);
  if (!ctx) throw new Error("dashboard context missing");
  return ctx;
}

export const rangePresets = [
  { id: "all", label: "全部" },
  { id: "7d", label: "近 7 天" },
  { id: "30d", label: "近 30 天" },
  { id: "custom", label: "自定义" }
] as const;

export function dayText(value: unknown): string | undefined {
  if (typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value)) return value;
  if (value instanceof Date && !Number.isNaN(value.getTime())) {
    const y = value.getFullYear();
    const m = String(value.getMonth() + 1).padStart(2, "0");
    const d = String(value.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  }
  return undefined;
}

export function toRangeQuery(range: string, customRange: [string, string] | null): string {
  if (range !== "custom") return range;
  const from = dayText(customRange?.[0]);
  const to = dayText(customRange?.[1]);
  if (from && to) return `custom:${from}:${to}`;
  return "custom";
}

export function disableFuture(d: Date) {
  const today = new Date();
  today.setHours(23, 59, 59, 999);
  return d.getTime() > today.getTime();
}

export function projectName(cwd: string | null | undefined): string {
  if (!cwd) return "—";
  const parts = cwd.replaceAll("\\", "/").split("/").filter(Boolean);
  return parts.at(-1) || cwd;
}

export function redact(): string {
  return "******";
}

export function parseAgentsQuery(value: unknown): string[] {
  const text = Array.isArray(value) ? value[0] : value;
  if (typeof text !== "string" || !text.trim()) return [];
  return text.split(",").map((part) => part.trim()).filter(Boolean);
}
