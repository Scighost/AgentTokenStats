using AgentTokenStats.Adapters;
using AgentTokenStats.Aggregation;
using AgentTokenStats.Api;
using AgentTokenStats.Models;
using AgentTokenStats.Services;

namespace AgentTokenStats.Tests;

public class CombinedStatsTests
{
    [Fact]
    public void Combined_dashboard_merges_agents_and_fills_calendar()
    {
        var day = DateTimeOffset.UtcNow.AddDays(-2);
        var opencode = Reduce("opencode",
        [
            Rec("opencode", "c1", day, "gpt-5", 100, 20, "Alpha", @"C:\one"),
            Rec("opencode", "c2", day.AddHours(1), "gpt-5", 10, 2, "Beta", @"C:\two")
        ]);
        var claude = Reduce("claude-code",
        [
            Rec("claude-code", "a1", day, "claude-sonnet-4", 50, 8, "Gamma", @"C:\one")
        ]);

        var dto = DtoMapper.ProjectCombined(
        [
            Scan("opencode", "OpenCode", opencode),
            Scan("claude-code", "Claude Code", claude)
        ], "all");

        Assert.Equal(160, dto.Summary.InputTokens);
        Assert.Equal(30, dto.Summary.OutputTokens);
        Assert.Equal(3, dto.SessionCount);
        Assert.Equal(2, dto.Agents.Count);
        Assert.Equal(160, dto.Agents.Sum(a => a.Metrics.InputTokens));
        Assert.True(dto.CalendarDays.Count >= 365);
        Assert.Contains(dto.CalendarDays, d => d.Metrics.InputTokens == 160);
        Assert.Equal(7, dto.Weekdays.Count);
        Assert.Equal(24, dto.Hours.Count);
        Assert.Equal(2, dto.Models.Count);
        Assert.Equal(3, dto.TopSessions.Count);
        Assert.Contains(dto.TopSessions, s => s.AgentId == "opencode" && s.AgentDisplayName == "OpenCode");
        Assert.Contains(dto.AgentModels, c => c.AgentDisplayName == "OpenCode" && c.ModelId == "gpt-5");
    }

    [Fact]
    public void Combined_dashboard_keeps_distinct_providers_for_same_model_family()
    {
        var day = DateTimeOffset.UtcNow;
        var snap = Reduce("opencode",
        [
            Rec("opencode", "a", day, "deepseek-v4-flash", 80, 8, "Go", @"C:\one", "opencode-go"),
            Rec("opencode", "b", day, "deepseek-v4-flash", 20, 2, "Official", @"C:\one", "deepseek")
        ]);

        var dto = DtoMapper.ProjectCombined([Scan("opencode", "OpenCode", snap)], "all");

        Assert.Equal(2, dto.Providers.Count);
        Assert.Equal("opencode-go", dto.Providers[0].Label);
        Assert.Equal(88, dto.Providers[0].Metrics.TotalTokens);
        Assert.Equal("deepseek", dto.Providers[1].Label);
        Assert.Equal(22, dto.Providers[1].Metrics.TotalTokens);

        Assert.Equal(2, dto.Models.Count);
        Assert.Contains(dto.Models, m => m.ProviderId == "opencode-go" && m.Metrics.TotalTokens == 88);
        Assert.Contains(dto.Models, m => m.ProviderId == "deepseek" && m.Metrics.TotalTokens == 22);

        var hot = Assert.Single(dto.HotModels);
        Assert.Equal("deepseek-v4-flash", hot.NormalizedModelKey);
        Assert.Equal("deepseek, opencode-go", hot.ProviderId);
        Assert.Equal(110, hot.Metrics.TotalTokens);

        Assert.Equal("opencode-go/deepseek-v4-flash", dto.TopSessions[0].ProviderModel);
        Assert.Equal("deepseek/deepseek-v4-flash", dto.TopSessions[1].ProviderModel);
    }

