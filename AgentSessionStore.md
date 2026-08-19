# opencode / codex / pi agent / Claude Code / Cursor 本地会话存储调研报告

> 调研日期：2026-08-11（对照官方文档与默认数据目录格式；Cursor 补充于 2026-08-18）
> 对照版本量级：opencode 1.18.x、codex-cli 0.122.x / Codex Desktop、pi（@earendil-works/pi-coding-agent 0.83.x）、Claude Code SDK 2.1.x
> 路径一律写默认位置与环境变量，示例 JSON / 目录名不含真实用户目录、会话标题或正文。

---

## 1. 概览对比

| 维度 | opencode | codex | pi agent | Claude Code |
|---|---|---|---|---|
| 数据根目录 | `~/.local/share/opencode`（`XDG_DATA_HOME`） | `~/.codex`（`CODEX_HOME`，默认） | `~/.pi/agent` | `~/.claude`（可用 `CLAUDE_CONFIG_DIR` 重定向） |
| 会话存储介质 | **SQLite 数据库**（`opencode.db`） | **JSONL 文件**（`sessions/YYYY/MM/DD/rollout-*.jsonl`）+ 索引 SQLite（`state_5.sqlite`） | **JSONL 文件**（`sessions/--<cwd>--/<ts>_<uuid>.jsonl`） | **JSONL 文件**（`projects/<cwd编码>/<uuid>.jsonl`） |
| 会话组织方式 | 按 project（worktree 根）分组，`session` 表 + 外键 | 按日期目录分片，每条会话一个 rollout 文件 | 按工作目录分目录，每条会话一个文件 | 按工作目录编码分目录，每条会话一个文件 |
| 消息存储 | `message` + `part` 两张表（JSON 存 `data` 列） | rollout JSONL 中的 `response_item` 事件流 | JSONL 中 `message` 条目（含 `message` 对象） | JSONL entry（user/assistant/progress 等，含 `message` 对象） |
| 消息关系 | 线性 + `parentID`（branch 支持） | 线性（rollout 内按时间顺序），`turn_id` 关联 | **树形**：每条 entry 有 `id`/`parentId`，支持就地分支 | 线性链 `parentUuid` + `isSidechain` 旁路；子代理独立文件 |
| 时间戳 | Unix 毫秒（INTEGER） | ISO8601 UTC 字符串 + 部分 Unix 秒 | ISO8601 UTC 字符串（entry）+ Unix 毫秒（message 内部） | ISO8601 UTC 字符串 |
| 用户输入 | `session_input` 表（prompt/delivery 队列） | `event_msg`(user_message) 事件 | `message` 条目（role=user） | `user` entry + `queue-operation` 入队记录 + 文件末 `last-prompt` |
| 其它数据 | snapshot/、tool-output/、token_export/、log/、repos/ | auth.json、config.toml、models.json、memories/、logs_*.sqlite、goals_*.sqlite、pets/、skills/、plugins/ 等 | auth.json、settings.json、models-store.json、run-history.jsonl、keybindings.json、bin/、npm/ | .claude.json + backups/、plans/、shell-snapshots/、plugins/、telemetry/、subagents/ |

---

## 2. opencode（SQLite 方案）

### 2.1 目录结构（默认 `~/.local/share/opencode`）

```
opencode/
├── opencode.db              # 主数据库（SQLite，含 -wal/-shm）
├── account.json             # 账户信息
├── auth.json                # 凭据（API keys 等）
├── log/                     # 日志（opencode.log 及历史 .log）
├── repos/                   # 仓库管理
├── snapshot/                # 文件快照（按会话 id 目录 + global/，内容为文件哈希）
├── storage/
│   ├── migration/           # 迁移记录
│   └── session_diff/        # 会话差异
├── token_export/            # token 导出
└── tool-output/             # 工具输出（tool_<id> 文件）
```

### 2.2 数据库表（`.tables`）

```
__drizzle_migrations  account_state        credential        event_sequence   message        session_share   todo
account               control_account      data_migration    event            migration      project         workspace
                                                                               part           session_input   session_message
```

### 2.3 核心表结构

