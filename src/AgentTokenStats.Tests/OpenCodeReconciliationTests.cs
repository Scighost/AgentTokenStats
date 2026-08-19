using AgentTokenStats.Adapters;
using AgentTokenStats.Aggregation;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;
using Microsoft.Data.Sqlite;

namespace AgentTokenStats.Tests;

public class OpenCodeReconciliationTests
{
    [Fact]
    public async Task Summary_matches_design_sql()
    {
        using var dir = new TempDir();
        var db = OpenCodeFixture.Create(dir.Path);

        await using var conn = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              COUNT(*) AS message_count,
              SUM(COALESCE(json_extract(data, '$.tokens.input'), 0)) AS input_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.output'), 0)) AS output_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.reasoning'), 0)) AS reasoning_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.cache.read'), 0)) AS cache_read_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.cache.write'), 0)) AS cache_write_tokens
            FROM message
            WHERE json_extract(data, '$.role') = 'assistant';
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var sqlCount = reader.GetInt64(0);
        var sqlInput = reader.GetInt64(1);
        var sqlOutput = reader.GetInt64(2);
        var sqlReasoning = reader.GetInt64(3);
        var sqlCacheRead = reader.GetInt64(4);
        var sqlCacheWrite = reader.GetInt64(5);
        await reader.DisposeAsync();
        await cmd.DisposeAsync();
        await conn.DisposeAsync();

        OpenCodeFixture.InsertJunk(db);

        var config = new AppConfigStore(Path.Combine(dir.Path, "settings.json"));
        var source = new OpenCodeDataSource(config);
        var set = source.SetRootPath(dir.Path);
        Assert.True(set.Ok, set.Error);

        var options = new ScanOptions();
        var records = new List<UnifiedUsageRecord>();
        await foreach (var record in source.Scan(options))
            records.Add(record);

        var snap = UsageAggregator.Reduce("opencode", dir.Path, true, records, options.Progress.Skipped);

        Assert.Equal(sqlCount, snap.Summary.MessageCount);
        Assert.Equal(sqlInput, snap.Summary.InputTokens);
        Assert.Equal(sqlOutput, snap.Summary.OutputTokens);
        Assert.Equal(sqlReasoning, snap.Summary.ReasoningTokens);
        Assert.Equal(sqlCacheRead, snap.Summary.CacheReadTokens);
        Assert.Equal(sqlCacheWrite, snap.Summary.CacheWriteTokens);
        Assert.Equal(2, snap.Summary.MessageCount);
        Assert.Equal(1, snap.SkippedRecords);
    }

    [Fact]
    public async Task Live_opencode_db_matches_sql_when_present()
    {
        var liveRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "opencode");
        var liveDb = Path.Combine(liveRoot, "opencode.db");
        if (!File.Exists(liveDb))
            return;

        await using var conn = new SqliteConnection($"Data Source={liveDb};Mode=ReadOnly");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              COUNT(*) AS message_count,
              SUM(COALESCE(json_extract(data, '$.tokens.input'), 0)) AS input_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.output'), 0)) AS output_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.reasoning'), 0)) AS reasoning_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.cache.read'), 0)) AS cache_read_tokens,
              SUM(COALESCE(json_extract(data, '$.tokens.cache.write'), 0)) AS cache_write_tokens
            FROM message
            WHERE json_extract(data, '$.role') = 'assistant';
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        long ReadLong(int i) => reader.IsDBNull(i) ? 0 : Convert.ToInt64(reader.GetValue(i));
        var sqlCount = ReadLong(0);
        var sqlInput = ReadLong(1);
        var sqlOutput = ReadLong(2);
        var sqlReasoning = ReadLong(3);
        var sqlCacheRead = ReadLong(4);
        var sqlCacheWrite = ReadLong(5);

        using var dir = new TempDir();
        var config = new AppConfigStore(Path.Combine(dir.Path, "settings.json"));
        var source = new OpenCodeDataSource(config);
        Assert.True(source.SetRootPath(liveRoot).Ok);

        var options = new ScanOptions();
        var records = new List<UnifiedUsageRecord>();
        await foreach (var record in source.Scan(options))
            records.Add(record);

        var snap = UsageAggregator.Reduce("opencode", liveRoot, true, records, options.Progress.Skipped);

        Assert.Equal(sqlCount, snap.Summary.MessageCount);
        Assert.Equal(sqlInput, snap.Summary.InputTokens);
        Assert.Equal(sqlOutput, snap.Summary.OutputTokens);
        Assert.Equal(sqlReasoning, snap.Summary.ReasoningTokens);
        Assert.Equal(sqlCacheRead, snap.Summary.CacheReadTokens);
        Assert.Equal(sqlCacheWrite, snap.Summary.CacheWriteTokens);
    }
}