    [Fact]
    public void Utc_records_bucket_by_local_calendar()
    {
        var utc = new DateTimeOffset(2026, 8, 1, 22, 0, 0, TimeSpan.Zero);
        var snap = Reduce("opencode", [Rec("opencode", "s1", utc, "gpt-5", 10, 1, "A", @"C:\one")]);
        var local = utc.ToLocalTime().DateTime;
        var day = DateOnly.FromDateTime(local);
        Assert.True(snap.ByDay.ContainsKey(day));
        Assert.Equal(local.Hour, Assert.Single(snap.ByDayHour.Keys).Hour);
    }

    [Fact]
    public void Combined_sessions_paginate_and_search()
    {
        var records = Enumerable.Range(0, 15)
            .Select(i => Rec("opencode", $"s{i}", DateTimeOffset.UtcNow.AddHours(-i), "gpt-5", 100 - i, 1, $"Title {i}", @"C:\proj"))
            .ToArray();
        var snap = Reduce("opencode", records);
        var scans = new[] { Scan("opencode", "OpenCode", snap) };

        var page2 = DtoMapper.ProjectSessions(scans, "all", true, null, 2, 10, null, null, null);
        Assert.Equal(15, page2.Total);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(2, page2.Page);

        var search = DtoMapper.ProjectSessions(scans, "all", true, "Title 3", 1, 20, null, null, null);
        Assert.Equal(1, search.Total);
        Assert.Equal("s3", search.Items[0].SessionId);
    }

    [Fact]
    public void Combined_projects_group_sessions_by_cwd()
    {
        var day = DateTimeOffset.UtcNow;
        var snap = Reduce("opencode",
        [
            Rec("opencode", "a", day, "gpt-5", 40, 4, "A", @"C:\one"),
            Rec("opencode", "b", day, "gpt-5", 10, 1, "B", @"C:\one"),
            Rec("opencode", "c", day, "gpt-5", 5, 1, "C", @"C:\two")
        ]);
        var page = DtoMapper.ProjectProjects(
            [Scan("opencode", "OpenCode", snap)],
            "all", null, 1, 20, null, null, null);

        Assert.Equal(2, page.Total);
        Assert.Equal("one", page.Items[0].Name);
        Assert.Equal(2, page.Items[0].SessionCount);
        Assert.Equal(50, page.Items[0].Metrics.InputTokens);
        Assert.Equal(2, page.Items[0].Sessions.Count);
        Assert.Equal("A", page.Items[0].Sessions[0].Title);
        Assert.Equal("gpt-5", page.Items[0].Sessions[0].ProviderModel);
        Assert.Equal(44, page.Items[0].Sessions[0].Metrics.TotalTokens);
    }

    private static UnifiedUsageRecord Rec(
        string agentId,
        string sessionId,
        DateTimeOffset at,
        string model,
        long input,
        long output,
        string title,
        string cwd,
        string? provider = null) => new()
    {
        AgentId = agentId,
        SessionId = sessionId,
        Timestamp = at,
        ModelId = model,
        ProviderId = provider,
        NormalizedModelKey = provider is null ? model : ModelKey.Normalize(provider, model),
        InputTokens = input,
        OutputTokens = output,
        MessageCount = 1,
        Title = title,
        Cwd = cwd
    };

    private static AggregationSnapshot Reduce(string agentId, IEnumerable<UnifiedUsageRecord> records) =>
        UsageAggregator.Reduce(agentId, null, false, records);

    private static AgentScan Scan(string id, string name, AggregationSnapshot snap) =>
        new(new StubSource(id, name), new DetectionResult { Found = true }, snap);

    private sealed class StubSource(string id, string name) : IAgentDataSource
    {
        public string AgentId { get; } = id;
        public string DisplayName { get; } = name;
        public bool CanScan => true;
        public DetectionResult Detect() => new() { Found = true };
        public PathSetResult SetRootPath(string? path) => new() { Ok = true, Detection = Detect() };
        public async IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options)
        {
            await Task.Yield();
            yield break;
        }
        public AgentSourceStatus GetStatus() => new() { CanScan = true };
    }
}
