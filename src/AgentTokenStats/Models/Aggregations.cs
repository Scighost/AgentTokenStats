namespace AgentTokenStats.Models;

public sealed class MetricBucket
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public int MessageCount { get; set; }

    public long TotalTokens =>
        InputTokens + OutputTokens + ReasoningTokens + CacheReadTokens + CacheWriteTokens;

    public void Add(UnifiedUsageRecord record)
    {
        InputTokens += record.InputTokens;
        OutputTokens += record.OutputTokens;
        ReasoningTokens += record.ReasoningTokens;
        CacheReadTokens += record.CacheReadTokens;
        CacheWriteTokens += record.CacheWriteTokens;
        MessageCount += record.MessageCount;
    }
}

public sealed class SessionBucket
{
    public required string SessionId { get; init; }
    public string AgentId { get; set; } = "";
    public string? Title { get; set; }
    public string? Cwd { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public MetricBucket Metrics { get; } = new();
    public Dictionary<string, ModelBucket> ByModel { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelBucket
{
    public required string NormalizedModelKey { get; init; }
    public string ModelId { get; set; } = "";
    public string? ProviderId { get; set; }
    public MetricBucket Metrics { get; } = new();
}

public sealed class AggregationSnapshot
{
    public required string AgentId { get; init; }
    public string? DataRootPath { get; init; }
    public bool ManualPath { get; init; }
    public DateTimeOffset ScannedAt { get; set; }
    public int RecordCount { get; set; }
    public int SkippedRecords { get; set; }
    public MetricBucket Summary { get; } = new();
    public Dictionary<DateOnly, MetricBucket> ByDay { get; } = new();
    public Dictionary<string, ModelBucket> ByModel { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<(DateOnly Day, string ModelKey), MetricBucket> ByDayModel { get; } = new();
    public Dictionary<(DateOnly Day, int Hour), MetricBucket> ByDayHour { get; } = new();
    public Dictionary<string, SessionBucket> BySession { get; } = new(StringComparer.Ordinal);
}
