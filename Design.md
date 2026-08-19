# Agent Token Stats 产品设计文档

> 版本：v0.5.0（与当前仓库实现同步）  
> 存储细节权威来源：[AgentSessionStore.md](AgentSessionStore.md)  
> 本文档描述产品口径、适配边界与信息架构；实现以代码为准，文档随代码演进。

---

## 1. 产品定位

Agent Token Stats 是一个基于本地会话历史的 Token 统计工具，面向使用 AI Agent 的开发者，把分散在各 Agent 目录里的用量汇总成可浏览的指标：总量、时间分布、模型、供应商、项目与会话。

产品形态：单文件自包含程序（程序集名 `ats`），Vue 3 前端嵌入 ASP.NET Core。启动后只监听环回地址并打开浏览器。当前适配 **OpenCode、Codex、Pi Agent、Claude Code** 四个数据源，默认合并展示，也可按数据源筛选。

不统计费用，不读取凭据，不上传数据。

---

## 2. 技术路线

| 层 | 选型 | 说明 |
|---|---|---|
| 前端 | Vue 3 + Vue Router + Element Plus + ECharts | SPA，`vite build` 输出到 `src/AgentTokenStats/wwwroot` 并嵌入程序集 |
| 后端 | ASP.NET Core（`net10.0`） | 本机 HTTP API + 嵌入静态文件 |
| 发布 | 单文件、部分剪裁、自包含运行时 | Windows / Linux / macOS × x64 / arm64 |
| 数据 | `IAgentDataSource` 适配器 | 流式产出 `UnifiedUsageRecord`，边扫边聚合 |

```text
LocalStores (OpenCode / Codex / Pi / Claude Code)
    → IAgentDataSource（探测 + 只读扫描）
    → UnifiedUsageRecord
    → UsageAggregator（按本地日历日 / 小时 / 模型 / 会话）
    → CombinedDashboard（多源合并）
    → Vue Dashboard
```

---

## 3. 设计原则

1. **零配置启动**：一个可执行文件即可运行；`--no-browser` 或环境变量 `AGENTTOKENSTATS_NO_BROWSER=1` 可只起服务。
2. **只读不打扰**：只读打开 Agent 存储；不写 WAL、不改用户数据；仅绑定 `127.0.0.1`。
3. **一次适配，处处统计**：源格式差异停在适配器，上层指标口径一致。
4. **边读边聚合**：不保留全量消息列表；API 只返回聚合与分页/嵌套会话。
5. **离线**：前端、Logo 内嵌；无价格表、无默认网络客户端。
6. **隐私最小化**：不解析会话正文；不读凭据文件；路径覆盖写在本机应用配置目录。

---

## 4. 统一数据模型

Token 分项为 **input / output / reasoning / cache read / cache write**。cache read 与 cache write 均属输入侧提示缓存；不存在「输出缓存」。`totalTokens` = 五者之和。

时间桶一律用 **`Timestamp.ToLocalTime()` 的本地日历日与小时**，不用 UTC 日期。

### 4.1 `UnifiedUsageRecord`

一条记录对应一次可归因用量（通常一条 assistant 消息或一次 turn）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `agentId` | string | `opencode` / `codex` / `pi` / `claude-code` |
| `sessionId` | string | 会话稳定 ID |
| `timestamp` | DateTimeOffset | 用量发生时间 |
| `modelId` | string | 源模型 ID |
| `providerId` | string? | 提供商 ID（有则填） |
| `normalizedModelKey` | string | `provider:model`（小写）；无 provider 时仅为 model |
| `inputTokens` 等 | long | 缺失记 `0` |
| `messageCount` | int | 通常 0 或 1 |
| `cwd` / `title` | string? | 会话元数据 |
| `isArchived` | bool | 归档；扫描默认包含归档会话 |

### 4.2 四源字段映射

以调研报告与适配器实现为准。

