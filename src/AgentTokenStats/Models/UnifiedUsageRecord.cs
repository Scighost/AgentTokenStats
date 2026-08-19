namespace AgentTokenStats.Models;

public sealed class UnifiedUsageRecord
{
    public required string AgentId { get; init; }
    public required string SessionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string ModelId { get; init; } = "";
    public string? ProviderId { get; init; }
    public string NormalizedModelKey { get; init; } = "";
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long ReasoningTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public int MessageCount { get; init; }
    public string? Cwd { get; init; }
    public string? Title { get; init; }
    public bool IsArchived { get; init; }

    public long TotalTokens =>
        InputTokens + OutputTokens + ReasoningTokens + CacheReadTokens + CacheWriteTokens;
}

public sealed class DetectionResult
{
    public bool Found { get; init; }
    public string? ResolvedPath { get; init; }
    public IReadOnlyList<string> CandidateTried { get; init; } = [];
    public string? Error { get; init; }
    public bool ManualPath { get; init; }
}

public sealed class ScanProgress
{
    public int Records { get; set; }
    public int Skipped { get; set; }
}

public sealed class ScanOptions
{
    public bool IncludeArchived { get; init; } = true;
    public DateTimeOffset? Since { get; init; }
    public ScanProgress Progress { get; init; } = new();
    public CancellationToken CancellationToken { get; init; }
}

public sealed class AgentSourceStatus
{
    public DateTimeOffset? LastScanAt { get; init; }
    public int RecordCount { get; init; }
    public int SkippedRecords { get; init; }
    public string? DataRootPath { get; init; }
    public bool ManualPath { get; init; }
    public bool CanScan { get; init; }
}

public sealed class PathSetResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public DetectionResult Detection { get; init; } = new();
}
