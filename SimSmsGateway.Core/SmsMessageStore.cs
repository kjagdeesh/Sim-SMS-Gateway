using Microsoft.Data.Sqlite;

namespace SimSmsGateway.Core;

public static class SmsStatus
{
    public const string Success = "success";
    public const string Failed = "failed";
}

public sealed record SmsMessageRecord(
    long Id,
    string? BatchId,
    string Destination,
    string Message,
    string Status,
    string? Error,
    DateTimeOffset CreatedAt);

public sealed record SmsStats(int Total, int Success, int Failed);

/// <summary>SQLite-backed history of every message the gateway attempted to send.</summary>
public sealed class SmsMessageStore : IDisposable
{
    private readonly object _gate = new();
    private readonly SqliteConnection _connection;

    /// <param name="databasePath">A file path, or null for an in-memory database.</param>
    public SmsMessageStore(string? databasePath = null)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath ?? ":memory:",
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                batch_id TEXT NULL,
                destination TEXT NOT NULL,
                message TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('success', 'failed')),
                error TEXT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_messages_status ON messages(status);
            CREATE INDEX IF NOT EXISTS ix_messages_created ON messages(created_at DESC, id DESC);
            """;
        command.ExecuteNonQuery();
    }

    public SmsMessageRecord Add(string? batchId, string destination, string message, string status, string? error)
    {
        var createdAt = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO messages (batch_id, destination, message, status, error, created_at)
                VALUES ($batch, $destination, $message, $status, $error, $created);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$batch", (object?)batchId ?? DBNull.Value);
            command.Parameters.AddWithValue("$destination", destination);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
            var id = (long)command.ExecuteScalar()!;
            return new SmsMessageRecord(id, batchId, destination, message, status, error, createdAt);
        }
    }

    public IReadOnlyList<SmsMessageRecord> List(string? status, int limit, int offset)
    {
        var records = new List<SmsMessageRecord>();
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, batch_id, destination, message, status, error, created_at
                FROM messages
                WHERE $status IS NULL OR status = $status
                ORDER BY id DESC
                LIMIT $limit OFFSET $offset;
                """;
            command.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new SmsMessageRecord(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return records;
    }

    public SmsStats GetStats()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*),
                       COALESCE(SUM(status = 'success'), 0),
                       COALESCE(SUM(status = 'failed'), 0)
                FROM messages;
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            return new SmsStats(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}
