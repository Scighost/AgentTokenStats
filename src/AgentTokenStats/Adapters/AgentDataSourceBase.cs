using AgentTokenStats.Infrastructure;
using AgentTokenStats.Models;

namespace AgentTokenStats.Adapters;

public abstract class AgentDataSourceBase : IAgentDataSource
{
    private readonly AppConfigStore _config;
    private string? _manualRoot;
    private string? _lastError;

    protected AgentDataSourceBase(AppConfigStore config)
    {
        _config = config;
        var stored = config.GetPathOverride(AgentId);
        if (!string.IsNullOrWhiteSpace(stored))
            _manualRoot = PathUtil.Normalize(stored);
    }

    public abstract string AgentId { get; }
    public abstract string DisplayName { get; }
    public virtual bool CanScan => false;

    public DetectionResult Detect()
    {
        var tried = new List<string>();
        if (!string.IsNullOrWhiteSpace(_manualRoot))
        {
            tried.Add(_manualRoot);
            if (IsValidRoot(_manualRoot, out var error))
            {
                _lastError = null;
                return new DetectionResult
                {
                    Found = true,
                    ResolvedPath = _manualRoot,
                    CandidateTried = tried,
                    ManualPath = true
                };
            }

            _lastError = error ?? "手动路径不可用";
            return new DetectionResult
            {
                Found = false,
                ResolvedPath = _manualRoot,
                CandidateTried = tried,
                Error = _lastError,
                ManualPath = true
            };
        }

        foreach (var candidate in EnumerateCandidates())
        {
            var path = PathUtil.Normalize(candidate);
            if (tried.Contains(path, StringComparer.OrdinalIgnoreCase))
                continue;
            tried.Add(path);
            if (IsValidRoot(path, out _))
            {
                _lastError = null;
                return new DetectionResult
                {
                    Found = true,
                    ResolvedPath = path,
                    CandidateTried = tried,
                    ManualPath = false
                };
            }
        }

        _lastError = "未找到有效数据目录";
        return new DetectionResult
        {
            Found = false,
            CandidateTried = tried,
            Error = _lastError
        };
    }

    public PathSetResult SetRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _manualRoot = null;
            _config.SetPathOverride(AgentId, null);
            return new PathSetResult { Ok = true, Detection = Detect() };
        }

        var normalized = PathUtil.Normalize(path);
        if (!IsValidRoot(normalized, out var error))
        {
            return new PathSetResult
            {
                Ok = false,
                Error = error ?? "路径无效或不可读",
                Detection = Detect()
            };
        }

        _manualRoot = normalized;
        _config.SetPathOverride(AgentId, normalized);
        return new PathSetResult { Ok = true, Detection = Detect() };
    }

    public virtual async IAsyncEnumerable<UnifiedUsageRecord> Scan(ScanOptions options)
    {
        await Task.CompletedTask;
        yield break;
    }

    public AgentSourceStatus GetStatus()
    {
        var detection = Detect();
        return new AgentSourceStatus
        {
            DataRootPath = detection.ResolvedPath,
            ManualPath = detection.ManualPath,
            CanScan = CanScan
        };
    }

    protected abstract IEnumerable<string> EnumerateCandidates();
    protected abstract bool IsValidRoot(string path, out string? error);

    protected static bool DirectoryReadable(string path, out string? error)
    {
        if (!Directory.Exists(path))
        {
            error = "目录不存在";
            return false;
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Any();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
