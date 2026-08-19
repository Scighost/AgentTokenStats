using Microsoft.Data.Sqlite;

namespace AgentTokenStats.Infrastructure;

public static class SqliteReadonly
{
    public const int BusyRetries = 6;

    public static SqliteConnection Open(string dbPath, bool immutable = false)
    {
        SqliteConnection conn;
        if (immutable)
        {
            var uri = dbPath.Replace('\\', '/');
            if (uri.Length >= 2 && uri[1] == ':')
                uri = "/" + uri;
            conn = new SqliteConnection($"Data Source=file:{uri}?mode=ro&immutable=1;Pooling=False;Default Timeout=5");
        }
        else
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString();
            conn = new SqliteConnection(cs);
        }

        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA query_only = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    public static Task<SqliteConnection> OpenAsync(string dbPath, CancellationToken cancellationToken) =>
        WithBusyRetry(
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(Open(dbPath));
            },
            cancellationToken);

    public static async Task<T> WithBusyRetry<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < BusyRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
            {
                last = ex;
                await Task.Delay(50 << attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Agent 占用中，稍后重试", last);
    }
}