**session**（会话主表）
```sql
id TEXT PRIMARY KEY,            -- "ses_xxxxxxxx" 格式
project_id TEXT NOT NULL,       -- 所属项目（"global" 表示未关联项目）
parent_id TEXT,                 -- 分支父会话
slug TEXT NOT NULL,             -- 短名（如 glowing-island）
directory TEXT NOT NULL,        -- 工作目录
title TEXT NOT NULL,            -- 会话标题（由第一条消息生成）
version TEXT NOT NULL,          -- 会话格式版本
share_url TEXT,                 -- 分享链接
summary_additions/deletions/files INTEGER,  -- 变更统计
summary_diffs TEXT,             -- 差异摘要
revert TEXT,
permission TEXT,
time_created / time_updated / time_compacting / time_archived INTEGER,  -- Unix 毫秒
workspace_id TEXT, path TEXT, agent TEXT,   -- agent 名（build/plan 等）
model TEXT,                     -- {"id":..., "providerID":..., "variant":...}
cost REAL,                      -- 累计费用
tokens_input/output/reasoning/cache_read/cache_write INTEGER,  -- token 统计
metadata TEXT
```

**message**（消息）
```sql
id TEXT PRIMARY KEY,
session_id TEXT NOT NULL,
time_created / time_updated INTEGER,
data TEXT NOT NULL              -- JSON
```
`data` 示例（最新格式）：
```json
{"parentID":"msg_xxx","role":"assistant","mode":"build","agent":"build","variant":"max",
 "path":{"cwd":"/path/to/project","root":"/"},
 "cost":0,"tokens":{"input":0,"output":0,"reasoning":0,"cache":{"read":0,"write":0}},
 "modelID":"deepseek-v4-flash","providerID":"opencode-go","time":{"created":1700000000000}}
```

**part**（消息内容块，1 条消息 → N 条 part）
```sql
id TEXT PRIMARY KEY,
message_id TEXT NOT NULL,   -- 外键 → message
session_id TEXT NOT NULL,
time_created / time_updated INTEGER,
data TEXT NOT NULL          -- JSON
```
常见 `data.type`：
| type | 说明 |
|---|---|
| tool | 工具调用，含输入/输出/元数据 |
| step-start | step 开始（含 snapshot 哈希） |
| step-finish | step 结束 |
| reasoning | 推理内容 |
| text | 文本 |
| patch | 文件补丁 |
| file | 文件内容 |
| compaction | 上下文压缩 |
| agent | agent 切换记录 |

各类型结构示例：
```json
// tool
{"type":"tool","tool":"write","callID":"call_xxx",
 "state":{"status":"completed",
   "input":{...}, "output":"Wrote file successfully.",
   "metadata":{"diagnostics":{},"filepath":"...","exists":true,"truncated":false},
   "title":"readme.md","time":{"start":1700000000000,"end":1700000001000}}}
// step-start
{"snapshot":"62a3be828c5d56ea09a756f3cf5670fc4de63fe5","type":"step-start"}
```

**project**（项目）
```sql
id TEXT PRIMARY KEY, worktree TEXT NOT NULL, vcs TEXT, name TEXT,
icon_url TEXT, icon_color TEXT,
time_created / time_updated / time_initialized INTEGER,
sandboxes TEXT NOT NULL, commands TEXT, icon_url_override TEXT
```

**session_input**（用户输入队列）
```sql
id TEXT PRIMARY KEY, session_id TEXT NOT NULL, prompt TEXT NOT NULL,
delivery TEXT NOT NULL, admitted_seq INTEGER NOT NULL, promoted_seq INTEGER,
time_created INTEGER NOT NULL
```

**event**（事件溯源 / 审计日志，聚合根模式）
```sql
id TEXT PRIMARY KEY, aggregate_id TEXT NOT NULL, seq INTEGER NOT NULL,
type TEXT NOT NULL, data TEXT NOT NULL
```
常见事件类型：`message.part.updated.1`、`message.updated.1`、`session.updated.1`、`message.removed.1`、`session.created.1`、`session.next.agent.switched.1`、`session.next.model.switched.1`

**workspace**：`id, type, name, branch, directory, extra, project_id, time_used`
**todo**：`session_id, content, status, priority, position, time_created, time_updated`
**session_message**：`id, session_id, type, time_created, time_updated, data, seq`（会话消息队列）

### 2.4 关键点
- 会话、消息、part 全部存 SQLite，删除会话直接级联删除（外键 ON DELETE CASCADE）。
- `snapshot/` 按 session id 存文件快照（git 式内容寻址），支撑 /undo 回滚。
- 消息是「消息 + part 分块」模型，每个 part 是独立 JSON 行，方便流式追加。
- `event` 表是事件溯源日志，可重放/审计。

---

## 3. codex（JSONL rollout 方案）

### 3.1 目录结构（默认 `~/.codex`）