| 统一字段 | OpenCode | Codex | Pi Agent | Claude Code |
|---|---|---|---|---|
| `inputTokens` | `$.tokens.input` | `token_count` 累计差分后的 input，再减去 cache read | `usage.input` | `usage.input_tokens` |
| `outputTokens` | `$.tokens.output` | 同上的 output 差分 | `usage.output` | `usage.output_tokens` |
| `reasoningTokens` | `$.tokens.reasoning` | `reasoning_output_tokens` 差分，无则 `0` | 恒 `0` | `reasoning_tokens` / `thinking_tokens`，无则 `0` |
| `cacheReadTokens` | `$.tokens.cache.read` | `cached_input_tokens` 等差分，并从 input 扣除 | `usage.cacheRead` | `cache_read_input_tokens` |
| `cacheWriteTokens` | `$.tokens.cache.write` | 无 → `0` | `usage.cacheWrite` | `cache_creation_input_tokens`；为 0 时用 `cache_creation` 的 5m+1h ephemeral 之和 |
| `messageCount` | assistant 消息行 | 非零 `token_count` 差分计 1 | `role=assistant` | `type=assistant` |
| `modelId` / `providerId` | `modelID` / `providerID` | turn_context / 线程元数据 | message 内 model/provider | `message.model`；provider 由模型名推断（如 `claude*` → `anthropic`） |
| `isArchived` | `session.time_archived > 0` | 索引库 `threads.archived` 或归档目录 | 恒 `false` | 恒 `false` |

OpenCode 以 `message.data` JSON 为真相源，不用 session 表 `tokens_*` 做唯一汇总。测试 `Summary_matches_design_sql` 对账下方 SQL。

### 4.3 聚合

扫描中累加到 `AggregationSnapshot`：

| 结构 | 维度 |
|---|---|
| `Summary` | 全局 |
| `ByDay` | 本地日历日 |
| `ByDayHour` | 本地日 + 小时 |
| `ByModel` | `normalizedModelKey`（含 provider 前缀） |
| `ByDayModel` | 日 × 模型键 |
| `BySession` | `sessionId`；会话内另有 `ByModel` 供「供应商/模型」展示 |

合并多源时，会话键为 `agentId:sessionId`。

对外 `CombinedDashboard` 再投影：

| 字段 | 规则 |
|---|---|
| `summary` | 当前时间窗内合计 |
| `calendarDays` | 近一年对齐到周一的连续日（总览热力图） |
| `recent14Days` / `top7Days` | 近 14 个本地日；历史按 token 最高 7 日 |
| `timeline` / `months` / `weekdays` / `hours` | 时间分析 |
| `hotModels` | **按模型族合并**后取前 5。族键去掉 `provider:` 与 `org/` 前缀；多供应商时 `providerId` 为逗号连接（如 `deepseek, opencode-go`） |
| `models` | **不合并**提供商与模型，一行一个 `provider:model` |
| `providers` | 按原始 `providerId`（或 key 里 `:` 前缀）汇总；空 provider 不计入 |
| `agents` / `agentModels` | Agent 分析；热力图同样按模型族 |
| `topSessions` | Token 最高 10 条；`providerModel` 形如 `opencode-go/deepseek-v4-flash` |

时间窗由 `DateWindow` 在聚合快照上过滤，扫描本身不传 `Since`。UI：`all`（默认）、`7d`、`30d`、`custom:from:to`（`yyyy-MM-dd`）。API 另接受 `90d`。总览固定 `all`。Agent 分析不按数据源筛选（始终全源对比）。日时间线跨度超过 180 天时不返回 `timeline`（改看月统计）。

---

## 5. 多 Agent 适配

### 5.1 `IAgentDataSource`

| 成员 | 语义 |
|---|---|
| `AgentId` / `DisplayName` | 稳定 ID 与展示名 |
| `CanScan` | 当前实现四源均为 `true` |
| `Detect()` | 手动路径优先，否则按候选探测 |
| `SetRootPath(path)` | `null` 清除覆盖；校验只读可访问后写入配置 |
| `Scan(options)` | 流式产出记录；坏行计入 `Progress.Skipped` |
| `GetStatus()` | 路径与扫描计数 |

