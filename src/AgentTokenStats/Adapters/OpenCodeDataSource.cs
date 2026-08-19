using System.Globalization;
using System.Runtime.CompilerServices;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;
using Microsoft.Data.Sqlite;

namespace AgentTokenStats.Adapters;

public sealed class OpenCodeDataSource : AgentDataSourceBase
{
    public const string Id = "opencode";

    public OpenCodeDataSource(AppConfigStore config) : base(config)
    {
    }

    public override string AgentId => Id;
    public override string DisplayName => "OpenCode";
    public override bool CanScan => true;

    protected override IEnumerable<string> EnumerateCandidates()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
            yield return Path.Combine(PathUtil.Expand(xdg), "opencode");

        yield return Path.Combine(PathUtil.UserHome(), ".local", "share", "opencode");
    }

    protected override bool IsValidRoot(string path, out string? error)
    {
        var db = ResolveDbPath(path);
        if (db is null)
        {
            error = "未找到可读的 opencode.db";
            return false;
        }

        if (!PathUtil.CanReadFile(db))
        {
            error = "opencode.db 不可读";
            return false;
        }

        error = null;
        return true;
    }

    public override async IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options)
    {
        var detection = Detect();
        if (!detection.Found || string.IsNullOrWhiteSpace(detection.ResolvedPath))
            yield break;

        var dbPath = ResolveDbPath(detection.ResolvedPath);
        if (dbPath is null)
            yield break;

        var sinceMs = options.Since?.ToUnixTimeMilliseconds();
        await foreach (var row in StreamRowsAsync(dbPath, sinceMs, options.CancellationToken)
            .ConfigureAwait(false))
        {
            if (row.Skipped)
                options.Progress.Skipped++;
            if (row.Record is null)
                continue;
            if (!options.IncludeArchived && row.Record.IsArchived)
                continue;
            options.Progress.Records++;
            yield return row.Record;
        }
    }

    private async IAsyncEnumerable<ScanRow> StreamRowsAsync(
        string dbPath,
        long? sinceMs,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var conn = await SqliteReadonly.OpenAsync(dbPath, cancellationToken)
            .ConfigureAwait(false);
        var sessions = LoadSessions(conn);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              m.session_id,
              m.time_created,
              json_valid(m.data),
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.role') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.modelID') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.providerID') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.tokens.input') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.tokens.output') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.tokens.reasoning') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.tokens.cache.read') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.tokens.cache.write') END,
              CASE WHEN json_valid(m.data) THEN json_extract(m.data, '$.time.created') END
            FROM message m
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sessionId = reader.IsDBNull(0) ? "" : reader.GetString(0);
            sessions.TryGetValue(sessionId, out var session);
            var timeCreated = ReadInt64OrNull(reader, 1);
            var valid = ReadInt64(reader, 2) != 0;
            var role = ReadString(reader, 3);
            if (!valid || string.IsNullOrEmpty(role))
            {
                yield return new ScanRow(null, true);
                continue;
            }

            if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ScanRow(null, false);
                continue;
            }

            var modelId = ReadString(reader, 4) ?? "";
            var providerId = ReadString(reader, 5);
            var input = ReadInt64(reader, 6);
            var output = ReadInt64(reader, 7);
            var reasoning = ReadInt64(reader, 8);
            var cacheRead = ReadInt64(reader, 9);
            var cacheWrite = ReadInt64(reader, 10);
            var jsonCreated = ReadInt64OrNull(reader, 11);
            var timestamp = ResolveTimestamp(timeCreated, jsonCreated);
            if (sinceMs is { } min && timestamp.ToUnixTimeMilliseconds() < min)
            {
                yield return new ScanRow(null, false);
                continue;
            }

            yield return new ScanRow(
                new UnifiedUsageRecord
                {
                    AgentId = Id,
                    SessionId = string.IsNullOrEmpty(sessionId) ? "(unknown)" : sessionId,
                    Timestamp = timestamp,
                    ModelId = modelId,
                    ProviderId = providerId,
                    NormalizedModelKey = ModelKey.Normalize(providerId, modelId),
                    InputTokens = input,
                    OutputTokens = output,
                    ReasoningTokens = reasoning,
                    CacheReadTokens = cacheRead,
                    CacheWriteTokens = cacheWrite,
                    MessageCount = 1,
                    Cwd = session.Cwd,
                    Title = session.Title,
                    IsArchived = session.Archived
                },
                false);
        }
    }

    private static Dictionary<string, SessionMeta> LoadSessions(SqliteConnection conn)
    {
        var result = new Dictionary<string, SessionMeta>(StringComparer.Ordinal);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, title, directory, time_archived FROM session";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (string.IsNullOrEmpty(id))
                    continue;
                result[id] = new SessionMeta(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    ReadInt64OrNull(reader, 3) is > 0);
            }
        }
        catch (SqliteException)
        {
            /* session table is optional */
        }

        return result;
    }

    private static DateTimeOffset ResolveTimestamp(long? columnMs, long? jsonCreatedMs)
    {
        if (columnMs is > 0)
            return DateTimeOffset.FromUnixTimeMilliseconds(columnMs.Value);
        if (jsonCreatedMs is > 0)
            return DateTimeOffset.FromUnixTimeMilliseconds(jsonCreatedMs.Value);
        return DateTimeOffset.UnixEpoch;
    }

    private static string? ReadString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static long ReadInt64(SqliteDataReader reader, int ordinal) =>
        ReadInt64OrNull(reader, ordinal) ?? 0;

    private static long? ReadInt64OrNull(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            float f => (long)f,
            decimal m => (long)m,
            bool b => b ? 1 : 0,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var df) => (long)df,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }

    private static string? ResolveDbPath(string path)
    {
        if (File.Exists(path) && path.EndsWith("opencode.db", StringComparison.OrdinalIgnoreCase))
            return path;
        var nested = Path.Combine(path, "opencode.db");
        return File.Exists(nested) ? nested : null;
    }

    private readonly record struct ScanRow(UnifiedUsageRecord? Record, bool Skipped);
    private readonly record struct SessionMeta(string? Title, string? Cwd, bool Archived);
}

