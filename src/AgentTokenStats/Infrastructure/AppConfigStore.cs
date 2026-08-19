using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentTokenStats.Infrastructure;

public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string _path;
    private AppConfig _config = new();

    public AppConfigStore() : this(Path.Combine(AppDirectories.ConfigDir, "settings.json"))
    {
    }

    public AppConfigStore(string path)
    {
        _path = path;
        Load();
    }

    public string? GetPathOverride(string agentId)
    {
        lock (_gate)
        {
            return _config.PathOverrides.TryGetValue(agentId, out var path) ? path : null;
        }
    }

    public void SetPathOverride(string agentId, string? path)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(path))
                _config.PathOverrides.Remove(agentId);
            else
                _config.PathOverrides[agentId] = path;
            Save_NoLock();
        }
    }

    public IReadOnlyDictionary<string, string> SnapshotOverrides()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_config.PathOverrides, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (parsed is not null)
                _config = parsed;
        }
        catch
        {
            _config = new AppConfig();
        }
    }

    private void Save_NoLock()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_config, JsonOptions));
    }

    private sealed class AppConfig
    {
        public Dictionary<string, string> PathOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