public class DetectionTests
{
    [Fact]
    public void OpenCode_detects_manual_fixture()
    {
        using var dir = new TempDir();
        OpenCodeFixture.Create(dir.Path);
        var config = new AppConfigStore(Path.Combine(dir.Path, "settings.json"));
        var source = new OpenCodeDataSource(config);
        var result = source.SetRootPath(dir.Path);
        Assert.True(result.Ok);
        Assert.True(result.Detection.Found);
        Assert.True(result.Detection.ManualPath);
    }

    [Fact]
    public void Missing_path_is_not_found()
    {
        using var dir = new TempDir();
        var config = new AppConfigStore(Path.Combine(dir.Path, "settings.json"));
        var source = new CodexDataSource(config);
        var result = source.SetRootPath(Path.Combine(dir.Path, "nope"));
        Assert.False(result.Ok);
        Assert.False(result.Detection.Found);
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ats-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); }
        catch { /* ignore */ }
    }
}

internal static class OpenCodeFixture
{
    public static string Create(string root, bool includeJunk = false)
    {
        var db = Path.Combine(root, "opencode.db");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE session (
              id TEXT PRIMARY KEY,
              title TEXT NOT NULL DEFAULT '',
              directory TEXT NOT NULL DEFAULT '',
              time_archived INTEGER
            );
            CREATE TABLE message (
              id TEXT PRIMARY KEY,
              session_id TEXT NOT NULL,
              time_created INTEGER,
              time_updated INTEGER,
              data TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        InsertSession(conn, "ses_1", "Alpha", "C:\\proj", archived: false);
        InsertSession(conn, "ses_2", "Beta", "C:\\other", archived: true);

        var t1 = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        InsertMessage(conn, "msg_1", "ses_1", t1, """
            {"role":"assistant","modelID":"claude-haiku-4-5","providerID":"anthropic",
             "cost":0.01,"tokens":{"input":10,"output":2,"reasoning":1,"cache":{"read":4,"write":1}}}
            """);
        InsertMessage(conn, "msg_2", "ses_1", t1, """{"role":"user","tokens":{"input":999}}""");
        InsertMessage(conn, "msg_3", "ses_2", t1, """
            {"role":"assistant","modelID":"claude-haiku-4-5","providerID":"anthropic",
             "tokens":{"input":5,"output":1,"reasoning":0,"cache":{"read":0,"write":0}}}
            """);
        if (includeJunk)
            InsertMessage(conn, "msg_bad", "ses_1", t1, "{not-json");

        return db;
    }

    public static void InsertJunk(string db)
    {
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        InsertMessage(conn, "msg_bad", "ses_1", 1, "{not-json");
    }

    private static void InsertSession(SqliteConnection conn, string id, string title, string dir, bool archived)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO session(id,title,directory,time_archived) VALUES($id,$t,$d,$a)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$d", dir);
        cmd.Parameters.AddWithValue("$a", archived ? 1 : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertMessage(SqliteConnection conn, string id, string session, long time, string data)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO message(id,session_id,time_created,time_updated,data) VALUES($id,$s,$t,$t,$d)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$s", session);
        cmd.Parameters.AddWithValue("$t", time);
        cmd.Parameters.AddWithValue("$d", data);
        cmd.ExecuteNonQuery();
    }
}
