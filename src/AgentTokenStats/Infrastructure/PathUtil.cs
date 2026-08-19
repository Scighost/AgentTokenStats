namespace AgentTokenStats.Infrastructure;

public static class PathUtil
{
    public static string UserHome() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Expand(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path == "~")
            return UserHome();

        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.GetFullPath(Path.Combine(UserHome(), path[2..]));

        return Path.GetFullPath(path);
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(Expand(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool CanReadFile(string path)
    {
        try
        {
            using var _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public static class AppDirectories
{
    public static string ConfigDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgentTokenStats");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var root = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(PathUtil.UserHome(), ".config")
                : PathUtil.Expand(xdg);
            return Path.Combine(root, "agent-token-stats");
        }
    }
}