public static class ModelKey
{
    public static string Normalize(string? providerId, string? modelId)
    {
        var provider = (providerId ?? "").Trim().ToLowerInvariant();
        var model = (modelId ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(provider))
            return model;
        if (string.IsNullOrEmpty(model))
            return provider;
        return $"{provider}:{model}";
    }

    /// <summary>
    /// Same underlying model across providers (e.g. anthropic vs github-copilot).
    /// </summary>
    public static string FamilyKey(string? modelId, string? normalizedKey = null)
    {
        var raw = !string.IsNullOrWhiteSpace(modelId)
            ? modelId.Trim().ToLowerInvariant()
            : (normalizedKey ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(raw))
            return "(unknown)";

        var colon = raw.IndexOf(':');
        if (colon > 0 && colon < raw.Length - 1)
            raw = raw[(colon + 1)..];

        var slash = raw.IndexOf('/');
        if (slash > 0 && slash < raw.Length - 1)
            raw = raw[(slash + 1)..];

        return raw;
    }

    public static string InferProvider(string? modelId)
    {
        var model = (modelId ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(model))
            return "";
        if (model.StartsWith("claude", StringComparison.Ordinal))
            return "anthropic";
        if (model.StartsWith("gpt", StringComparison.Ordinal)
            || model.StartsWith("o1", StringComparison.Ordinal)
            || model.StartsWith("o3", StringComparison.Ordinal)
            || model.StartsWith("o4", StringComparison.Ordinal)
            || model.StartsWith("chatgpt", StringComparison.Ordinal)
            || model.Contains("codex", StringComparison.Ordinal)
            || model.StartsWith("openai", StringComparison.Ordinal))
            return "openai";
        if (model.StartsWith("gemini", StringComparison.Ordinal))
            return "google";
        if (model.StartsWith("deepseek", StringComparison.Ordinal))
            return "deepseek";
        if (model.StartsWith("grok", StringComparison.Ordinal))
            return "xai";
        if (model.StartsWith("mimo", StringComparison.Ordinal))
            return "mimo";
        if (model.StartsWith("composer", StringComparison.Ordinal)
            || model.StartsWith("cursor", StringComparison.Ordinal)
            || model is "auto" or "cursor-auto")
            return "cursor";
        return "";
    }
}
