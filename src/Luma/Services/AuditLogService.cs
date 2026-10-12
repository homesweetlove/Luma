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
}
