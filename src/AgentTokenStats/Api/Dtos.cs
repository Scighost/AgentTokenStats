using AgentTokenStats.Adapters;
using AgentTokenStats.Models;
using AgentTokenStats.Services;
using System.Globalization;

namespace AgentTokenStats.Api;

public sealed class AgentListItemDto
{
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public bool Found { get; init; }
    public string? ResolvedPath { get; init; }
    public IReadOnlyList<string> CandidateTried { get; init; } = [];
    public string? Error { get; init; }
    public bool ManualPath { get; init; }
    public bool CanScan { get; init; }
}

public sealed class MetaDto
{
    public required string Version { get; init; }
    public required string Product { get; init; }
    public required string Privacy { get; init; }
    public required string License { get; init; }
}

public sealed class PathBody
{
    public string? Path { get; set; }
}

public sealed class MetricsDto
{
    public long TotalTokens { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long ReasoningTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public int MessageCount { get; init; }
}

public sealed class DayPointDto
{
    public required string Date { get; init; }
    public required MetricsDto Metrics { get; init; }
}

public sealed class ModelPointDto
{
    public required string NormalizedModelKey { get; init; }
    public string ModelId { get; init; } = "";
    public string? ProviderId { get; init; }
    public required MetricsDto Metrics { get; init; }
}

public sealed class SessionRowDto
{
    public required string SessionId { get; init; }
    public string AgentId { get; init; } = "";
    public string? AgentDisplayName { get; init; }
    public string? Title { get; init; }
    public string? ProviderModel { get; init; }
    public string? Cwd { get; init; }
    public bool IsArchived { get; init; }
    public required string StartedAt { get; init; }
    public required string EndedAt { get; init; }
    public required MetricsDto Metrics { get; init; }
}

public sealed class DashboardDto
{
    public required string AgentId { get; init; }
    public bool Found { get; init; }
    public bool CanScan { get; init; }
    public string? DataRootPath { get; init; }
    public bool ManualPath { get; init; }
    public string? Error { get; init; }
    public required MetricsDto Summary { get; init; }
    public IReadOnlyList<DayPointDto> Recent14Days { get; init; } = [];
    public IReadOnlyList<DayPointDto> Top7Days { get; init; } = [];
    public IReadOnlyList<ModelPointDto> HotModels { get; init; } = [];
    public DateTimeOffset ScannedAt { get; init; }
    public int RecordCount { get; init; }
    public int SkippedRecords { get; init; }
    public int SessionCount { get; init; }
}

public sealed class SessionPageDto
{
    public IReadOnlyList<SessionRowDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class SlicePointDto
{
    public required string Label { get; init; }
    public required MetricsDto Metrics { get; init; }
}

public sealed class AgentPointDto
{
    public required string AgentId { get; init; }
    public required string DisplayName { get; init; }
    public bool Found { get; init; }
    public bool CanScan { get; init; }
    public string? DataRootPath { get; init; }
    public string? Error { get; init; }
    public required MetricsDto Metrics { get; init; }
    public int SessionCount { get; init; }
    public int RecordCount { get; init; }
}

public sealed class ProjectRowDto
{
    public required string Name { get; init; }
    public string? Cwd { get; init; }
    public int SessionCount { get; init; }
    public required string LastSeen { get; init; }
    public required MetricsDto Metrics { get; init; }
    public IReadOnlyList<SessionRowDto> Sessions { get; init; } = [];
}

public sealed class ProjectPageDto
{
    public IReadOnlyList<ProjectRowDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class AgentModelCellDto
{
    public required string AgentId { get; init; }
    public required string AgentDisplayName { get; init; }
    public required string ModelId { get; init; }
    public long TotalTokens { get; init; }
}

public sealed class CombinedDashboardDto
{
    public IReadOnlyList<string> AgentIds { get; init; } = [];
    public bool Found { get; init; }
    public bool CanScan { get; init; }
    public string? Error { get; init; }
    public required MetricsDto Summary { get; init; }
    public IReadOnlyList<DayPointDto> Recent14Days { get; init; } = [];
    public IReadOnlyList<DayPointDto> Top7Days { get; init; } = [];
    public IReadOnlyList<DayPointDto> CalendarDays { get; init; } = [];
    public IReadOnlyList<DayPointDto> Timeline { get; init; } = [];
    public IReadOnlyList<SlicePointDto> Months { get; init; } = [];
    public IReadOnlyList<SlicePointDto> Weekdays { get; init; } = [];
    public IReadOnlyList<SlicePointDto> Hours { get; init; } = [];
    public IReadOnlyList<ModelPointDto> HotModels { get; init; } = [];
    public IReadOnlyList<ModelPointDto> Models { get; init; } = [];
    public IReadOnlyList<SlicePointDto> Providers { get; init; } = [];
    public IReadOnlyList<AgentPointDto> Agents { get; init; } = [];
    public IReadOnlyList<AgentModelCellDto> AgentModels { get; init; } = [];
    public IReadOnlyList<SessionRowDto> TopSessions { get; init; } = [];
    public DateTimeOffset ScannedAt { get; init; }
    public int RecordCount { get; init; }
    public int SkippedRecords { get; init; }
    public int SessionCount { get; init; }
}

public static class DtoMapper
{
    public static MetricsDto FromBucket(MetricBucket bucket) => new()
    {
        TotalTokens = bucket.TotalTokens,
        InputTokens = bucket.InputTokens,
        OutputTokens = bucket.OutputTokens,
        ReasoningTokens = bucket.ReasoningTokens,
        CacheReadTokens = bucket.CacheReadTokens,
        CacheWriteTokens = bucket.CacheWriteTokens,
        MessageCount = bucket.MessageCount
    };