`ScanOptions`：`IncludeArchived`（默认 true）、可选 `Since`、`CancellationToken`。

错误：路径无效不崩溃；坏 JSONL / 坏 SQLite 行跳过。

### 5.2 路径候选

环境变量优先；手动路径覆盖并持久化到应用配置（非 Agent 目录）。

**OpenCode**（`opencode`）：`$XDG_DATA_HOME/opencode` → `~/.local/share/opencode`。有效：目录内或路径本身为可读 `opencode.db`。

**Codex**（`codex`）：`$CODEX_HOME` → `~/.codex`。有效：`sessions/`、`archived_sessions/`、`state_5.sqlite`、日期树下的 `rollout-*.jsonl`，或单个 `.jsonl`。

**Pi Agent**（`pi`）：`~/.pi/agent`。有效：`sessions/`、会话 jsonl 目录（跳过 `run-history.jsonl`），或单个 `.jsonl`。

**Claude Code**（`claude-code`）：`$CLAUDE_CONFIG_DIR` → `~/.claude`；Windows 另试 `%APPDATA%\CherryStudio\.claude`。有效：`projects/`（或根目录）下有 `.jsonl`，或单个 `.jsonl`。

### 5.3 扫描缓存

进程内按 `agentId + includeArchived + 数据根路径` 缓存 `AggregationSnapshot`。刷新或改路径时 `Invalidate`。不做文件监视，不做落盘指纹缓存。

多源扫描 `Parallel.ForEachAsync` 并行。

---

## 6. 安全与隐私

1. 仅 `127.0.0.1`；Kestrel 不绑 `0.0.0.0`。
2. SQLite：`Mode=ReadOnly`、`PRAGMA query_only=ON`；共享读、有限次 `BUSY` 重试。
3. 不读 `auth.json`、API key 等凭据。
4. 扫描不解析、不返回、不缓存 prompt / 回复全文。
5. 关于页声明：「数据仅本机处理，只读，不上传。」许可证 MIT。
6. 列表可将项目名/标题显示为 `******`（隐藏信息）。

---

## 7. 性能

- JSONL 逐行；SQLite 游标读取。
- 归约在扫描循环内完成。
- 前端绑定聚合数组；项目表分页（每页 20），展开行携带该页项目下的会话列表。
- 日志默认 `Warning` 以免刷屏。

---

## 8. 信息架构

### 8.1 主页 `/`

产品名、一句话说明、进入 Dashboard / 关于。数据源 Logo：探测到则彩色；未探测到灰色，点击指定路径。

### 8.2 Dashboard `/dashboard/*`

宽屏：顶栏品牌 + 左侧分析导航 + 主栏。  
窄屏（≤860px）：顶栏为菜单按钮、当前页名、刷新；分析导航收入左侧抽屉。

工具栏：数据源多选（Agent 分析页除外）、时间范围（总览除外：全部 / 近 7 天 / 近 30 天 / 自定义）。刷新先 `POST /api/stats/refresh`（返回 `all`），若当前不是全部再按范围重拉。

| 路由 | 页面 | 内容 |
|---|---|---|
| `/dashboard/overview` | 总览 | 指标卡（总量、输入、读缓存、输出、写缓存、推理、会话数、消息数）；近一年热力图（对齐周一，悬停图例钉在格上）；近 14 天；Top 7 天；热门模型（族合并）；高消耗会话（项目、标题、供应商/模型、时间、Token；可隐藏信息） |
| `/dashboard/agents` | Agent 分析 | 各源堆叠条 + 环图；Agent×模型热力图（无图例）；明细表 |
| `/dashboard/time` | 时间分析 | 日统计（有 `timeline` 时）；月统计（`all` 或无日线时）；星期；小时 |
| `/dashboard/models` | 模型分析 | 模型用量图（前端按 `modelId` 再汇总，取前 10）；供应商用量图（`providers`）；模型明细不合并供应商，可筛选 |
| `/dashboard/projects` | 项目分析 | 按 `cwd` 分组，每页 20；可按 Token / 时间排序；点击行展开会话（标题、供应商/模型、消息、Token、输入、输出、推理、缓存读/写） |

