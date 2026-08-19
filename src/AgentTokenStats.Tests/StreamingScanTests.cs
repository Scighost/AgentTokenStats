using AgentTokenStats.Adapters;
using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;
using AgentTokenStats.Services;

namespace AgentTokenStats.Tests;

public class StreamingScanTests
{
    [Fact]
    public async Task Jsonl_reads_documents_line_by_line()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "rows.jsonl");
        await File.WriteAllTextAsync(path, """
            {"n":1}
            not-json
            {"n":2}

            {"n":3}
            """);

        var items = new List<Utf8LineResult<int>>();
        await foreach (var item in JsonlScan.ParseLinesAsync(path, new NParser(), CancellationToken.None))
            items.Add(item);

        Assert.Equal(4, items.Count);
        Assert.Equal(1, items[0].Value);
        Assert.True(items[1].Invalid);
        Assert.Equal(2, items[2].Value);
        Assert.Equal(3, items[3].Value);
    }

    [Fact]
    public async Task ScanMany_reads_sources_in_parallel()
    {
        var live = 0;
        var peak = 0;
        void Enter()
        {
            var n = Interlocked.Increment(ref live);
            while (true)
            {
                var snap = Volatile.Read(ref peak);
                if (n <= snap || Interlocked.CompareExchange(ref peak, n, snap) == snap)
                    break;
            }
        }

        void Leave() => Interlocked.Decrement(ref live);

        var stats = new StatsService(
            [new DelaySource("one", Enter, Leave), new DelaySource("two", Enter, Leave)]);
        var scans = await stats.ScanManyAsync(null, force: true, includeArchived: true, CancellationToken.None);
        Assert.Equal(2, scans.Count);
        Assert.True(peak >= 2, $"expected overlapping scans, peak={peak}");
    }

    private sealed class DelaySource : IAgentDataSource
    {
        private readonly Action _enter;
        private readonly Action _leave;

        public DelaySource(string id, Action enter, Action leave)
        {
            AgentId = id;
            _enter = enter;
            _leave = leave;
        }

        public string AgentId { get; }
        public string DisplayName => AgentId;
        public bool CanScan => true;

        public DetectionResult Detect() => new() { Found = true, ResolvedPath = AgentId };

        public PathSetResult SetRootPath(string? path) => new() { Ok = true, Detection = Detect() };

        public async IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options)
        {
            _enter();
            try
            {
                await Task.Delay(250, options.CancellationToken);
            }
            finally
            {
                _leave();
            }

            yield break;
        }

        public AgentSourceStatus GetStatus() => new() { CanScan = true };
    }

    private sealed class NParser : IUtf8LineParser<int>
    {
        public int Parse(ReadOnlySpan<byte> utf8, out bool invalid, out bool skipped)
        {
            skipped = false;
            if (!Utf8JsonWalk.TryStartObject(utf8, out var reader))
            {
                invalid = true;
                return 0;
            }

            var n = 0;
            while (Utf8JsonWalk.NextProperty(ref reader, out var prop))
            {
                if (Utf8JsonWalk.NameEquals(prop, "n"u8))
                    n = (int)Utf8JsonWalk.ReadInt64(ref reader);
                else
                    Utf8JsonWalk.SkipValue(ref reader);
            }

            invalid = false;
            return n;
        }
    }
}