```
.codex/
├── sessions/                     # 会话目录：YYYY/MM/DD/rollout-*.jsonl
├── state_5.sqlite                # 会话索引（threads 表）
├── session_index.jsonl           # 线程标题/更新时间索引
├── .codex-global-state.json      # 桌面端全局状态（UI、项目、权限）
├── auth.json                     # 登录凭据
├── config.toml                   # 主配置（模型、provider、插件）
├── models.json                   # 模型目录
├── installation_id / cap_sid     # 安装标识
├── logs_2.sqlite / goals_1.sqlite / memories_1.sqlite   # 日志/目标/记忆
├── memories/                     # 记忆（markdown）
├── skills/  plugins/  pets/  computer-use/  visualizations/
├── process_manager/  thread-writer-locks/  tmp/  .tmp/
└── sqlite/  sandbox/  .sandbox/  .sandbox-bin/  .sandbox-secrets/
```

### 3.2 会话文件：`sessions/YYYY/MM/DD/rollout-<时间戳>-<session_id>.jsonl`

文件命名示例：`rollout-2026-07-27T20-04-42-<session-uuid>.jsonl`
每行一个 JSON 事件，顶层字段：`timestamp`（ISO8601 UTC）、`type`、`payload`。

**顶层 type**：
| type | 说明 |
|---|---|
| response_item | 模型响应条目（核心内容） |
| event_msg | 运行事件（task 开始/结束、token 计数、MCP 等） |
| session_meta | 会话元数据（文件第一条） |
| world_state | 世界状态快照（全量/增量） |
| turn_context | 每轮上下文（cwd、沙箱、权限、模型） |

**session_meta（第一条）关键字段**：
```json
{"timestamp":"...","type":"session_meta","payload":{
  "session_id":"<uuid>","id":"<uuid>","timestamp":"...",
  "cwd":"/path/to/project","originator":"Codex Desktop","cli_version":"0.146.0-alpha.3.1",
  "source":"vscode","thread_source":"user","model_provider":"openai",
  "base_instructions":{"text":"..."},
  "dynamic_tools":[{...namespace/function 列表...}]
}}
```

**response_item.payload.type**：
| type | 说明 | 关键字段 |
|---|---|---|
| message | 消息 | `id`(msg_xxx), `role`(developer/user/assistant), `content[]`(input_text/output_text 等), `internal_chat_message_metadata_passthrough` |
| function_call | 工具调用 | `id`(fc_xxx), `name`, `arguments`(JSON 字符串), `call_id`(call_xxx) |
| function_call_output | 工具输出 | `id`(fco_xxx), `call_id`, `output`(含 exit code/耗时) |
| reasoning | 推理 | `id`(rs_xxx), `summary[]`, `content[]`(reasoning_text), `encrypted_content` |

**event_msg.payload.type**：`task_started`（含 turn_id、model_context_window、collaboration_mode_kind）、`user_message`、`agent_message`、`token_count`（流式 token 统计）、`mcp_tool_call_end`、`thread_settings_applied`

**turn_context（每轮上下文）字段**：`turn_id`、`cwd`、`workspace_roots[]`、`current_date`、`timezone`、`approval_policy`、`approvals_reviewer`、`sandbox_policy{type, writable_roots, network_access}`、`permission_profile`、`file_system_sandbox_policy`、`model`、`personality`、`collaboration_mode{mode, settings{model, reasoning_effort, developer_instructions}}`、`multi_agent_version`、`realtime_active`、`effort`、`summary`

**world_state（世界状态）字段**：`full`(bool，全量/增量)、`state{agents_md, environments{environments{local{cwd,status,shell}}, current_date, timezone, filesystem}, git_attribution, host_skills, permissions, plugins_instructions, realtime, skills}`

### 3.3 索引与状态库

**state_5.sqlite `threads` 表**（会话索引）：
```sql
id TEXT PRIMARY KEY,            -- 会话 UUID（与 rollout 文件名一致）
rollout_path TEXT NOT NULL,     -- 指向 JSONL 文件（核心）
created_at / updated_at INTEGER,
source TEXT, model_provider TEXT, cwd TEXT, title TEXT,
sandbox_policy TEXT, approval_mode TEXT,
tokens_used INTEGER, has_user_event INTEGER,
archived / archived_at, git_sha, git_branch, git_origin_url,
cli_version, first_user_message, agent_nickname, agent_role,
memory_mode, model, reasoning_effort, agent_path,
created_at_ms / updated_at_ms, thread_source, preview,
recency_at / recency_at_ms, history_mode, name, is_pinned,
thread_section_id, section_position, section_entered_at_ms
```
另有：`thread_sections(id, name)`、`thread_dynamic_tools`、`thread_spawn_edges`（agent 派生关系）、`remote_control_enrollments`、`external_agent_config_imports`、`backfill_state`

