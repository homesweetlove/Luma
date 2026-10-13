using Microsoft.Data.Sqlite;

namespace Luma.Services;

public sealed class AuditLogService
{
    private readonly string _databasePath;

    public AuditLogService(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        _databasePath = Path.Combine(dataRoot, "luma.db");
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL,
    category TEXT NOT NULL,
    action TEXT NOT NULL,
    target TEXT,
    detail TEXT
);
CREATE INDEX IF NOT EXISTS ix_audit_log_created_at ON audit_log(created_at DESC);
""";
        await command.ExecuteNonQueryAsync();
    }

    public async Task WriteAsync(string category, string action, string? target = null, string? detail = null)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO audit_log(created_at, category, action, target, detail)
VALUES($createdAt, $category, $action, $target, $detail);
""";
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$target", (object?)target ?? DBNull.Value);
        command.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<string>> GetRecentNavigationTargetsAsync(int limit = 6)
    {
        limit = Math.Clamp(limit, 1, 20);
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
SELECT target
FROM audit_log
WHERE category = 'browser'
  AND action = 'navigate'
  AND target IS NOT NULL
  AND target <> 'https://luma.local'
GROUP BY target
ORDER BY MAX(id) DESC
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0) && reader.GetString(0) is { Length: > 0 } value)
                result.Add(value);
        }
        return result;
    }
}
