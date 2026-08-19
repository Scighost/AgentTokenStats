using AgentTokenStats.Models;

namespace AgentTokenStats.Adapters;

public interface IAgentDataSource
{
    string AgentId { get; }
    string DisplayName { get; }
    bool CanScan { get; }
    DetectionResult Detect();
    PathSetResult SetRootPath(string? path);
    IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options);
    AgentSourceStatus GetStatus();
}
