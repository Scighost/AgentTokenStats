export type Metrics = {
  totalTokens: number;
  inputTokens: number;
  outputTokens: number;
  reasoningTokens: number;
  cacheReadTokens: number;
  cacheWriteTokens: number;
  messageCount: number;
};

export type AgentItem = {
  agentId: string;
  displayName: string;
  found: boolean;
  resolvedPath: string | null;
  candidateTried: string[];
  error: string | null;
  manualPath: boolean;
  canScan: boolean;
};

export type DayPoint = { date: string; metrics: Metrics };
export type SlicePoint = { label: string; metrics: Metrics };
export type ModelPoint = {
  normalizedModelKey: string;
  modelId: string;
  providerId: string | null;
  metrics: Metrics;
};

export type AgentPoint = {
  agentId: string;
  displayName: string;
  found: boolean;
  canScan: boolean;
  dataRootPath: string | null;
  error: string | null;
  metrics: Metrics;
  sessionCount: number;
  recordCount: number;
};

export type AgentModelCell = {
  agentId: string;
  agentDisplayName: string;
  modelId: string;
  totalTokens: number;
};

export type CombinedDashboard = {
  agentIds: string[];
  found: boolean;
  canScan: boolean;
  error: string | null;
  summary: Metrics;
  recent14Days: DayPoint[];
  top7Days: DayPoint[];
  calendarDays: DayPoint[];
  timeline: DayPoint[];
  months: SlicePoint[];
  weekdays: SlicePoint[];
  hours: SlicePoint[];
  hotModels: ModelPoint[];
  models: ModelPoint[];
  providers: SlicePoint[];
  agents: AgentPoint[];
  agentModels: AgentModelCell[];
  topSessions: SessionRow[];
  scannedAt: string;
  recordCount: number;
  skippedRecords: number;
  sessionCount: number;
};

export type SessionRow = {
  sessionId: string;
  agentId: string;
  agentDisplayName: string | null;
  title: string | null;
  providerModel: string | null;
  cwd: string | null;
  isArchived: boolean;
  startedAt: string;
  endedAt: string;
  metrics: Metrics;
};

export type SessionPage = {
  items: SessionRow[];
  total: number;
  page: number;
  pageSize: number;
};

export type Meta = {
  version: string;
  product: string;
  privacy: string;
  license: string;
};

async function getJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || res.statusText);
  }
  return res.json() as Promise<T>;
}

function statsQuery(params: Record<string, string | number | boolean | undefined>) {
  const qs = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === "") continue;
    qs.set(key, String(value));
  }
  const text = qs.toString();
  return text ? `?${text}` : "";
}

export type ProjectRow = {
  name: string;
  cwd: string | null;
  sessionCount: number;
  lastSeen: string;
  metrics: Metrics;
  sessions: SessionRow[];
};

export type ProjectPage = {
  items: ProjectRow[];
  total: number;
  page: number;
  pageSize: number;
};

export const api = {
  meta: () => getJson<Meta>("/api/meta"),
  agents: () => getJson<AgentItem[]>("/api/agents"),
  stats: (agents: string | undefined, range: string) =>
    getJson<CombinedDashboard>(`/api/stats${statsQuery({ agents, range, includeArchived: true })}`),
  projects: (opts: {
    agents?: string;
    range: string;
    q?: string;
    sort?: string;
    page: number;
    pageSize: number;
  }) =>
    getJson<ProjectPage>(
      `/api/stats/projects${statsQuery({
        agents: opts.agents,
        range: opts.range,
        q: opts.q,
        sort: opts.sort,
        page: opts.page,
        pageSize: opts.pageSize
      })}`
    ),
  setPath: (agentId: string, path: string) =>
    getJson<AgentItem>(`/api/agents/${agentId}/path`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path })
    }),
  clearPath: (agentId: string) =>
    getJson<AgentItem>(`/api/agents/${agentId}/path`, { method: "DELETE" }),
  refresh: (agents: string | undefined) =>
    getJson<CombinedDashboard>(
      `/api/stats/refresh${statsQuery({ agents, includeArchived: true })}`,
      { method: "POST" }
    )
};

export function formatTokens(n: number): string {
  if (Math.abs(n) >= 1_000_000_000) return `${(n / 1_000_000_000).toFixed(2)}B`;
  if (Math.abs(n) >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`;
  if (Math.abs(n) >= 1_000) return `${(n / 1_000).toFixed(2)}k`;
  return String(n);
}

export function formatShare(part: number, total: number): string {
  if (total <= 0) return "—";
  return `${((part / total) * 100).toFixed(1)}%`;
}

export function sortAgents(agents: AgentItem[]): AgentItem[] {
  return [...agents].sort((a, b) => {
    if (a.found !== b.found) return a.found ? -1 : 1;
    return a.displayName.localeCompare(b.displayName, "en", { sensitivity: "base" });
  });
}

export function agentsParam(ids: string[]): string | undefined {
  return ids.length ? ids.join(",") : undefined;
}

export function mergeMetrics(into: Metrics, add: Metrics): Metrics {
  return {
    totalTokens: into.totalTokens + add.totalTokens,
    inputTokens: into.inputTokens + add.inputTokens,
    outputTokens: into.outputTokens + add.outputTokens,
    reasoningTokens: into.reasoningTokens + add.reasoningTokens,
    cacheReadTokens: into.cacheReadTokens + add.cacheReadTokens,
    cacheWriteTokens: into.cacheWriteTokens + add.cacheWriteTokens,
    messageCount: into.messageCount + add.messageCount
  };
}

export function emptyMetrics(): Metrics {
  return {
    totalTokens: 0,
    inputTokens: 0,
    outputTokens: 0,
    reasoningTokens: 0,
    cacheReadTokens: 0,
    cacheWriteTokens: 0,
    messageCount: 0
  };
}
