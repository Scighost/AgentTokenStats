using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;
using Microsoft.Data.Sqlite;

namespace AgentTokenStats.Adapters;

public sealed class CodexDataSource : AgentDataSourceBase
{
    public const string Id = "codex";

    public CodexDataSource(AppConfigStore config) : base(config)
    {
    }

    public override string AgentId => Id;
    public override string DisplayName => "Codex";
    public override bool CanScan => true;

    protected override IEnumerable<string> EnumerateCandidates()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(home))
            yield return PathUtil.Expand(home);
        yield return Path.Combine(PathUtil.UserHome(), ".codex");
    }

    protected override bool IsValidRoot(string path, out string? error)
    {
        if (File.Exists(path) && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return true;
        }

        if (!DirectoryReadable(path, out error))
            return false;

        if (Directory.Exists(Path.Combine(path, "sessions"))
            || File.Exists(Path.Combine(path, "state_5.sqlite"))
            || Directory.Exists(Path.Combine(path, "archived_sessions"))
            || LooksLikeSessionsTree(path))
        {
            error = null;
            return true;
        }

        error = "未找到 sessions/、archived_sessions/ 或 state_5.sqlite";
        return false;
    }

    public override async IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options)
    {
        var detection = Detect();
        if (!detection.Found || string.IsNullOrWhiteSpace(detection.ResolvedPath))
            yield break;

        var root = detection.ResolvedPath;
        var index = LoadThreadIndex(root);
        foreach (var (path, archivedDir) in EnumerateRollouts(root))
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            await foreach (var record in ScanFileAsync(path, archivedDir, index, options).ConfigureAwait(false))
            {
                if (!options.IncludeArchived && record.IsArchived)
                    continue;
                if (options.Since is { } since && record.Timestamp < since)
                    continue;
                options.Progress.Records++;
                yield return record;
            }
        }
    }

    internal static IEnumerable<(string Path, bool ArchivedDir)> EnumerateRollouts(string root)
    {
        if (File.Exists(root) && root.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            yield return (root, false);
            yield break;
        }

        var sessions = Path.Combine(root, "sessions");
        var archived = Path.Combine(root, "archived_sessions");
        var sessionRoot = Directory.Exists(sessions) ? sessions : root;
        foreach (var file in JsonlScan.EnumerateFiles(sessionRoot, "rollout-*.jsonl"))
            yield return (file, false);
        if (Directory.Exists(archived))
        {
            foreach (var file in JsonlScan.EnumerateFiles(archived, "rollout-*.jsonl"))
                yield return (file, true);
        }
    }

    internal static Dictionary<string, ThreadMeta> LoadThreadIndex(string root)
    {
        var result = new Dictionary<string, ThreadMeta>(StringComparer.OrdinalIgnoreCase);
        var db = Path.Combine(root, "state_5.sqlite");
        if (File.Exists(db))
        {
            try
            {
                using var conn = SqliteReadonly.Open(db);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT id, title, cwd, archived, tokens_used, model, model_provider
                    FROM threads
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (string.IsNullOrEmpty(id))
                        continue;
                    result[id] = new ThreadMeta(
                        id,
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        !reader.IsDBNull(3) && Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture) != 0,
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(4) ? null : Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture));
                }
            }
            catch (SqliteException)
            {
                /* missing table or locked after retries — index is optional */
            }
        }

        var indexFile = Path.Combine(root, "session_index.jsonl");
        if (!File.Exists(indexFile))
            return result;

        foreach (var line in File.ReadLines(indexFile))
        {
            if (!JsonUtil.TryParse(line, out var doc))
                continue;
            using (doc)
            {
                var id = JsonUtil.GetString(doc.RootElement, "id");
                if (string.IsNullOrEmpty(id))
                    continue;
                var title = JsonUtil.GetString(doc.RootElement, "thread_name", "title");
                if (!result.TryGetValue(id, out var existing))
                {
                    result[id] = new ThreadMeta(id, title, null, false, null, null, null);
                    continue;
                }

                if (string.IsNullOrEmpty(existing.Title) && !string.IsNullOrEmpty(title))
                    result[id] = existing with { Title = title };
            }
        }

        return result;
    }

    private static async IAsyncEnumerable<UnifiedUsageRecord> ScanFileAsync(
        string path,
        bool archivedDir,
        IReadOnlyDictionary<string, ThreadMeta> index,
        ScanOptions options)
    {
        var fileId = SessionIdFromFileName(path);
        var state = new RolloutState { SessionId = fileId };
        if (!string.IsNullOrEmpty(fileId) && index.TryGetValue(fileId, out var seeded))
            ApplyThread(state, seeded);

        var parser = new RolloutParser(state, archivedDir);
        await foreach (var item in JsonlScan.ParseLinesAsync(path, parser, options.CancellationToken)
            .ConfigureAwait(false))
        {
            if (item.Invalid || item.Skipped)
                options.Progress.Skipped++;
            if (item.Value is not null)
                yield return item.Value;
        }
    }

    internal static (UnifiedUsageRecord? Record, bool Skipped) TryConsumeEvent(
        ReadOnlySpan<byte> utf8,
        RolloutState state,
        bool archivedDir)
    {
        if (!Utf8JsonWalk.TryStartObject(utf8, out var reader))
            return (null, true);

        string? type = null;
        var timestamp = DateTimeOffset.UnixEpoch;
        PayloadFields? payload = null;

        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "type"u8) || Utf8JsonWalk.NameEquals(prop, "kind"u8))
            {
                type = Utf8JsonWalk.ReadString(ref reader);
                if (!IsTrackedType(type))
                    return (null, string.IsNullOrEmpty(type));
            }
            else if (Utf8JsonWalk.NameEquals(prop, "timestamp"u8))
                timestamp = Utf8JsonWalk.ReadTimestamp(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "payload"u8))
            {
                if (type is not null && !IsTrackedType(type))
                    Utf8JsonWalk.SkipValue(ref reader);
                else if (Utf8JsonWalk.TryEnterObject(ref reader))
                    payload = ReadPayload(ref reader);
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        if (string.IsNullOrEmpty(type))
            return (null, true);
        if (!IsTrackedType(type))
            return (null, false);

        payload ??= ReadRootAsPayload(utf8);

        if (type.Equals("session_meta", StringComparison.OrdinalIgnoreCase))
        {
            ApplySessionMeta(state, payload.Value, timestamp);
            return (null, false);
        }

        if (type.Equals("turn_context", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(payload.Value.Model))
                state.ModelId = payload.Value.Model;
            if (!string.IsNullOrEmpty(payload.Value.Cwd))
                state.Cwd = payload.Value.Cwd;
            return (null, false);
        }

        if (!string.Equals(payload.Value.InnerType, "token_count", StringComparison.OrdinalIgnoreCase))
            return (null, false);

        return ConsumeTokenCount(payload.Value, timestamp, state, archivedDir);
    }

    private static bool IsTrackedType(string? type) =>
        type is not null && (
            type.Equals("session_meta", StringComparison.OrdinalIgnoreCase)
            || type.Equals("turn_context", StringComparison.OrdinalIgnoreCase)
            || type.Equals("event_msg", StringComparison.OrdinalIgnoreCase));

    private static PayloadFields ReadRootAsPayload(ReadOnlySpan<byte> utf8)
    {
        if (!Utf8JsonWalk.TryStartObject(utf8, out var reader))
            return default;
        return ReadPayload(ref reader);
    }

    private static PayloadFields ReadPayload(ref Utf8JsonReader reader)
    {
        var fields = new PayloadFields();
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "type"u8))
                fields.InnerType = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "session_id"u8))
                fields.SessionId = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "id"u8))
            {
                var id = Utf8JsonWalk.ReadString(ref reader);
                if (string.IsNullOrEmpty(fields.SessionId))
                    fields.SessionId = id;
            }
            else if (Utf8JsonWalk.NameEquals(prop, "cwd"u8))
                fields.Cwd = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "model_provider"u8))
                fields.ModelProvider = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "provider"u8))
                fields.Provider = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "forked_from_id"u8))
                fields.ForkedFromId = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "parent_thread_id"u8))
                fields.ParentThreadId = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "model"u8))
                fields.Model = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "info"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                    ReadInfo(ref reader, ref fields);
            }
            else if (Utf8JsonWalk.NameEquals(prop, "input_tokens"u8) || Utf8JsonWalk.NameEquals(prop, "input"u8)
                     || Utf8JsonWalk.NameEquals(prop, "output_tokens"u8) || Utf8JsonWalk.NameEquals(prop, "output"u8)
                     || Utf8JsonWalk.NameEquals(prop, "reasoning_output_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "reasoning_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "reasoning"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cached_input_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cache_read_input_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cached"u8))
            {
                ReadUsageField(ref reader, ref fields.Direct);
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        return fields;
    }

    private static void ReadInfo(ref Utf8JsonReader reader, ref PayloadFields fields)
    {
        fields.HasInfo = true;
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "total_token_usage"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                {
                    fields.Total = ReadBucket(ref reader);
                    fields.HasTotal = true;
                }
            }
            else if (Utf8JsonWalk.NameEquals(prop, "last_token_usage"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                {
                    fields.Last = ReadBucket(ref reader);
                    fields.HasLast = true;
                }
            }
            else if (Utf8JsonWalk.NameEquals(prop, "input_tokens"u8) || Utf8JsonWalk.NameEquals(prop, "input"u8)
                     || Utf8JsonWalk.NameEquals(prop, "output_tokens"u8) || Utf8JsonWalk.NameEquals(prop, "output"u8)
                     || Utf8JsonWalk.NameEquals(prop, "reasoning_output_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "reasoning_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "reasoning"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cached_input_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cache_read_input_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cached"u8))
            {
                ReadUsageField(ref reader, ref fields.InfoDirect);
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }
    }

    private static TokenUsage ReadBucket(ref Utf8JsonReader reader)
    {
        var usage = new TokenUsage();
        while (Utf8JsonWalk.NextProperty(ref reader, out _))
            ReadUsageField(ref reader, ref usage);
        return usage;
    }

    private static void ReadUsageField(ref Utf8JsonReader reader, ref TokenUsage usage)
    {
        var name = reader.GetString();
        if (name is "input_tokens" or "input")
            usage.Input = Utf8JsonWalk.ReadInt64(ref reader);
        else if (name is "output_tokens" or "output")
            usage.Output = Utf8JsonWalk.ReadInt64(ref reader);
        else if (name is "reasoning_output_tokens" or "reasoning_tokens" or "reasoning")
            usage.Reasoning = Utf8JsonWalk.ReadInt64(ref reader);
        else if (name is "cached_input_tokens" or "cache_read_input_tokens" or "cached")
            usage.Cached = Utf8JsonWalk.ReadInt64(ref reader);
        else
            Utf8JsonWalk.SkipValue(ref reader);
    }

    private static (UnifiedUsageRecord? Record, bool Skipped) ConsumeTokenCount(
        PayloadFields payload,
        DateTimeOffset timestamp,
        RolloutState state,
        bool archivedDir)
    {
        if (!TryReadUsage(payload, out var current))
            return (null, false);

        TokenUsage usage;
        if (current.HasTotal)
        {
            var delta = current.Subtract(state.PrevTotal);
            state.PrevTotal = current.WithTotalOnly();
            if (delta.IsEmpty)
                return (null, false);
            usage = delta;
        }
        else
        {
            if (current.Equals(state.PrevLast))
                return (null, false);
            state.PrevLast = current;
            usage = current;
        }

        if (state.Forked && timestamp - state.SessionStarted <= TimeSpan.FromSeconds(5))
            return (null, false);

        var mapped = MapUsage(usage);
        if (mapped.IsEmpty)
            return (null, false);

        var modelId = state.ModelId ?? "";
        var providerId = state.ProviderId;
        return (new UnifiedUsageRecord
        {
            AgentId = Id,
            SessionId = string.IsNullOrEmpty(state.SessionId) ? "(unknown)" : state.SessionId,
            Timestamp = timestamp == DateTimeOffset.UnixEpoch ? DateTimeOffset.UtcNow : timestamp,
            ModelId = modelId,
            ProviderId = providerId,
            NormalizedModelKey = ModelKey.Normalize(providerId, modelId),
            InputTokens = mapped.Input,
            OutputTokens = mapped.Output,
            ReasoningTokens = mapped.Reasoning,
            CacheReadTokens = mapped.CacheRead,
            CacheWriteTokens = 0,
            MessageCount = 1,
            Cwd = state.Cwd,
            Title = state.Title,
            IsArchived = archivedDir || state.Archived
        }, false);
    }

    private static bool TryReadUsage(PayloadFields payload, out TokenUsage usage)
    {
        usage = default;
        if (payload.HasInfo)
        {
            if (payload.HasTotal)
            {
                usage = payload.Total with { HasTotal = true };
                if (!usage.IsEmpty)
                    return true;
            }

            if (payload.HasLast)
            {
                usage = payload.Last;
                return !usage.IsEmpty;
            }

            usage = payload.InfoDirect;
            return !usage.IsEmpty;
        }

        usage = payload.Direct;
        return !usage.IsEmpty;
    }

    internal static MappedUsage MapUsage(TokenUsage usage)
    {
        var cacheRead = Math.Max(0, usage.Cached);
        var input = usage.Input - cacheRead;
        if (input < 0)
            input = 0;
        return new MappedUsage(input, Math.Max(0, usage.Output), Math.Max(0, usage.Reasoning), cacheRead);
    }

    private static void ApplySessionMeta(RolloutState state, PayloadFields payload, DateTimeOffset timestamp)
    {
        if (!string.IsNullOrEmpty(payload.SessionId))
            state.SessionId = payload.SessionId;
        if (!string.IsNullOrEmpty(payload.Cwd))
            state.Cwd = payload.Cwd;
        var provider = payload.ModelProvider ?? payload.Provider;
        if (!string.IsNullOrEmpty(provider))
            state.ProviderId = provider;
        state.Forked = !string.IsNullOrEmpty(payload.ForkedFromId) || !string.IsNullOrEmpty(payload.ParentThreadId);
        if (timestamp != DateTimeOffset.UnixEpoch)
            state.SessionStarted = timestamp;
        if (!string.IsNullOrEmpty(payload.Model))
            state.ModelId = payload.Model;
    }

    private static void ApplyThread(RolloutState state, ThreadMeta meta)
    {
        state.SessionId = meta.Id;
        state.Title = meta.Title;
        if (!string.IsNullOrEmpty(meta.Cwd))
            state.Cwd = meta.Cwd;
        state.Archived = meta.Archived;
        if (!string.IsNullOrEmpty(meta.Model))
            state.ModelId = meta.Model;
        if (!string.IsNullOrEmpty(meta.Provider))
            state.ProviderId = meta.Provider;
    }

    private static readonly Regex UuidTail = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static string SessionIdFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var match = UuidTail.Match(name);
        return match.Success ? match.Value : name;
    }

    private static bool LooksLikeSessionsTree(string path)
    {
        try
        {
            if (Directory.EnumerateFiles(path, "rollout-*.jsonl").Any())
                return true;
            foreach (var year in Directory.EnumerateDirectories(path))
            foreach (var month in Directory.EnumerateDirectories(year))
            foreach (var day in Directory.EnumerateDirectories(month))
            {
                if (Directory.EnumerateFiles(day, "rollout-*.jsonl").Any())
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal sealed class RolloutState
    {
        public string SessionId { get; set; } = "";
        public string? Title { get; set; }
        public string? Cwd { get; set; }
        public string? ModelId { get; set; }
        public string? ProviderId { get; set; }
        public bool Archived { get; set; }
        public bool Forked { get; set; }
        public DateTimeOffset SessionStarted { get; set; } = DateTimeOffset.UnixEpoch;
        public TokenUsage PrevTotal { get; set; }
        public TokenUsage PrevLast { get; set; }
    }

    internal readonly record struct ThreadMeta(
        string Id,
        string? Title,
        string? Cwd,
        bool Archived,
        string? Model,
        string? Provider,
        long? TokensUsed);

    internal record struct TokenUsage(long Input, long Output, long Reasoning, long Cached, bool HasTotal = false)
    {
        public bool IsEmpty => Input == 0 && Output == 0 && Reasoning == 0 && Cached == 0;

        public TokenUsage Subtract(TokenUsage prev) => new(
            Math.Max(0, Input - prev.Input),
            Math.Max(0, Output - prev.Output),
            Math.Max(0, Reasoning - prev.Reasoning),
            Math.Max(0, Cached - prev.Cached),
            HasTotal: true);

        public TokenUsage WithTotalOnly() => this with { HasTotal = true };
    }

    internal readonly record struct MappedUsage(long Input, long Output, long Reasoning, long CacheRead)
    {
        public bool IsEmpty => Input == 0 && Output == 0 && Reasoning == 0 && CacheRead == 0;
    }

    private struct PayloadFields
    {
        public string? InnerType;
        public string? SessionId;
        public string? Cwd;
        public string? ModelProvider;
        public string? Provider;
        public string? ForkedFromId;
        public string? ParentThreadId;
        public string? Model;
        public bool HasInfo;
        public bool HasTotal;
        public bool HasLast;
        public TokenUsage Total;
        public TokenUsage Last;
        public TokenUsage InfoDirect;
        public TokenUsage Direct;
    }

    private sealed class RolloutParser : IUtf8LineParser<UnifiedUsageRecord>
    {
        private readonly RolloutState _state;
        private readonly bool _archivedDir;

        public RolloutParser(RolloutState state, bool archivedDir)
        {
            _state = state;
            _archivedDir = archivedDir;
        }

        public UnifiedUsageRecord? Parse(ReadOnlySpan<byte> utf8, out bool invalid, out bool skipped)
        {
            invalid = false;
            var parsed = TryConsumeEvent(utf8, _state, _archivedDir);
            skipped = parsed.Skipped;
            return parsed.Record;
        }
    }
}