**session_index.jsonl**（每行）：
```json
{"id":"<uuid>","thread_name":"<session title>","updated_at":"2026-07-27T12:05:49.0125264Z"}
```

### 3.4 关键点
- **双写模式**：正文存 JSONL（便于流式追加、增量同步），索引存 SQLite（列表查询、标题、归档、置顶）。
- rollout 是「事件流」而非「消息列表」——token_count、world_state、turn_context 等元事件与内容事件混排，按时间戳顺序回放。
- token 统计在 `event_msg(token_count)` 中按 turn 流式累加，最终汇总进 `threads.tokens_used`。
- Desktop 端还会写 `.codex-global-state.json`（UI 状态、thread→project 映射、权限缓存）。

---

## 4. pi agent（JSONL 树形会话方案）

### 4.1 目录结构（默认 `~/.pi/agent`）

```
.pi/agent/
├── sessions/                     # 会话：--<cwd编码>--/<timestamp>_<uuid>.jsonl
├── auth.json                     # 凭据
├── settings.json                 # 全局设置（provider/model/theme/包）
├── models-store.json             # 各 provider 的模型列表
├── alibaba-config.json / alibaba-cloud-models.cache.json
├── run-history.jsonl             # 子任务运行历史（worker agent）
├── keybindings.json / trust.json
├── bin/  npm/                    # 运行时
```

### 4.2 会话文件

路径：`~/.pi/agent/sessions/--<path>--/<timestamp>_<uuid>.jsonl`
- `<path>` 为工作目录，`/` 替换为 `-`，如 `--home-user-project--`
- 文件名 = 创建时间（ISO8601 UTC）+ `_` + UUID，如 `2026-08-02T04-03-09-649Z_<uuid>.jsonl`

每行一个 entry，统一基础结构（`SessionEntryBase`）：
```typescript
interface SessionEntryBase {
  type: string;
  id: string;                     // 8 位十六进制（树节点 ID）
  parentId: string | null;        // 父节点 ID（首条为 null）
  timestamp: string;              // ISO8601 UTC
}
```
**树形结构**：所有条目通过 id/parentId 组成树，leaf 为当前会话位置，支持就地分支（/tree、/fork、/clone）不新建文件。版本 v3（v1 线性 → v2 树 → v3 hookMessage 改名 custom，自动迁移）。

### 4.3 Entry 类型（官方 session-format.md）

| type | 说明 | 额外字段 |
|---|---|---|
| session（首行，无 id/parentId） | 会话头 | `version:3, id(uuid), timestamp, cwd`，可选 `parentSession` |
| message | 消息 | `message: AgentMessage`（见下） |
| model_change | 中途换模型 | `provider, modelId` |
| thinking_level_change | 换思考等级 | `thinkingLevel`（low/high 等） |
| compaction | 上下文压缩 | `summary, firstKeptEntryId, tokensBefore, details?, fromHook?` |
| branch_summary | 分支摘要 | `fromId, summary, details?` |
| custom | 扩展状态（不进 LLM 上下文） | `customType, data` |
| custom_message | 扩展注入消息（进 LLM 上下文） | `customType, content, display, details?` |
| label | 书签 | `targetId, label` |
| session_info | 会话显示名 | `name` |

### 4.4 AgentMessage（message 内的 message 字段）

**Content 块类型**：`text{text}`、`image{data(base64), mimeType}`、`thinking{thinking}`、`toolCall{id, name, arguments}`

**UserMessage**：`role:"user"`, `content: string | (TextContent|ImageContent)[]`, `timestamp`(Unix ms)
**AssistantMessage**：`role:"assistant"`, `content: (TextContent|ThinkingContent|ToolCall)[]`, `api`, `provider`, `model`, `usage`, `stopReason("stop"|"length"|"toolUse"|"error"|"aborted")`, `errorMessage?`, `timestamp`, `responseId?`
**ToolResultMessage**：`role:"toolResult"`, `toolCallId`, `toolName`, `content`, `details?`, `isError`, `timestamp`

**Usage 结构**：
```typescript
interface Usage {
  input; output; cacheRead; cacheWrite; totalTokens: number;
  cost: { input; output; cacheRead; cacheWrite; total };
}
```