`/dashboard/sessions` 重定向到项目分析。`/dashboard/:agentId` 重定向到总览并带 `?agents=`。

空数据：说明去首页指定路径，不画空骨架图。

### 8.3 关于 `/about`

版本、隐私声明、MIT、支持的数据源说明。

---

## 9. 运行时与 API

| 项 | 约定 |
|---|---|
| 绑定 | `http://127.0.0.1:<port>` |
| 端口 | 优先 `17821`，占用则向后试 20 个 |
| 静态 | 嵌入 `wwwroot`；非 `/api` fallback 到 `index.html` |
| 浏览器 | 启动成功后用系统默认浏览器打开；可关闭 |
| 配置 | Windows：`%LOCALAPPDATA%\AgentTokenStats\settings.json`；Unix：`$XDG_CONFIG_HOME/agent-token-stats` 或 `~/.config/agent-token-stats` |
| 生命周期 | 退出进程即停；不注册服务 |

主要 API（camelCase JSON）：

| 方法 | 路径 | 作用 |
|---|---|---|
| GET | `/api/meta` | 版本、隐私、许可证 |
| GET | `/api/agents` | 探测结果 |
| PUT/DELETE | `/api/agents/{id}/path` | 手动路径 / 恢复自动探测 |
| GET | `/api/stats` | 合并 Dashboard；`agents`、`range` |
| POST | `/api/stats/refresh` | 失效缓存并重扫 |
| GET | `/api/stats/projects` | 项目分页（含 `sessions`） |
| GET | `/api/stats/sessions` | 会话分页（内部/兼容） |

仍保留单源 `/api/agents/{id}/dashboard` 等，前端主路径走合并 `/api/stats`。

---

## 10. 发布与仓库

- `PublishSingleFile` + `PublishTrimmed`（partial）+ 自包含。
- CI：push 构建 6 个 RID 工件 `ats_{windows|linux|macos}_{x64|arm64}_{sha7}`；tag 打草稿 Release zip `ats_{os}_{arch}_{tag}.zip`。
- 本机：`publish.ps1` / `publish.sh` 按当前 OS 与 CPU 发布到 `artifacts/<rid>/`（Linux musl 会打成 `linux-musl-*`，不在 CI 矩阵里）。
- 运行时需 .NET 10 构建环境与 Node.js 24（开发）；用户只跑二进制。
- 测试：`src/AgentTokenStats.Tests`，xUnit v3 + Microsoft.Testing.Platform（`global.json` 指定 runner）。

```text
AgentTokenStats/
  Design.md
  AgentSessionStore.md
  README.md
  LICENSE
  publish.ps1 / publish.sh
  global.json
  src/AgentTokenStats/          # 宿主、适配器、聚合、嵌入 wwwroot
  src/AgentTokenStats.Web/      # Vue
  src/AgentTokenStats.Tests/
  .github/workflows/ci.yml
```

---

## 11. OpenCode 对账 SQL

assistant 消息 `data` JSON（SQLite `json_extract`；缺失为 0）：

```sql
SELECT
  COUNT(*) AS message_count,
  SUM(COALESCE(json_extract(data, '$.tokens.input'), 0)) AS input_tokens,
  SUM(COALESCE(json_extract(data, '$.tokens.output'), 0)) AS output_tokens,
  SUM(COALESCE(json_extract(data, '$.tokens.reasoning'), 0)) AS reasoning_tokens,
  SUM(COALESCE(json_extract(data, '$.tokens.cache.read'), 0)) AS cache_read_tokens,
  SUM(COALESCE(json_extract(data, '$.tokens.cache.write'), 0)) AS cache_write_tokens
FROM message
WHERE json_extract(data, '$.role') = 'assistant';
```

产品 Summary 对应字段须与此一致（夹具测试）。
