using System.Text.Json;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;

namespace AgentTokenStats.Adapters;

public sealed class PiDataSource : AgentDataSourceBase
{
    public const string Id = "pi";

    public PiDataSource(AppConfigStore config) : base(config)
    {
    }

    public override string AgentId => Id;
    public override string DisplayName => "Pi Agent";
    public override bool CanScan => true;

    protected override IEnumerable<string> EnumerateCandidates()
    {
        yield return Path.Combine(PathUtil.UserHome(), ".pi", "agent");
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

        if (Directory.Exists(Path.Combine(path, "sessions")) || LooksLikeSessionDir(path))
        {
            error = null;
            return true;
        }

        error = "未找到 sessions/ 目录";
        return false;
    }

    public override async IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options)
    {
        var detection = Detect();
        if (!detection.Found || string.IsNullOrWhiteSpace(detection.ResolvedPath))
            yield break;

        foreach (var file in EnumerateSessionFiles(detection.ResolvedPath))
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var parser = new LineParser(options.Since, SessionIdFromFileName(file));
            await foreach (var item in JsonlScan.ParseLinesAsync(file, parser, options.CancellationToken)
                .ConfigureAwait(false))
            {
                if (item.Invalid || item.Skipped)
                    options.Progress.Skipped++;
                if (item.Value is null)
                    continue;
                options.Progress.Records++;
                yield return item.Value;
            }
        }
    }

    internal static IEnumerable<string> EnumerateSessionFiles(string root)
    {
        if (File.Exists(root) && root.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            yield return root;
            yield break;
        }

        var sessions = Path.Combine(root, "sessions");
        var start = Directory.Exists(sessions) ? sessions : root;
        foreach (var file in JsonlScan.EnumerateFiles(start, "*.jsonl"))
        {
            if (Path.GetFileName(file).Equals("run-history.jsonl", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }

    internal static (UnifiedUsageRecord? Record, bool Skipped) TryConsume(
        ReadOnlySpan<byte> utf8,
        SessionCtx ctx,
        DateTimeOffset? since)
    {
        if (!Utf8JsonWalk.TryStartObject(utf8, out var reader))
            return (null, true);

        string? type = null, id = null, cwd = null, name = null, provider = null, modelId = null, model = null;
        DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;
        MessageFields? message = null;
        var messageMissing = false;

        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "type"u8))
                type = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "id"u8))
                id = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cwd"u8))
                cwd = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "name"u8))
                name = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "provider"u8))
                provider = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "modelId"u8))
                modelId = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "model"u8))
                model = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "timestamp"u8))
                timestamp = Utf8JsonWalk.ReadTimestamp(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "message"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                    message = ReadMessage(ref reader);
                else
                    messageMissing = true;
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        if (string.IsNullOrEmpty(type))
            return (null, true);

        if (type.Equals("session", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(id))
                ctx.SessionId = id;
            ctx.Cwd = cwd ?? ctx.Cwd;
            return (null, false);
        }

        if (type.Equals("session_info", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Title = name ?? ctx.Title;
            return (null, false);
        }

        if (type.Equals("model_change", StringComparison.OrdinalIgnoreCase))
        {
            ctx.ProviderId = provider ?? ctx.ProviderId;
            ctx.ModelId = modelId ?? model ?? ctx.ModelId;
            return (null, false);
        }

        if (!type.Equals("message", StringComparison.OrdinalIgnoreCase))
            return (null, false);

        if (messageMissing || message is null)
            return (null, true);

        var msg = message.Value;
        if (!string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            return (null, false);

        if (timestamp == DateTimeOffset.UnixEpoch)
            timestamp = msg.Timestamp;
        if (since is { } min && timestamp < min)
            return (null, false);

        var resolvedModel = msg.Model ?? ctx.ModelId ?? "";
        var resolvedProvider = msg.Provider ?? ctx.ProviderId;
        ctx.ModelId = resolvedModel;
        ctx.ProviderId = resolvedProvider;

        return (new UnifiedUsageRecord
        {
            AgentId = Id,
            SessionId = string.IsNullOrEmpty(ctx.SessionId) ? "(unknown)" : ctx.SessionId,
            Timestamp = timestamp,
            ModelId = resolvedModel,
            ProviderId = resolvedProvider,
            NormalizedModelKey = ModelKey.Normalize(resolvedProvider, resolvedModel),
            InputTokens = msg.Input,
            OutputTokens = msg.Output,
            ReasoningTokens = 0,
            CacheReadTokens = msg.CacheRead,
            CacheWriteTokens = msg.CacheWrite,
            MessageCount = 1,
            Cwd = ctx.Cwd,
            Title = ctx.Title,
            IsArchived = false
        }, false);
    }

    private static MessageFields ReadMessage(ref Utf8JsonReader reader)
    {
        var fields = new MessageFields();
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "role"u8))
                fields.Role = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "provider"u8))
                fields.Provider = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "model"u8))
                fields.Model = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "timestamp"u8))
                fields.Timestamp = Utf8JsonWalk.ReadTimestamp(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "usage"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                    ReadUsage(ref reader, ref fields);
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        return fields;
    }

    private static void ReadUsage(ref Utf8JsonReader reader, ref MessageFields fields)
    {
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "input"u8) || Utf8JsonWalk.NameEquals(prop, "inputTokens"u8))
                fields.Input = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "output"u8) || Utf8JsonWalk.NameEquals(prop, "outputTokens"u8))
                fields.Output = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cacheRead"u8) || Utf8JsonWalk.NameEquals(prop, "cache_read"u8))
                fields.CacheRead = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cacheWrite"u8) || Utf8JsonWalk.NameEquals(prop, "cache_write"u8))
                fields.CacheWrite = Utf8JsonWalk.ReadInt64(ref reader);
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }
    }

    internal static string SessionIdFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var under = name.LastIndexOf('_');
        return under >= 0 && under < name.Length - 1 ? name[(under + 1)..] : name;
    }

    private static bool LooksLikeSessionDir(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*.jsonl").Any()
                   || Directory.EnumerateDirectories(path).Any(d =>
                       Directory.EnumerateFiles(d, "*.jsonl").Any());
        }
        catch
        {
            return false;
        }
    }

    internal sealed class SessionCtx
    {
        public string SessionId { get; set; } = "";
        public string? Title { get; set; }
        public string? Cwd { get; set; }
        public string? ModelId { get; set; }
        public string? ProviderId { get; set; }
    }

    private sealed class LineParser : IUtf8LineParser<UnifiedUsageRecord>
    {
        private readonly SessionCtx _ctx = new();
        private readonly DateTimeOffset? _since;

        public LineParser(DateTimeOffset? since, string sessionId, string? title = null)
        {
            _since = since;
            _ctx.SessionId = sessionId;
            _ctx.Title = title;
        }

        public UnifiedUsageRecord? Parse(ReadOnlySpan<byte> utf8, out bool invalid, out bool skipped)
        {
            invalid = false;
            var parsed = TryConsume(utf8, _ctx, _since);
            skipped = parsed.Skipped;
            return parsed.Record;
        }
    }

    private struct MessageFields
    {
        public string? Role;
        public string? Provider;
        public string? Model;
        public DateTimeOffset Timestamp;
        public long Input;
        public long Output;
        public long CacheRead;
        public long CacheWrite;
    }
}