**扩展类型**：`bashExecution{command, output, exitCode, cancelled, truncated, fullOutputPath?, excludeFromContext?}`、`custom{customType, content, display, details?}`、`branchSummary{summary, fromId}`、`compactionSummary{summary, tokensBefore}`

assistant 消息字段示例：
```json
{"type":"message","id":"aaaaaaaa","parentId":"bbbbbbbb","timestamp":"2026-05-22T19:05:33.571Z",
 "message":{"role":"assistant",
   "content":[{"type":"thinking","thinking":"...","thinkingSignature":"reasoning_content"},
              {"type":"text","text":"..."},
              {"type":"toolCall","id":"call_00_...","name":"read","arguments":{"path":"..."}}],
   "api":"openai-completions","provider":"deepseek","model":"deepseek-v4-flash",
   "usage":{"input":933,"output":302,"cacheRead":768,"cacheWrite":0,"totalTokens":2003,
            "cost":{"input":0.0001306,"output":0,"cacheRead":0,"cacheWrite":0,"total":0.0001306}},
   "stopReason":"toolUse","timestamp":1700000000000,"responseId":"<uuid>"}}
```

### 4.5 关键点
- 纯 JSONL + 树形指针，无数据库；删除会话 = 删文件；备份 = 拷文件。
- 官方提供 `SessionManager` API（open/forkFrom/listAll 等）与完整 TypeScript 类型定义（源码 pi-mono：`packages/coding-agent/src/core/session-manager.ts`、`packages/ai/src/types.ts`）。
- `/export` 可导出 HTML，`/share` 上传 GitHub gist。
- `run-history.jsonl` 记录 worker 子任务：`{agent:"worker", task, ts, status:"ok"|"error", duration, exit?}`。

---

## 5. Claude Code（JSONL 会话文件方案）

> 默认目录为 `~/.claude`。部分桌面应用会通过 `CLAUDE_CONFIG_DIR` 把整棵目录指到应用数据目录（Windows 上例如 `%APPDATA%\<App>\.claude`）。以下为默认布局 + 官方格式规范。

### 5.1 标准存储位置（跨平台）

| 平台 | 路径 |
|---|---|
| macOS / Linux | `~/.claude/`（会话、配置、记忆等）+ `~/.claude.json`（全局状态） |
| Windows | `%USERPROFILE%\.claude\` + `%USERPROFILE%\.claude.json` |
| 可自定义 | 环境变量 `CLAUDE_CONFIG_DIR` 可整体改目录 |

### 5.2 目录结构（`~/.claude`）

```
.claude/
├── .claude.json                    # 全局状态（installMethod、用户ID、版本迁移标记等）
├── backups/.claude.json.backup.*   # 全局状态自动备份
├── projects/                       # ★ 会话存储（按项目目录分组）
│   └── <cwd-编码>/                 #   如 C--Users-<user>--Projects--demo
│       ├── <session-uuid>.jsonl    #   主会话（每行一条 entry）
│       ├── <session-uuid>/         #   会话附属目录
│       │   └── subagents/          #     子代理会话
│       │       ├── agent-<id>.jsonl
│       │       └── agent-<id>.meta.json   # {agentType, description}
│       └── memory/                 #   自动记忆目录
├── plans/                          # Plan 模式文档
├── plugins/blocklist.json          # 插件黑名单（fetchedAt + 列表）
├── shell-snapshots/                # bash 环境快照
└── telemetry/                      # 遥测
```

目录名编码规则：cwd 路径中 `\` `/` `:` 替换为 `-`；非 ASCII 字符编码方式随实现而异（官方 CLI 为百分号编码，部分宿主会把 CJK 段替换为连续 `-`）。

### 5.3 会话 JSONL 格式（核心）

每个项目目录下按会话 UUID 一个 `.jsonl` 文件。每行一条 entry，是**带 parentUuid 的树形/线性混合结构**（`parentUuid` 指向上一轮，`isSidechain` 标记旁路）。

**entry 公共字段**：
```json
{"parentUuid": null, "isSidechain": false, "type": "user",
 "uuid": "<uuid>", "timestamp": "2026-04-12T12:42:51.084Z",
 "cwd": "/path/to/project", "sessionId": "<session-uuid>",
 "version": "2.1.81", "gitBranch": "main",
 "userType": "external", "entrypoint": "sdk-ts"}
