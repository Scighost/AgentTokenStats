using System.Collections.Concurrent;
using AgentTokenStats.Adapters;
using AgentTokenStats.Aggregation;
using AgentTokenStats.Models;

namespace AgentTokenStats.Services;

public readonly record struct AgentScan(
    IAgentDataSource Source,
    DetectionResult Detection,
    AggregationSnapshot Snapshot);

public sealed class StatsService
{
    private readonly IReadOnlyList<IAgentDataSource> _sources;
    private readonly ConcurrentDictionary<string, AggregationSnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public StatsService(IEnumerable<IAgentDataSource> sources)
    {
        _sources = sources.ToList();
    }

    public IReadOnlyList<IAgentDataSource> Sources => _sources;

    public IAgentDataSource? GetSource(string agentId) =>
        _sources.FirstOrDefault(s => s.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IAgentDataSource> ResolveSources(string? agentsQuery)
    {
        if (string.IsNullOrWhiteSpace(agentsQuery) ||
            agentsQuery.Equals("all", StringComparison.OrdinalIgnoreCase))
            return _sources;

        var ids = agentsQuery.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return _sources.Where(source => wanted.Contains(source.AgentId)).ToList();
    }

    public async Task<IReadOnlyList<AgentScan>> ScanManyAsync(
        string? agentsQuery,
        bool force,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var sources = ResolveSources(agentsQuery);
        var results = new AgentScan[sources.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, sources.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, sources.Count),
                CancellationToken = cancellationToken
            },
            async (index, ct) =>
            {
                var source = sources[index];
                var detection = source.Detect();
                AggregationSnapshot snap;
                try
                {
                    snap = await ScanAsync(source.AgentId, force, includeArchived, ct)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    snap = Empty(source.AgentId, detection);
                }

                results[index] = new AgentScan(source, detection, snap);
            }).ConfigureAwait(false);
        return results;
    }

    public async Task<AggregationSnapshot> ScanAsync(
        string agentId,
        bool force,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var source = GetSource(agentId)
            ?? throw new KeyNotFoundException($"Unknown agent '{agentId}'.");
        var detection = source.Detect();
        var cacheKey = CacheKey(agentId, includeArchived, detection.ResolvedPath);
        var gate = _gates.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _cache.TryGetValue(cacheKey, out var cached))
                return cached;

            if (!source.CanScan || !detection.Found)
            {
                var empty = Empty(agentId, detection);
                _cache[cacheKey] = empty;
                return empty;
            }

            var options = new ScanOptions
            {
                IncludeArchived = includeArchived,
                CancellationToken = cancellationToken
            };
            var snap = new AggregationSnapshot
            {
                AgentId = agentId,
                DataRootPath = detection.ResolvedPath,
                ManualPath = detection.ManualPath,
                ScannedAt = DateTimeOffset.UtcNow
            };

            await foreach (var record in source.Scan(options).WithCancellation(cancellationToken)
                .ConfigureAwait(false))
                UsageAggregator.Add(snap, record);

            snap.SkippedRecords = options.Progress.Skipped;
            _cache[cacheKey] = snap;
            return snap;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(string agentId)
    {
        var prefix = agentId + "|";
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
    }

    private static string CacheKey(string agentId, bool includeArchived, string? path) =>
        $"{agentId}|{includeArchived}|{path ?? ""}";

    private static AggregationSnapshot Empty(string agentId, DetectionResult detection) =>
        new()
        {
            AgentId = agentId,
            DataRootPath = detection.ResolvedPath,
            ManualPath = detection.ManualPath,
            ScannedAt = DateTimeOffset.UtcNow
        };
}
