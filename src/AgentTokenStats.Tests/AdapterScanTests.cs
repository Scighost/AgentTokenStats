using AgentTokenStats.Adapters;
using AgentTokenStats.Api;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;
using Microsoft.Data.Sqlite;

namespace AgentTokenStats.Tests;

public class AdapterScanTests
{
    [Fact]
    public void Codex_maps_cached_input_out_of_input_bucket()
    {
        var mapped = CodexDataSource.MapUsage(new CodexDataSource.TokenUsage(100, 5, 2, 20));
        Assert.Equal(80, mapped.Input);
        Assert.Equal(5, mapped.Output);
        Assert.Equal(2, mapped.Reasoning);
        Assert.Equal(20, mapped.CacheRead);
    }

    [Fact]
    public async Task Codex_scan_uses_cumulative_delta_and_skips_junk()
    {
        using var dir = new TempDir();
        var id = "019fa376-8ad1-73f2-9967-b706914d8739";
        var sessionDir = Path.Combine(dir.Path, "sessions", "2026", "08", "01");
        Directory.CreateDirectory(sessionDir);
        var file = Path.Combine(sessionDir, $"rollout-2026-08-01T12-00-00-{id}.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            """{"timestamp":"2026-08-01T12:00:00Z","type":"session_meta","payload":{"id":"019fa376-8ad1-73f2-9967-b706914d8739","cwd":"C:\\proj","model_provider":"openai"}}""",
            """{"timestamp":"2026-08-01T12:00:01Z","type":"turn_context","payload":{"model":"gpt-5.4","cwd":"C:\\proj"}}""",
            "not-json",
            """{"timestamp":"2026-08-01T12:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":5,"reasoning_output_tokens":2}}}}""",
            """{"timestamp":"2026-08-01T12:00:03Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"cached_input_tokens":40,"output_tokens":9,"reasoning_output_tokens":3}}}}""",
            """{"timestamp":"2026-08-01T12:00:04Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"cached_input_tokens":40,"output_tokens":9,"reasoning_output_tokens":3}}}}"""
        ]);
        CreateThreadsDb(dir.Path, id, "Alpha", "C:\\proj", archived: true);

        var source = new CodexDataSource(new AppConfigStore(Path.Combine(dir.Path, "settings.json")));
        Assert.True(source.SetRootPath(dir.Path).Ok);
        var options = new ScanOptions();
        var records = await Collect(source, options);

        Assert.Equal(2, records.Count);
        Assert.Equal(1, options.Progress.Skipped);
        Assert.Equal(id, records[0].SessionId);
        Assert.Equal("openai", records[0].ProviderId);
        Assert.Equal("gpt-5.4", records[0].ModelId);
        Assert.Equal(80, records[0].InputTokens);
        Assert.Equal(20, records[0].CacheReadTokens);
        Assert.Equal(5, records[0].OutputTokens);
        Assert.Equal(2, records[0].ReasoningTokens);
        Assert.Equal(30, records[1].InputTokens);
        Assert.Equal(20, records[1].CacheReadTokens);
        Assert.Equal(4, records[1].OutputTokens);
        Assert.Equal(1, records[1].ReasoningTokens);
        Assert.True(records[0].IsArchived);
        Assert.Equal("Alpha", records[0].Title);
    }

    [Fact]
    public async Task Pi_scan_reads_assistant_usage()
    {
        using var dir = new TempDir();
        var sid = "019fc0a3-d2d1-7691-b1c5-d611007b1c6b";
        var sessionDir = Path.Combine(dir.Path, "sessions", "--C--proj--");
        Directory.CreateDirectory(sessionDir);
        var file = Path.Combine(sessionDir, $"2026-08-01T00-00-00-000Z_{sid}.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            """{"type":"session","version":3,"id":"019fc0a3-d2d1-7691-b1c5-d611007b1c6b","timestamp":"2026-08-01T00:00:00Z","cwd":"C:\\proj"}""",
            """{"type":"session_info","id":"aaaa","parentId":null,"timestamp":"2026-08-01T00:00:01Z","name":"Demo"}""",
            """{"type":"message","id":"1","parentId":null,"timestamp":"2026-08-01T00:00:02Z","message":{"role":"user","content":"hi","timestamp":1}}""",
            """{"type":"message","id":"2","parentId":"1","timestamp":"2026-08-01T00:00:03Z","message":{"role":"assistant","provider":"deepseek","model":"deepseek-v4-flash","usage":{"input":933,"output":302,"cacheRead":768,"cacheWrite":0,"cost":{"total":0.0002}},"timestamp":1}}""",
            "{bad"
        ]);

        var source = new PiDataSource(new AppConfigStore(Path.Combine(dir.Path, "settings.json")));
        Assert.True(source.SetRootPath(dir.Path).Ok);
        var options = new ScanOptions();
        var records = await Collect(source, options);

        Assert.Single(records);
        Assert.Equal(1, options.Progress.Skipped);
        Assert.Equal(sid, records[0].SessionId);
        Assert.Equal("deepseek", records[0].ProviderId);
        Assert.Equal(933, records[0].InputTokens);
        Assert.Equal(302, records[0].OutputTokens);
        Assert.Equal(768, records[0].CacheReadTokens);
        Assert.Equal("Demo", records[0].Title);
        Assert.Equal("C:\\proj", records[0].Cwd);
        Assert.False(records[0].IsArchived);
    }

    [Fact]
    public async Task Claude_scan_sums_cache_creation_and_skips_non_assistant()
    {
        using var dir = new TempDir();
        var sid = "3ee08462-aaaa-bbbb-cccc-ddddeeeeffff";
        var project = Path.Combine(dir.Path, "projects", "C--proj");
        Directory.CreateDirectory(project);
        var file = Path.Combine(project, sid + ".jsonl");
        await File.WriteAllLinesAsync(file,
        [
            """{"type":"user","sessionId":"3ee08462-aaaa-bbbb-cccc-ddddeeeeffff","cwd":"C:\\proj","timestamp":"2026-08-01T00:00:00Z","message":{"role":"user","content":"hi"}}""",
            """{"type":"assistant","sessionId":"3ee08462-aaaa-bbbb-cccc-ddddeeeeffff","cwd":"C:\\proj","slug":"Demo","timestamp":"2026-08-01T00:00:01Z","message":{"role":"assistant","model":"claude-haiku-4-5","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":40,"cache_creation_input_tokens":0,"cache_creation":{"ephemeral_5m_input_tokens":8,"ephemeral_1h_input_tokens":2}}}}""",
            "not-json"
        ]);

        var source = new ClaudeCodeDataSource(new AppConfigStore(Path.Combine(dir.Path, "settings.json")));
        Assert.True(source.SetRootPath(dir.Path).Ok);
        var options = new ScanOptions();
        var records = await Collect(source, options);

        Assert.Single(records);
        Assert.Equal(1, options.Progress.Skipped);
        Assert.Equal(sid, records[0].SessionId);
        Assert.Equal("anthropic", records[0].ProviderId);
        Assert.Equal(100, records[0].InputTokens);
        Assert.Equal(20, records[0].OutputTokens);
        Assert.Equal(40, records[0].CacheReadTokens);
        Assert.Equal(10, records[0].CacheWriteTokens);
        Assert.Equal("Demo", records[0].Title);
    }

    [Fact]
    public async Task Claude_scan_skips_huge_content_and_keeps_usage()
    {
        using var dir = new TempDir();
        var sid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var project = Path.Combine(dir.Path, "projects", "C--proj");
        Directory.CreateDirectory(project);
        var file = Path.Combine(project, sid + ".jsonl");
        var blob = new string('x', 256 * 1024);
        await File.WriteAllLinesAsync(file,
        [
            "{\"type\":\"user\",\"sessionId\":\"" + sid + "\",\"cwd\":\"C:\\\\proj\",\"message\":{\"role\":\"user\",\"content\":\"" + blob + "\"}}",
            "{\"type\":\"assistant\",\"sessionId\":\"" + sid + "\",\"cwd\":\"C:\\\\proj\",\"timestamp\":\"2026-08-01T00:00:01Z\",\"message\":{\"role\":\"assistant\",\"model\":\"claude-haiku-4-5\",\"content\":\"" + blob + "\",\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}}"
        ]);

        var source = new ClaudeCodeDataSource(new AppConfigStore(Path.Combine(dir.Path, "settings.json")));
        Assert.True(source.SetRootPath(dir.Path).Ok);
        var records = await Collect(source, new ScanOptions());

        Assert.Single(records);
        Assert.Equal(7, records[0].InputTokens);
        Assert.Equal(3, records[0].OutputTokens);
    }

    [Fact]
    public void ModelKey_infers_providers()
    {
        Assert.Equal("anthropic", ModelKey.InferProvider("claude-haiku-4-5"));
        Assert.Equal("openai", ModelKey.InferProvider("gpt-5.4"));
        Assert.Equal("cursor", ModelKey.InferProvider("composer-1"));
        Assert.Equal("deepseek", ModelKey.InferProvider("deepseek-v4-flash"));
    }

    [Fact]
    public void ModelKey_family_strips_provider()
    {
        Assert.Equal("claude-sonnet-4", ModelKey.FamilyKey("claude-sonnet-4", "anthropic:claude-sonnet-4"));
        Assert.Equal("claude-sonnet-4", ModelKey.FamilyKey("", "github-copilot:claude-sonnet-4"));
        Assert.Equal("claude-sonnet-4", ModelKey.FamilyKey("anthropic/claude-sonnet-4"));
        Assert.Equal("(unknown)", ModelKey.FamilyKey(null, ""));
    }

    [Fact]
    public void Dashboard_recent_is_14_days_and_hot_models_merge_to_top_5()
    {
        var snap = new AggregationSnapshot { AgentId = "t", ScannedAt = DateTimeOffset.UtcNow };
        var today = DateOnly.FromDateTime(DateTime.Now);
        for (var i = 0; i < 14; i++)
            snap.ByDay[today.AddDays(-i)] = new MetricBucket { InputTokens = i + 1 };

        AddModel(snap, "anthropic:claude-sonnet-4", "claude-sonnet-4", "anthropic", 100);
        AddModel(snap, "github-copilot:claude-sonnet-4", "claude-sonnet-4", "github-copilot", 40);
        for (var i = 1; i <= 6; i++)
            AddModel(snap, $"openai:m{i}", $"m{i}", "openai", i * 10);

        var dto = DtoMapper.Project(snap, true, true, null, "all", "tokens");
        Assert.Equal(14, dto.Recent14Days.Count);
        Assert.Equal(5, dto.HotModels.Count);
        Assert.Equal(0, dto.SessionCount);

        var claude = Assert.Single(dto.HotModels, m => m.NormalizedModelKey == "claude-sonnet-4");
        Assert.Equal(140, claude.Metrics.InputTokens);
        Assert.Equal("anthropic, github-copilot", claude.ProviderId);
        Assert.DoesNotContain(dto.HotModels, m => m.NormalizedModelKey is "m1" or "m2");
    }

    [Fact]
    public void Dashboard_session_count_respects_range()
    {
        var snap = new AggregationSnapshot { AgentId = "t", ScannedAt = DateTimeOffset.UtcNow };
        var now = DateTimeOffset.UtcNow;
        snap.BySession["a"] = new SessionBucket { SessionId = "a", FirstSeen = now, LastSeen = now };
        snap.BySession["b"] = new SessionBucket
        {
            SessionId = "b",
            FirstSeen = now.AddDays(-40),
            LastSeen = now.AddDays(-40)
        };

        Assert.Equal(2, DtoMapper.Project(snap, true, true, null, "all", "tokens").SessionCount);
        Assert.Equal(1, DtoMapper.Project(snap, true, true, null, "30d", "tokens").SessionCount);
    }

    [Fact]
    public void Codex_filename_extracts_uuid()
    {
        var id = CodexDataSource.SessionIdFromFileName(
            @"C:\x\rollout-2026-07-27T20-04-42-019fa376-8ad1-73f2-9967-b706914d8739.jsonl");
        Assert.Equal("019fa376-8ad1-73f2-9967-b706914d8739", id);
    }

    private static async Task<List<UnifiedUsageRecord>> Collect(IAgentDataSource source, ScanOptions options)
    {
        var records = new List<UnifiedUsageRecord>();
        await foreach (var record in source.Scan(options))
            records.Add(record);
        return records;
    }

    private static void AddModel(
        AggregationSnapshot snap,
        string key,
        string modelId,
        string provider,
        long input)
    {
        var bucket = new ModelBucket
        {
            NormalizedModelKey = key,
            ModelId = modelId,
            ProviderId = provider
        };
        bucket.Metrics.InputTokens = input;
        snap.ByModel[key] = bucket;
    }

    private static void CreateThreadsDb(string root, string id, string title, string cwd, bool archived)
    {
        var db = Path.Combine(root, "state_5.sqlite");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE threads (
              id TEXT PRIMARY KEY,
              title TEXT,
              cwd TEXT,
              archived INTEGER,
              tokens_used INTEGER,
              model TEXT,
              model_provider TEXT
            );
            INSERT INTO threads(id,title,cwd,archived,tokens_used,model,model_provider)
            VALUES($id,$t,$c,$a,0,'gpt-5.4','openai');
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$c", cwd);
        cmd.Parameters.AddWithValue("$a", archived ? 1 : 0);
        cmd.ExecuteNonQuery();
    }
}