```

**entry type**：
| type | 说明 | 独有字段 |
|---|---|---|
| user | 用户消息 | `message{role:"user", content}`、`promptId`、`permissionMode` |
| assistant | 助手消息（含工具调用） | `message`（见下）、`slug`（会话名）、`promptId` |
| progress | 进程/hook 事件（子代理中常见） | `agentId`、`data{type, hookEvent, hookName, command, ...}`、`toolUseID` |
| queue-operation | 输入队列（入队/出队） | `operation`("enqueue"/...)、`content`、`sessionId` |
| last-prompt | 会话最后一条用户提示（文件末行） | `lastPrompt`、`sessionId` |
| summary | 上下文压缩摘要（官方格式） | `summary`(字符串) |
| system | 系统事件（官方格式） | `subtype`（init 等） |

**user entry 的 message**：`{"role":"user","content":"..."}`（content 也可能是数组：text/image/text_delta 块，用于多模态）。

**assistant entry 的 message**：
```json
{"id":"msg_xxx","type":"message","role":"assistant",
 "content":[{"type":"text","text":"..."},
            {"type":"tool_use","id":"call-xxx","name":"Read","input":{"file_path":"..."}}],
 "model":"claude-sonnet-4",
 "usage":{"input_tokens":1000,"cache_creation_input_tokens":0,
          "cache_read_input_tokens":200,"output_tokens":50,
          "server_tool_use":{"web_search_requests":0,"web_fetch_requests":0},
          "service_tier":"standard","cache_creation":{"ephemeral_1h_input_tokens":0,
          "ephemeral_5m_input_tokens":0},"inference_geo":"","iterations":[],"speed":"standard"},
 "stop_reason":"end_turn"}