    public readonly record struct DateWindow(DateOnly? Start, DateOnly? End)
    {
        public bool Unbounded => Start is null && End is null;

        public bool Contains(DateOnly day) =>
            (Start is null || day >= Start) && (End is null || day <= End);
    }

    public static DateWindow ParseRange(string? range, string? from = null, string? to = null)
    {
        var today = TodayLocal();
        if (TryParseCustomRange(range, from, to, out var custom))
            return custom;

        return range switch
        {
            "7d" or "7" => new DateWindow(today.AddDays(-6), today),
            "30d" or "30" => new DateWindow(today.AddDays(-29), today),
            "90d" or "90" => new DateWindow(today.AddDays(-89), today),
            _ => new DateWindow(null, null)
        };
    }

    private static bool TryParseCustomRange(string? range, string? from, string? to, out DateWindow window)
    {
        window = default;
        var isCustom = !string.IsNullOrWhiteSpace(range) &&
                       range.StartsWith("custom", StringComparison.OrdinalIgnoreCase);
        if (!isCustom && string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
            return false;

        string? startText = from;
        string? endText = to;
        if (isCustom)
        {
            var parts = range!.Split(':', 3, StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                if (string.IsNullOrWhiteSpace(startText)) startText = parts[1];
                if (string.IsNullOrWhiteSpace(endText)) endText = parts[2];
            }
        }

        var start = ParseDay(startText);
        var end = ParseDay(endText);
        if (start is null && end is null)
            return isCustom;

        if (start is not null && end is not null && end < start)
            (start, end) = (end, start);
        window = new DateWindow(start, end);
        return true;
    }

    private static DateOnly? ParseDay(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    private static DateOnly TodayLocal() => DateOnly.FromDateTime(DateTime.Now);

    private static DateOnly LocalDay(DateTimeOffset value) => DateOnly.FromDateTime(value.LocalDateTime);

    public static DashboardDto Project(
        AggregationSnapshot snap,
        bool found,
        bool canScan,
        string? error,
        string? range,
        string? modelSort,
        string? from = null,
        string? to = null)
    {
        var window = ParseRange(range, from, to);
        _ = modelSort;
        var models = RankedModels(snap, window);
        return new DashboardDto
        {
            AgentId = snap.AgentId,
            Found = found,
            CanScan = canScan,
            DataRootPath = snap.DataRootPath,
            ManualPath = snap.ManualPath,
            Error = error,
            Summary = FromBucket(Summarize(snap, window)),
            Recent14Days = RecentDays(snap, window, 14),
            Top7Days = TopDays(snap, window, 7),
            HotModels = SortModels(MergeByFamily(models)).Take(5).ToList(),
            ScannedAt = snap.ScannedAt,
            RecordCount = snap.RecordCount,
            SkippedRecords = snap.SkippedRecords,
            SessionCount = CountSessions(snap, window)
        };
    }

    public static CombinedDashboardDto ProjectCombined(
        IReadOnlyList<AgentScan> scans,
        string? range,
        string? from = null,
        string? to = null)
    {
        var window = ParseRange(range, from, to);
        var names = scans.ToDictionary(
            s => s.Source.AgentId,
            s => s.Source.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        var merged = Merge(scans);
        var models = RankedModels(merged, window);
        var families = SortModels(MergeByFamily(models));
        var providers = RankedProviders(merged, window);
        var today = TodayLocal();
        var found = scans.Any(s => s.Detection.Found);
        var canScan = scans.Any(s => s.Source.CanScan);
        var hasData = merged.RecordCount > 0 || merged.Summary.MessageCount > 0;
        var error = hasData
            ? null
            : string.Join(" ", scans
                .Select(s => s.Detection.Error)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct());

        return new CombinedDashboardDto
        {
            AgentIds = scans.Select(s => s.Source.AgentId).ToList(),
            Found = found,
            CanScan = canScan,
            Error = string.IsNullOrWhiteSpace(error) ? null : error,
            Summary = FromBucket(Summarize(merged, window)),
            Recent14Days = RecentDays(merged, window, 14),
            Top7Days = TopDays(merged, window, 7),
            CalendarDays = CalendarDays(merged, today),
            Timeline = TimelineDays(merged, window, today),
            Months = Months(merged, window),
            Weekdays = Weekdays(merged, window),
            Hours = Hours(merged, window),
            HotModels = families.Take(5).ToList(),
            Models = models,
            Providers = providers,
            Agents = scans
                .Select(s => new AgentPointDto
                {
                    AgentId = s.Source.AgentId,
                    DisplayName = s.Source.DisplayName,
                    Found = s.Detection.Found,
                    CanScan = s.Source.CanScan,
                    DataRootPath = s.Snapshot.DataRootPath,
                    Error = s.Detection.Error,
                    Metrics = FromBucket(Summarize(s.Snapshot, window)),
                    SessionCount = CountSessions(s.Snapshot, window),
                    RecordCount = s.Snapshot.RecordCount
                })
                .OrderByDescending(a => a.Metrics.TotalTokens)
                .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AgentModels = AgentModelCells(scans, window),
            TopSessions = ProjectSessions(merged, range, true, null, 1, 10, from, to, names).Items,
            ScannedAt = merged.ScannedAt,
            RecordCount = merged.RecordCount,
            SkippedRecords = merged.SkippedRecords,
            SessionCount = CountSessions(merged, window)
        };
    }

    public static SessionPageDto ProjectSessions(
        AggregationSnapshot snap,
        string? range,
        bool includeArchived,
        string? query,
        int page,
        int pageSize,
        string? from = null,
        string? to = null,
        IReadOnlyDictionary<string, string>? displayNames = null,
        string? sort = null) =>
        ProjectSessionsFiltered(snap, range, includeArchived, query, page, pageSize, from, to, displayNames, sort);

    public static SessionPageDto ProjectSessions(
        IReadOnlyList<AgentScan> scans,
        string? range,
        bool includeArchived,
        string? query,
        int page,
        int pageSize,
        string? from,
        string? to,
        string? sort)
    {
        var names = scans.ToDictionary(
            s => s.Source.AgentId,
            s => s.Source.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        return ProjectSessionsFiltered(Merge(scans), range, includeArchived, query, page, pageSize, from, to, names, sort);
    }

    private static SessionPageDto ProjectSessionsFiltered(
        AggregationSnapshot snap,
        string? range,
        bool includeArchived,
        string? query,
        int page,
        int pageSize,
        string? from,
        string? to,
        IReadOnlyDictionary<string, string>? displayNames,
        string? sort)
    {
        var window = ParseRange(range, from, to);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var byTime = string.Equals(sort, "time", StringComparison.OrdinalIgnoreCase);

        var filtered = snap.BySession.Values
            .Where(s => includeArchived || !s.IsArchived)
            .Where(s => window.Contains(LocalDay(s.LastSeen)))
            .Where(s => MatchesSession(s, query));

        var ordered = byTime
            ? filtered.OrderByDescending(s => s.LastSeen).ThenByDescending(s => s.Metrics.TotalTokens)
            : filtered.OrderByDescending(s => s.Metrics.TotalTokens).ThenByDescending(s => s.LastSeen);

        var materialized = ordered.ToList();
        return new SessionPageDto
        {
            Items = materialized.Skip((page - 1) * pageSize).Take(pageSize).Select(s => ToRow(s, displayNames)).ToList(),
            Total = materialized.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    private static SessionRowDto ToRow(SessionBucket session, IReadOnlyDictionary<string, string>? displayNames)
    {
        var agentId = string.IsNullOrEmpty(session.AgentId) ? "" : session.AgentId;
        string? name = null;
        displayNames?.TryGetValue(agentId, out name);
        return new SessionRowDto
        {
            SessionId = session.SessionId,
            AgentId = agentId,
            AgentDisplayName = name,
            Title = session.Title,
            ProviderModel = SessionProviderModel(session),
            Cwd = session.Cwd,
            IsArchived = session.IsArchived,
            StartedAt = session.FirstSeen.ToUniversalTime().ToString("O"),
            EndedAt = session.LastSeen.ToUniversalTime().ToString("O"),
            Metrics = FromBucket(session.Metrics)
        };
    }

    private static string? SessionProviderModel(SessionBucket session)
    {
        if (session.ByModel.Count == 0)
            return null;
        var labels = session.ByModel.Values
            .OrderByDescending(m => m.Metrics.TotalTokens)
            .ThenBy(m => m.NormalizedModelKey, StringComparer.OrdinalIgnoreCase)
            .Select(FormatProviderModel)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        return labels.Count == 0 ? null : string.Join(", ", labels);
    }

    private static string? FormatProviderModel(ModelBucket model)
    {
        var modelId = ModelKey.FamilyKey(model.ModelId, model.NormalizedModelKey);
        if (modelId == "(unknown)")
            modelId = "";
        var provider = ProviderOf(model.ProviderId, model.NormalizedModelKey);
        if (string.IsNullOrEmpty(provider) && string.IsNullOrEmpty(modelId))
            return null;
        if (string.IsNullOrEmpty(provider))
            return modelId;
        if (string.IsNullOrEmpty(modelId))
            return provider;
        return $"{provider}/{modelId}";
    }

    private static bool MatchesSession(SessionBucket session, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        return Contains(session.Title, query)
               || Contains(session.Cwd, query)
               || Contains(session.SessionId, query)
               || Contains(session.AgentId, query);
    }

    private static bool Contains(string? text, string query) =>
        !string.IsNullOrEmpty(text) && text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static AggregationSnapshot Merge(IReadOnlyList<AgentScan> scans)
    {
        var merged = new AggregationSnapshot
        {
            AgentId = "all",
            ScannedAt = DateTimeOffset.MinValue
        };

        foreach (var scan in scans)
        {
            var snap = scan.Snapshot;
            CopyInto(merged.Summary, snap.Summary);
            merged.RecordCount += snap.RecordCount;
            merged.SkippedRecords += snap.SkippedRecords;
            if (snap.ScannedAt > merged.ScannedAt)
                merged.ScannedAt = snap.ScannedAt;

            foreach (var (day, bucket) in snap.ByDay)
            {
                if (!merged.ByDay.TryGetValue(day, out var target))
                {
                    target = new MetricBucket();
                    merged.ByDay[day] = target;
                }

                CopyInto(target, bucket);
            }

            foreach (var (key, model) in snap.ByModel)
            {
                if (!merged.ByModel.TryGetValue(key, out var target))
                {
                    target = new ModelBucket
                    {
                        NormalizedModelKey = model.NormalizedModelKey,
                        ModelId = model.ModelId,
                        ProviderId = model.ProviderId
                    };
                    merged.ByModel[key] = target;
                }

                CopyInto(target.Metrics, model.Metrics);
            }

            foreach (var (key, bucket) in snap.ByDayModel)
            {
                if (!merged.ByDayModel.TryGetValue(key, out var target))
                {
                    target = new MetricBucket();
                    merged.ByDayModel[key] = target;
                }

                CopyInto(target, bucket);
            }

            foreach (var (key, bucket) in snap.ByDayHour)
            {
                if (!merged.ByDayHour.TryGetValue(key, out var target))
                {
                    target = new MetricBucket();
                    merged.ByDayHour[key] = target;
                }

                CopyInto(target, bucket);
            }

            foreach (var session in snap.BySession.Values)
            {
                var id = string.IsNullOrEmpty(session.AgentId)
                    ? session.SessionId
                    : session.AgentId + ":" + session.SessionId;
                merged.BySession[id] = session;
            }
        }

        if (merged.ScannedAt == DateTimeOffset.MinValue)
            merged.ScannedAt = DateTimeOffset.UtcNow;
        return merged;
    }

    private static MetricBucket Summarize(AggregationSnapshot snap, DateWindow window)
    {
        var summary = new MetricBucket();
        if (window.Unbounded)
        {
            CopyInto(summary, snap.Summary);
            return summary;
        }

        foreach (var (day, bucket) in snap.ByDay)
        {
            if (window.Contains(day))
                CopyInto(summary, bucket);
        }

        return summary;
    }

    private static List<DayPointDto> ConsecutiveDays(AggregationSnapshot snap, DateOnly start, DateOnly end)
    {
        var list = new List<DayPointDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            snap.ByDay.TryGetValue(day, out var bucket);
            list.Add(new DayPointDto
            {
                Date = day.ToString("yyyy-MM-dd"),
                Metrics = FromBucket(bucket ?? new MetricBucket())
            });
        }

        return list;
    }

    private static List<DayPointDto> RecentDays(AggregationSnapshot snap, DateWindow window, int days)
    {
        var today = TodayLocal();
        var end = window.End ?? today;
        var start = end.AddDays(1 - days);
        if (window.Start is { } bound && start < bound)
            start = bound;
        return ConsecutiveDays(snap, start, end);
    }

    private static List<DayPointDto> TopDays(AggregationSnapshot snap, DateWindow window, int take) =>
        snap.ByDay
            .Where(kv => window.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value.TotalTokens)
            .ThenByDescending(kv => kv.Key)
            .Take(take)
            .Select(kv => new DayPointDto
            {
                Date = kv.Key.ToString("yyyy-MM-dd"),
                Metrics = FromBucket(kv.Value)
            })
            .ToList();

    private static List<DayPointDto> CalendarDays(AggregationSnapshot snap, DateOnly today)
    {
        var start = today.AddDays(-364);
        while (start.DayOfWeek != DayOfWeek.Monday)
            start = start.AddDays(-1);
        return ConsecutiveDays(snap, start, today);
    }

    private static List<DayPointDto> TimelineDays(AggregationSnapshot snap, DateWindow window, DateOnly today)
    {
        DateOnly start;
        DateOnly end;
        if (window.Unbounded)
        {
            if (snap.ByDay.Count == 0)
                return [];
            start = snap.ByDay.Keys.Min();
            end = snap.ByDay.Keys.Max();
            if (end.DayNumber - start.DayNumber > 180)
                return [];
        }
        else
        {
            end = window.End ?? today;
            start = window.Start ?? end.AddDays(-89);
            if (end.DayNumber - start.DayNumber > 180)
                return [];
        }

        return ConsecutiveDays(snap, start, end);
    }

    private static List<SlicePointDto> Months(AggregationSnapshot snap, DateWindow window)
    {
        var rolled = new Dictionary<string, MetricBucket>(StringComparer.Ordinal);
        foreach (var (day, bucket) in snap.ByDay)
        {
            if (!window.Contains(day))
                continue;
            var key = day.ToString("yyyy-MM");
            if (!rolled.TryGetValue(key, out var target))
            {
                target = new MetricBucket();
                rolled[key] = target;
            }

            CopyInto(target, bucket);
        }

        return rolled
            .OrderBy(kv => kv.Key)
            .Select(kv => new SlicePointDto { Label = kv.Key, Metrics = FromBucket(kv.Value) })
            .ToList();
    }

    private static List<SlicePointDto> Weekdays(AggregationSnapshot snap, DateWindow window)
    {
        var buckets = Enumerable.Range(0, 7).Select(_ => new MetricBucket()).ToArray();
        foreach (var (day, bucket) in snap.ByDay)
        {
            if (!window.Contains(day))
                continue;
            var index = ((int)day.DayOfWeek + 6) % 7;
            CopyInto(buckets[index], bucket);
        }

        string[] labels = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
        return labels
            .Select((label, i) => new SlicePointDto { Label = label, Metrics = FromBucket(buckets[i]) })
            .ToList();
    }

    private static List<SlicePointDto> Hours(AggregationSnapshot snap, DateWindow window)
    {
        var buckets = Enumerable.Range(0, 24).Select(_ => new MetricBucket()).ToArray();
        foreach (var ((day, hour), bucket) in snap.ByDayHour)
        {
            if (!window.Contains(day) || hour is < 0 or > 23)
                continue;
            CopyInto(buckets[hour], bucket);
        }

        return buckets
            .Select((bucket, hour) => new SlicePointDto
            {
                Label = hour.ToString("00"),
                Metrics = FromBucket(bucket)
            })
            .ToList();
    }

    public static ProjectPageDto ProjectProjects(
        IReadOnlyList<AgentScan> scans,
        string? range,
        string? query,
        int page,
        int pageSize,
        string? from,
        string? to,
        string? sort)
    {
        var window = ParseRange(range, from, to);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var byTime = string.Equals(sort, "time", StringComparison.OrdinalIgnoreCase);
        var names = scans.ToDictionary(
            s => s.Source.AgentId,
            s => s.Source.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        var merged = Merge(scans);

        var rolled = new Dictionary<string, (string? Cwd, int Sessions, DateTimeOffset LastSeen, MetricBucket Metrics, List<SessionBucket> Items)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var session in merged.BySession.Values)
        {
            if (!window.Contains(LocalDay(session.LastSeen)))
                continue;
            var name = ProjectName(session.Cwd);
            if (!string.IsNullOrWhiteSpace(query) &&
                !Contains(name, query) &&
                !Contains(session.Cwd, query))
                continue;

            var key = string.IsNullOrEmpty(session.Cwd) ? "" : session.Cwd;
            if (!rolled.TryGetValue(key, out var row))
            {
                row = (session.Cwd, 0, session.LastSeen, new MetricBucket(), []);
                rolled[key] = row;
            }

            row.Sessions += 1;
            if (session.LastSeen > row.LastSeen)
                row.LastSeen = session.LastSeen;
            CopyInto(row.Metrics, session.Metrics);
            row.Items.Add(session);
            rolled[key] = row;
        }

        var ordered = byTime
            ? rolled.OrderByDescending(kv => kv.Value.LastSeen).ThenByDescending(kv => kv.Value.Metrics.TotalTokens)
            : rolled.OrderByDescending(kv => kv.Value.Metrics.TotalTokens).ThenByDescending(kv => kv.Value.LastSeen);

        var materialized = ordered.ToList();
        return new ProjectPageDto
        {
            Items = materialized
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(kv => new ProjectRowDto
                {
                    Name = ProjectName(kv.Value.Cwd),
                    Cwd = kv.Value.Cwd,
                    SessionCount = kv.Value.Sessions,
                    LastSeen = kv.Value.LastSeen.ToUniversalTime().ToString("O"),
                    Metrics = FromBucket(kv.Value.Metrics),
                    Sessions = kv.Value.Items
                        .OrderByDescending(s => s.Metrics.TotalTokens)
                        .ThenByDescending(s => s.LastSeen)
                        .Select(s => ToRow(s, names))
                        .ToList()
                })
                .ToList(),
            Total = materialized.Count,
            Page = page,
            PageSize = pageSize
        };
    }

    private static string ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return "—";
        var parts = cwd.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? cwd : parts[^1];
    }

    private static List<AgentModelCellDto> AgentModelCells(IReadOnlyList<AgentScan> scans, DateWindow window) =>
        scans
            .Where(s => s.Detection.Found)
            .SelectMany(s => SortModels(MergeByFamily(RankedModels(s.Snapshot, window)))
                .Where(m => m.Metrics.TotalTokens > 0)
                .Select(m => new AgentModelCellDto
                {
                    AgentId = s.Source.AgentId,
                    AgentDisplayName = s.Source.DisplayName,
                    ModelId = string.IsNullOrEmpty(m.ModelId) ? m.NormalizedModelKey : m.ModelId,
                    TotalTokens = m.Metrics.TotalTokens
                }))
            .ToList();

    private static List<ModelPointDto> RankedModels(AggregationSnapshot snap, DateWindow window)
    {
        IEnumerable<ModelPointDto> models;
        if (window.Unbounded)
        {
            models = snap.ByModel.Values.Select(m => new ModelPointDto
            {
                NormalizedModelKey = m.NormalizedModelKey,
                ModelId = m.ModelId,
                ProviderId = m.ProviderId,
                Metrics = FromBucket(m.Metrics)
            });
        }
        else
        {
            var rolled = new Dictionary<string, (ModelBucket Src, MetricBucket Metrics)>(StringComparer.OrdinalIgnoreCase);
            foreach (var ((day, key), bucket) in snap.ByDayModel)
            {
                if (!window.Contains(day))
                    continue;
                if (!rolled.TryGetValue(key, out var pair))
                {
                    snap.ByModel.TryGetValue(key, out var src);
                    pair = (src ?? new ModelBucket { NormalizedModelKey = key }, new MetricBucket());
                    rolled[key] = pair;
                }

                CopyInto(pair.Metrics, bucket);
            }

            models = rolled.Select(kv => new ModelPointDto
            {
                NormalizedModelKey = kv.Key,
                ModelId = kv.Value.Src.ModelId,
                ProviderId = kv.Value.Src.ProviderId,
                Metrics = FromBucket(kv.Value.Metrics)
            });
        }

        return SortModels(models);
    }

    private static List<ModelPointDto> SortModels(IEnumerable<ModelPointDto> models) =>
        models
            .OrderByDescending(m => m.Metrics.TotalTokens)
            .ThenBy(m => m.NormalizedModelKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<SlicePointDto> RankedProviders(AggregationSnapshot snap, DateWindow window)
    {
        var rolled = new Dictionary<string, MetricBucket>(StringComparer.OrdinalIgnoreCase);
        void Add(string? provider, MetricBucket bucket)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return;
            if (!rolled.TryGetValue(provider, out var target))
            {
                target = new MetricBucket();
                rolled[provider] = target;
            }

            CopyInto(target, bucket);
        }

        if (window.Unbounded)
        {
            foreach (var model in snap.ByModel.Values)
                Add(ProviderOf(model.ProviderId, model.NormalizedModelKey), model.Metrics);
        }
        else
        {
            foreach (var ((day, key), bucket) in snap.ByDayModel)
            {
                if (!window.Contains(day))
                    continue;
                snap.ByModel.TryGetValue(key, out var src);
                Add(ProviderOf(src?.ProviderId, key), bucket);
            }
        }

        return rolled
            .OrderByDescending(kv => kv.Value.TotalTokens)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new SlicePointDto { Label = kv.Key, Metrics = FromBucket(kv.Value) })
            .ToList();
    }

    private static string? ProviderOf(string? providerId, string? key)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
            return providerId.Trim();
        var colon = (key ?? "").IndexOf(':');
        return colon > 0 ? key![..colon] : null;
    }

    private static string? JoinProviders(HashSet<string> providers) =>
        providers.Count == 0
            ? null
            : string.Join(", ", providers.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

    private static int CountSessions(AggregationSnapshot snap, DateWindow window) =>
        snap.BySession.Values.Count(s => window.Contains(LocalDay(s.LastSeen)));

    private static IEnumerable<ModelPointDto> MergeByFamily(IEnumerable<ModelPointDto> models)
    {
        var merged = new Dictionary<string, (MetricBucket Metrics, HashSet<string> Providers)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            var key = ModelKey.FamilyKey(model.ModelId, model.NormalizedModelKey);
            if (!merged.TryGetValue(key, out var row))
            {
                row = (new MetricBucket(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                merged[key] = row;
            }

            if (!string.IsNullOrEmpty(model.ProviderId))
                row.Providers.Add(model.ProviderId);
            CopyInto(row.Metrics, ToBucket(model.Metrics));
            merged[key] = row;
        }

        return merged.Select(kv => new ModelPointDto
        {
            NormalizedModelKey = kv.Key,
            ModelId = kv.Key,
            ProviderId = JoinProviders(kv.Value.Providers),
            Metrics = FromBucket(kv.Value.Metrics)
        });
    }

    private static MetricBucket ToBucket(MetricsDto metrics) => new()
    {
        InputTokens = metrics.InputTokens,
        OutputTokens = metrics.OutputTokens,
        ReasoningTokens = metrics.ReasoningTokens,
        CacheReadTokens = metrics.CacheReadTokens,
        CacheWriteTokens = metrics.CacheWriteTokens,
        MessageCount = metrics.MessageCount
    };

    private static void CopyInto(MetricBucket target, MetricBucket source)
    {
        target.InputTokens += source.InputTokens;
        target.OutputTokens += source.OutputTokens;
        target.ReasoningTokens += source.ReasoningTokens;
        target.CacheReadTokens += source.CacheReadTokens;
        target.CacheWriteTokens += source.CacheWriteTokens;
        target.MessageCount += source.MessageCount;
    }
}
