using System.Reflection;
using AgentTokenStats.Services;

namespace AgentTokenStats.Api;

public static class ApiEndpoints
{
    public const string PrivacyStatement = "数据仅本机处理，只读，不上传。";

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", (StatsService stats) =>
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.1.0";
            var plus = version.IndexOf('+');
            if (plus >= 0)
                version = version[..plus];

            return Results.Json(new MetaDto
            {
                Version = version,
                Product = "Agent Token Stats",
                Privacy = PrivacyStatement,
                License = "MIT"
            });
        });

        api.MapGet("/agents", (StatsService stats) =>
        {
            var items = stats.Sources.Select(source =>
            {
                var d = source.Detect();
                return new AgentListItemDto
                {
                    AgentId = source.AgentId,
                    DisplayName = source.DisplayName,
                    Found = d.Found,
                    ResolvedPath = d.ResolvedPath,
                    CandidateTried = d.CandidateTried,
                    Error = d.Error,
                    ManualPath = d.ManualPath,
                    CanScan = source.CanScan
                };
            })
            .OrderByDescending(a => a.Found)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
            return Results.Json(items);
        });

        api.MapPut("/agents/{agentId}/path", (string agentId, PathBody body, StatsService stats) =>
        {
            var source = stats.GetSource(agentId);
            if (source is null)
                return Results.NotFound();
            var result = source.SetRootPath(body.Path);
            stats.Invalidate(agentId);
            return result.Ok
                ? Results.Json(ToDto(source, result.Detection))
                : Results.BadRequest(new { error = result.Error, agent = ToDto(source, result.Detection) });
        });

        api.MapDelete("/agents/{agentId}/path", (string agentId, StatsService stats) =>
        {
            var source = stats.GetSource(agentId);
            if (source is null)
                return Results.NotFound();
            var result = source.SetRootPath(null);
            stats.Invalidate(agentId);
            return Results.Json(ToDto(source, result.Detection));
        });

        api.MapGet("/stats", async (
            StatsService stats,
            CancellationToken ct,
            string? agents,
            string? range,
            string? from,
            string? to,
            bool includeArchived = true) =>
        {
            var scans = await stats.ScanManyAsync(agents, force: false, includeArchived, ct);
            return Results.Json(DtoMapper.ProjectCombined(scans, range, from, to));
        });

        api.MapPost("/stats/refresh", async (
            StatsService stats,
            CancellationToken ct,
            string? agents,
            bool includeArchived = true) =>
        {
            foreach (var source in stats.ResolveSources(agents))
                stats.Invalidate(source.AgentId);
            var scans = await stats.ScanManyAsync(agents, force: true, includeArchived, ct);
            return Results.Json(DtoMapper.ProjectCombined(scans, "all"));
        });

        api.MapGet("/stats/sessions", async (
            StatsService stats,
            CancellationToken ct,
            string? agents,
            string? range,
            string? from,
            string? to,
            string? q,
            string? sort,
            int page = 1,
            int pageSize = 20,
            bool includeArchived = true) =>
        {
            var scans = await stats.ScanManyAsync(agents, force: false, includeArchived: true, ct);
            return Results.Json(DtoMapper.ProjectSessions(scans, range, includeArchived, q, page, pageSize, from, to, sort));
        });

        api.MapGet("/stats/projects", async (
            StatsService stats,
            CancellationToken ct,
            string? agents,
            string? range,
            string? from,
            string? to,
            string? q,
            string? sort,
            int page = 1,
            int pageSize = 20) =>
        {
            var scans = await stats.ScanManyAsync(agents, force: false, includeArchived: true, ct);
            return Results.Json(DtoMapper.ProjectProjects(scans, range, q, page, pageSize, from, to, sort));
        });

        api.MapPost("/agents/{agentId}/refresh", async (string agentId, StatsService stats, CancellationToken ct) =>
        {
            var source = stats.GetSource(agentId);
            if (source is null)
                return Results.NotFound();
            stats.Invalidate(agentId);
            var detection = source.Detect();
            var snap = await stats.ScanAsync(agentId, force: true, includeArchived: true, ct);
            return Results.Json(DtoMapper.Project(snap, detection.Found, source.CanScan, detection.Error, "all", null));
        });

        api.MapGet("/agents/{agentId}/dashboard", async (
            string agentId,
            StatsService stats,
            CancellationToken ct,
            string? range,
            string? from,
            string? to,
            bool includeArchived = true,
            string? modelSort = "tokens") =>
        {
            var source = stats.GetSource(agentId);
            if (source is null)
                return Results.NotFound();
            var detection = source.Detect();
            try
            {
                var snap = await stats.ScanAsync(agentId, force: false, includeArchived, ct);
                return Results.Json(DtoMapper.Project(snap, detection.Found, source.CanScan, detection.Error, range, modelSort, from, to));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new DashboardDto
                {
                    AgentId = agentId,
                    Found = detection.Found,
                    CanScan = source.CanScan,
                    DataRootPath = detection.ResolvedPath,
                    ManualPath = detection.ManualPath,
                    Error = ex.Message,
                    Summary = DtoMapper.FromBucket(new Models.MetricBucket()),
                    ScannedAt = DateTimeOffset.UtcNow
                });
            }
        });

        api.MapGet("/agents/{agentId}/sessions", async (
            string agentId,
            StatsService stats,
            CancellationToken ct,
            string? range,
            string? from,
            string? to,
            string? q,
            int page = 1,
            int pageSize = 20,
            bool includeArchived = true) =>
        {
            var source = stats.GetSource(agentId);
            if (source is null)
                return Results.NotFound();
            var snap = await stats.ScanAsync(agentId, force: false, includeArchived: true, ct);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [agentId] = source.DisplayName
            };
            return Results.Json(DtoMapper.ProjectSessions(snap, range, includeArchived, q, page, pageSize, from, to, names));
        });
    }

    private static AgentListItemDto ToDto(Adapters.IAgentDataSource source, Models.DetectionResult d) => new()
    {
        AgentId = source.AgentId,
        DisplayName = source.DisplayName,
        Found = d.Found,
        ResolvedPath = d.ResolvedPath,
        CandidateTried = d.CandidateTried,
        Error = d.Error,
        ManualPath = d.ManualPath,
        CanScan = source.CanScan
    };
}