```
- **content 块类型**：`text`、`thinking`（推理）、`tool_use{id, name, input}`、`tool_result{tool_use_id, content, is_error}`、`image`、`text_delta`（流式）
- **usage 字段**：input/output/cache_read/cache_creation 四类 token + server_tool_use（web 搜索计数）+ 缓存分级（ephemeral_1h/5m）
- **stop_reason**：`end_turn` / `tool_use` / `max_tokens` 等

### 5.4 子代理（Subagent）

- 位置：`projects/<cwd>/<session-id>/subagents/agent-<8hex>.jsonl`（在父会话的附属目录内，不混在主文件）
- entry 结构同主会话，额外字段：`agentId`、`slug`；多出 `progress` 类型记录 hook 进度（如 `PreToolUse:TodoWrite`）
- 元数据在 `agent-<id>.meta.json`：`{"agentType":"general-purpose","description":"..."}`

### 5.5 全局状态与配置

- **`~/.claude.json`**（含备份）：全局状态，标准字段含 `installMethod`、`hasCompletedOnboarding`、`permissions`（工具权限规则）、`projects`（cwd → 最近会话/历史/删除记录映射）、`customInstructions` 等；部分宿主会精简为启动时间、用户 ID、迁移标记。
- **`~/.claude/settings.json`**：全局设置（权限、输出风格、模型）。
- **项目级 `.claude/`**：`agents/`、`commands/`、`skills/` 三个扩展目录，存放项目专属 agent/斜杠命令/技能。

### 5.6 关键点
- 会话 = 一个 UUID 命名 JSONL 文件，JSONL 内每行含完整上下文（cwd、gitBranch、sessionId、version），**可直接 grep/脚本解析，无需数据库**。
- `last-prompt` 独立一行记录最后提示（用于会话列表预览），`queue-operation` 记录输入入队/出队时序。
- 子代理会话独立成文件并挂在父会话目录下，用 `.meta.json` 存身份描述。
- `parentUuid` + `isSidechain`：主线线性续接，侧链（tool 内部会话等）用 isSidechain=true 标记。
- 删除/备份会话 = 删除/拷贝 `.jsonl` 文件即可。

---

## 6. 四方对比总结

| 对比项 | opencode | codex | pi | claude code |
|---|---|---|---|---|
| 存储介质 | SQLite（单库集中） | JSONL + SQLite 索引（双写） | 纯 JSONL（分散文件） | 纯 JSONL（分散文件） |
| 会话文件 | 数据库表 session | rollout JSONL（按日期分片） | `<ts>_<uuid>.jsonl`（按 cwd 分目录） | `<session-uuid>.jsonl`（按 cwd 编码分目录） |
| 消息模型 | message + part 分块 | response_item 事件流 | 树形 entry + AgentMessage | entry（user/assistant/progress...）含 message 对象 |
| 分支支持 | 有（parent_id） | 弱（thread_spawn_edges） | 强（id/parentId 树，就地分支） | 中（parentUuid 链 + isSidechain，子代理独立文件） |
| 元数据 | 表字段 + JSON | JSONL 事件 + SQLite 列 | entry 字段 | entry 字段（cwd/gitBranch/sessionId 每行冗余） |
| token/费用统计 | session 表字段（cost + 5 类 token） | event_msg(token_count) + threads.tokens_used | usage + cost 对象 | message.usage（input/output/cache 分级 + web 调用） |
| 上下文压缩 | compaction part | 未见 | compaction entry | summary entry |
| 附加产物 | snapshot、tool-output、token_export | memories、goals、visualizations、thread 锁 | run-history、models-store | shell-snapshots、plans、backups、subagents、memory |
| 可移植性 | 需 sqlite 工具 | JSONL 可解析 | JSONL 可解析 | JSONL 可解析（最直观，每行自包含） |
| **归档/存档功能** | **有**（time_archived 标记） | **有**（archived + archived_at 标记） | **无** | **无** |

## 7. 附：归档（存档）功能

> 说明："存档"即 Archive——把会话移出默认列表但不删除。结论：**四个 agent 中只有 opencode 和 codex 有归档功能，且都是"原地打标记"，归档不改动任何数据内容、不移动任何文件。**

### 7.1 opencode —— 有归档，原地标记（时间戳）

- **触发方式**：TUI 命令面板 "Archive session"（二进制中 `session.archive` 命令存在于命令面板，含多语言条目）；SDK 侧通过 `session.update` 设置 `timeArchived` 属性。
- **存储变化**：`session` 表同一行内 `time_archived` 字段被写入 Unix 毫秒时间戳。**不新建表、不移动数据**。
- **数据保留**：已归档会话的 message / part 仍在同一库中，原样可读。
- `opencode session list` CLI 仍会列出已归档会话，TUI 会话选择器默认隐藏。
- **恢复**：清空 `time_archived` 即取消归档（SDK `session.update`）。
- **删除**：`opencode session delete <id>` 或 TUI 删除，级联删除 message/part（外键 CASCADE）。

### 7.2 codex —— 有归档，原地标记（标志位 + 时间戳）

- **触发方式**：桌面端线程右键/按钮归档；agent 可通过 `set_thread_archived` 工具归档；app-server 提供 `thread/archive` 端点（二进制中存在 `thread/archive`、`archived` 字符串）。
- **存储变化**：`state_5.sqlite` 的 `threads` 表同一行设置 `archived=1` 与 `archived_at`（Unix 秒）。rollout JSONL 正文文件**原封不动**。
- **查询影响**：SQLite 索引全部带 `WHERE archived=...` 过滤（`idx_threads_visible_*`、`idx_threads_pinned_recency_at_ms` 等），归档后从默认会话列表/索引中隐去，但数据仍在。
- **取消归档**：同端点再调（archived=0）。

### 7.3 pi agent —— 无归档功能

- 产品代码全量检索（dist + docs）**无任何会话归档实现**（命中的 "archive" 均为 tar/zip 工具包解压逻辑）。
- 会话管理仅两种：**删除**（`/resume` 选择器 Ctrl+D，有 `trash` CLI 时进回收站而非永久删除）或**手动删 `.jsonl` 文件**。
- 可选 `--no-session` 完全不落盘（ephemeral 模式），无中间态。

### 7.4 Claude Code —— 无归档功能

- 数据目录与产品文档中无会话 archive 机制。
- 会话永久保存在 `~/.claude/projects/**/*.jsonl`，`claude --resume` 选择器列出所有会话，无归档/隐藏机制。
- 上下文管理靠 `/clear`（清空当前上下文，**不删文件**）与 `/compact`（压缩摘要）；唯一"移除"方式是手动删除 JSONL 文件。

### 7.5 归档方式对比小结

| agent | 有无归档 | 归档落盘变化 | 内容是否受影响 | 恢复方式 |
|---|---|---|---|---|
| opencode | 有（TUI 命令面板） | `session.time_archived` 写毫秒时间戳 | 否（消息/part 原样） | 清空字段 |
| codex | 有（桌面端/工具/API） | `threads.archived`=1 + `archived_at` | 否（rollout 原封不动） | 置回 0 |
| pi | 无 | — | — | — |
| claude code | 无 | — | — | — |

> 共性结论：有归档功能的两个工具都采用**软删除式的原地标记**，归档会话与普通会话共用同一存储介质、同一表/文件、同一格式，唯一区别是增加一个时间戳或标志位字段；这也意味着归档只影响"列表可见性"，不影响磁盘占用与可解析性（仍可通过 DB 查询 / grep JSONL 找回）。

## 8. 附：默认路径

| 数据源 | 默认位置 |
|---|---|
| opencode | `$XDG_DATA_HOME/opencode/opencode.db`，否则 `~/.local/share/opencode/opencode.db` |
| codex | `$CODEX_HOME` 或 `~/.codex`（`sessions/YYYY/MM/DD/rollout-*.jsonl` + `state_5.sqlite`） |
| pi | `~/.pi/agent/sessions/--<cwd-encoding>--/*.jsonl` |
| Claude Code | `$CLAUDE_CONFIG_DIR` 或 `~/.claude`；Windows 上部分宿主为 `%APPDATA%\<App>\.claude` |
| Cursor | `~/.cursor`（`chats/`、`projects/`、`ai-tracking/`）；用量库另在 User `globalStorage/state.vscdb`（Windows：`%APPDATA%\Cursor\User\...`） |

> 存储格式以各产品当前版本与官方文档为准（pi：npm 包内 `docs/session-format.md`、`docs/sessions.md`）。Cursor **会话**以 `~/.cursor` 的 `agent-transcripts` JSONL 与 `chats/**/store.db` 为准；**用量**以 `state.vscdb` 的 `cursorDiskKV`（`composerData:` / `bubbleId:`）为准。统计适配器不读取消息正文。

## 9. Cursor（`.cursor` 会话 + SQLite KV 用量）

> 路径与表结构来自默认 `~/.cursor` / Cursor User 目录布局，以及公开的 Composer / `state.vscdb` 资料。适配器**只读 token / 模型 / 时间 / 会话元数据**，不把 prompt 或 assistant 正文纳入统计。当前 Agent Token Stats 发行版未接入 Cursor。

### 9.1 目录

默认数据根是 **`~/.cursor`**（可用 `CURSOR_AGENT_HOME` 覆盖）：

```
.cursor/
├── chats/           # store.db 消息流
├── projects/        # 含 agent-transcripts
├── ai-tracking/     # 代码追踪库
└── agents/ plugins/ skills-cursor/ ...
```

### 9.2 会话源（`.cursor`）

- **Agent transcripts**：`projects/<slug>/agent-transcripts/**/*.jsonl`。每行 JSON 含 `role`（`user`/`assistant`）或 `type`（如 `turn_ended`），通常无 token、无时间戳。适配器对 `role==assistant` 发一条记录（`messageCount=1`，tokens 为 0），`sessionId` 为文件名（不含扩展名），`cwd` 取 `projects/<slug>`，时间用文件 `LastWriteTimeUtc`。不解析 `message.content`。
- **Chat stores**：`chats/<hash>/<session-uuid>/store.db` + 旁路 `meta.json`。`meta` 表 key `"0"` 为 hex 编码 JSON（`agentId`、`name`、`createdAt`、`lastUsedModel`）。`blobs.data` 内嵌 `"role":"assistant"|"user"|"tool"`。适配器按字节搜索 `"role":"assistant"` 计数，不解码全文。`meta.json` 提供 `cwd`、`title`、`hasConversation`、毫秒时间戳。
- 同一 `sessionId` 同时存在 transcript 与 chat store 时：以 transcript 计消息，chat 的 title/model/cwd 覆盖到 transcript 记录，不再单独发一条 chat 会话记录。

### 9.3 用量源（`state.vscdb`）

用量库在 IDE User 目录的 `globalStorage/state.vscdb`（Windows：`%APPDATA%\Cursor\User`）。探测到 `~/.cursor` 后，适配器会同时打开该 vscdb（若 agent home 内已有 `state.vscdb` 则优先用本地文件）。

- 表：`cursorDiskKV(key, value)`，value 为 JSON。
- 会话：`composerData:{composerId}` → `name`、`createdAt`、`modelConfig.modelName`、`isArchived`、`promptTokenBreakdown.totalUsedTokens` / `contextTokensUsed`、`usageData.*.costInCents`。
- 消息：`bubbleId:{composerId}:{bubbleId}` → `type`（1=user，2/0=assistant）、`tokenCount.{inputTokens,outputTokens}`、`modelInfo.modelName`、`createdAt`。
- 新版本常见 per-bubble token 为 `{0,0}`；此时用 composer 上下文计量做会话级 input 补记，且不从文本长度估算。
- 归档：composer / 工作区 `allComposers[].isArchived` 原地标记。
- 与 `.cursor` 会话 ID 重叠时：vscdb 只叠加 token（`messageCount=0`），避免消息双计。不解析 `agentKv`、checkpoint 或消息正文。
