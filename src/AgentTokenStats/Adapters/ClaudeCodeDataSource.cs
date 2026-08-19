using System.Text.Json;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;

namespace AgentTokenStats.Adapters;

public sealed class ClaudeCodeDataSource : AgentDataSourceBase
{
    public const string Id = "claude-code";

    public ClaudeCodeDataSource(AppConfigStore config) : base(config)
    {
    }

    public override string AgentId => Id;
    public override string DisplayName => "Claude Code";
    public override bool CanScan => true;

    protected override IEnumerable<string> EnumerateCandidates()
    {
        var env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            yield return PathUtil.Expand(env);
        yield return Path.Combine(PathUtil.UserHome(), ".claude");
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return Path.Combine(appData, "CherryStudio", ".claude");
        }
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

        var projects = Path.Combine(path, "projects");
        var start = Directory.Exists(projects) ? projects : path;
        try
        {
            if (JsonlScan.EnumerateFiles(start, "*.jsonl").Any())
            {
                error = null;
                return true;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        error = "projects/ 下没有 .jsonl 会话文件";
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
            var parser = new LineParser(
                options.Since,
                Path.GetFileNameWithoutExtension(file),
                TryReadSubagentTitle(file));
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

        var projects = Path.Combine(root, "projects");
        var start = Directory.Exists(projects) ? projects : root;
        foreach (var file in JsonlScan.EnumerateFiles(start, "*.jsonl"))
            yield return file;
    }

    internal static (UnifiedUsageRecord? Record, bool Skipped) TryConsume(
        ReadOnlySpan<byte> utf8,
        SessionCtx ctx,
        DateTimeOffset? since)
    {
        if (!Utf8JsonWalk.TryStartObject(utf8, out var reader))
            return (null, true);

        string? type = null, sessionId = null, cwd = null, slug = null;
        DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;
        AssistantMessage? message = null;
        var messageMissing = false;

        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "type"u8))
                type = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "sessionId"u8))
                sessionId = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cwd"u8))
                cwd = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "slug"u8))
                slug = Utf8JsonWalk.ReadString(ref reader);
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

        if (!string.IsNullOrEmpty(sessionId))
            ctx.SessionId = sessionId;
        if (!string.IsNullOrEmpty(cwd))
            ctx.Cwd = cwd;
        if (!string.IsNullOrEmpty(slug))
            ctx.Title = slug;

        if (!type.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            return (null, false);

        if (messageMissing || message is null)
            return (null, true);

        if (since is { } min && timestamp < min)
            return (null, false);

        var msg = message.Value;
        var modelId = msg.Model ?? "";
        var providerId = ModelKey.InferProvider(modelId);
        if (string.IsNullOrEmpty(providerId) && modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
            providerId = "anthropic";

        return (new UnifiedUsageRecord
        {
            AgentId = Id,
            SessionId = string.IsNullOrEmpty(ctx.SessionId) ? "(unknown)" : ctx.SessionId,
            Timestamp = timestamp,
            ModelId = modelId,
            ProviderId = string.IsNullOrEmpty(providerId) ? null : providerId,
            NormalizedModelKey = ModelKey.Normalize(providerId, modelId),
            InputTokens = msg.Input,
            OutputTokens = msg.Output,
            ReasoningTokens = msg.Reasoning,
            CacheReadTokens = msg.CacheRead,
            CacheWriteTokens = msg.CacheWrite,
            MessageCount = 1,
            Cwd = ctx.Cwd,
            Title = ctx.Title,
            IsArchived = false
        }, false);
    }

    private static AssistantMessage ReadMessage(ref Utf8JsonReader reader)
    {
        var msg = new AssistantMessage();
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "model"u8))
                msg.Model = Utf8JsonWalk.ReadString(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "usage"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                    ReadUsage(ref reader, ref msg);
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        return msg;
    }

    private static void ReadUsage(ref Utf8JsonReader reader, ref AssistantMessage msg)
    {
        long ephemeral = 0;
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "input_tokens"u8) || Utf8JsonWalk.NameEquals(prop, "input"u8))
                msg.Input = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "output_tokens"u8) || Utf8JsonWalk.NameEquals(prop, "output"u8))
                msg.Output = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "reasoning_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "thinking_tokens"u8))
                msg.Reasoning = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cache_read_input_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cache_read"u8))
                msg.CacheRead = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cache_creation_input_tokens"u8)
                     || Utf8JsonWalk.NameEquals(prop, "cache_write"u8))
                msg.CacheWrite = Utf8JsonWalk.ReadInt64(ref reader);
            else if (Utf8JsonWalk.NameEquals(prop, "cache_creation"u8))
            {
                if (Utf8JsonWalk.TryEnterObject(ref reader))
                    ephemeral = ReadEphemeral(ref reader);
            }
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        if (msg.CacheWrite == 0 && ephemeral > 0)
            msg.CacheWrite = ephemeral;
    }

    private static long ReadEphemeral(ref Utf8JsonReader reader)
    {
        long ephemeral = 0;
        while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
        {
            if (Utf8JsonWalk.NameEquals(prop, "ephemeral_1h_input_tokens"u8)
                || Utf8JsonWalk.NameEquals(prop, "ephemeral_5m_input_tokens"u8))
                ephemeral += Utf8JsonWalk.ReadInt64(ref reader);
            else
                Utf8JsonWalk.SkipValue(ref reader);
        }

        return ephemeral;
    }

    private static string? TryReadSubagentTitle(string jsonlPath)
    {
        var meta = Path.ChangeExtension(jsonlPath, ".meta.json");
        if (!File.Exists(meta))
        {
            var sibling = jsonlPath + ".meta.json";
            if (!File.Exists(sibling))
                return null;
            meta = sibling;
        }

        try
        {
            using var stream = new FileStream(
                meta,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                8 * 1024,
                FileOptions.SequentialScan);
            using var doc = JsonDocument.Parse(stream);
            return JsonUtil.GetString(doc.RootElement, "description", "agentType");
        }
        catch
        {
            return null;
        }
    }

    internal sealed class SessionCtx
    {
        public string SessionId { get; set; } = "";
        public string? Title { get; set; }
        public string? Cwd { get; set; }
    }

    private sealed class LineParser : IUtf8LineParser<UnifiedUsageRecord>
    {
        private readonly SessionCtx _ctx = new();
        private readonly DateTimeOffset? _since;

        public LineParser(DateTimeOffset? since, string sessionId, string? title)
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

    private struct AssistantMessage
    {
        public string? Model;
        public long Input;
        public long Output;
        public long Reasoning;
        public long CacheRead;
        public long CacheWrite;
    }
}
