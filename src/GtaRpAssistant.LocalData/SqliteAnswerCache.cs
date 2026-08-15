using System.Globalization;
using System.Text.Json;
using GtaRpAssistant.Core;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.LocalData;

public sealed class SqliteAnswerCache : IAnswerCache, IDisposable
{
    private const int MaximumEntries = 1000;
    private const int MaximumPayloadCharacters = 64 * 1024;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteAnswerCache(string connectionString)
    {
        _connectionString = connectionString;
        EnsureDirectory();
        InitializeSchema();
    }

    public async Task<AnswerCacheEntry?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json,created_at,expires_at,hit_count FROM answer_cache WHERE cache_key=$key;";
            command.Parameters.AddWithValue("$key", key);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;

            var expiresAt = ParseDate(reader.GetString(2));
            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                await reader.DisposeAsync();
                await DeleteAsync(connection, key, cancellationToken);
                return null;
            }

            AssistantAnswer? answer;
            try { answer = JsonSerializer.Deserialize<AssistantAnswer>(reader.GetString(0)); }
            catch (JsonException)
            {
                await reader.DisposeAsync();
                await DeleteAsync(connection, key, cancellationToken);
                return null;
            }
            if (answer is null || answer.Decision != AnswerDecision.Show) return null;
            var createdAt = ParseDate(reader.GetString(1));
            var hitCount = reader.GetInt32(3) + 1;
            await reader.DisposeAsync();

            await using var touch = connection.CreateCommand();
            touch.CommandText = "UPDATE answer_cache SET last_accessed_at=$now,hit_count=$hits WHERE cache_key=$key;";
            touch.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            touch.Parameters.AddWithValue("$hits", hitCount);
            touch.Parameters.AddWithValue("$key", key);
            await touch.ExecuteNonQueryAsync(cancellationToken);
            return new(answer, createdAt, expiresAt, hitCount);
        }
        finally { _gate.Release(); }
    }

    public async Task StoreAsync(string key, AssistantAnswer answer, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        if (answer.Decision != AnswerDecision.Show) return;
        var payload = JsonSerializer.Serialize(answer);
        if (payload.Length > MaximumPayloadCharacters) return;
        var boundedTtl = TimeSpan.FromMinutes(Math.Clamp(ttl.TotalMinutes, 1, TimeSpan.FromDays(30).TotalMinutes));
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO answer_cache(cache_key,payload_json,created_at,expires_at,last_accessed_at,hit_count)
                    VALUES($key,$payload,$created,$expires,$accessed,0)
                    ON CONFLICT(cache_key) DO UPDATE SET
                        payload_json=excluded.payload_json,
                        created_at=excluded.created_at,
                        expires_at=excluded.expires_at,
                        last_accessed_at=excluded.last_accessed_at,
                        hit_count=0;
                    """;
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$payload", payload);
                command.Parameters.AddWithValue("$created", now.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$expires", now.Add(boundedTtl).ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$accessed", now.ToString("O", CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var prune = connection.CreateCommand();
            prune.CommandText = """
                DELETE FROM answer_cache WHERE expires_at <= $now;
                DELETE FROM answer_cache WHERE cache_key IN (
                    SELECT cache_key FROM answer_cache ORDER BY last_accessed_at DESC LIMIT -1 OFFSET $capacity
                );
                """;
            prune.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
            prune.Parameters.AddWithValue("$capacity", MaximumEntries);
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM answer_cache;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private void InitializeSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS answer_cache(
                cache_key TEXT PRIMARY KEY,
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                last_accessed_at TEXT NOT NULL,
                hit_count INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_answer_cache_expiry ON answer_cache(expires_at);
            CREATE INDEX IF NOT EXISTS idx_answer_cache_access ON answer_cache(last_accessed_at DESC);
            """;
        command.ExecuteNonQuery();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task DeleteAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM answer_cache WHERE cache_key=$key;";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureDirectory()
    {
        var dataSource = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        if (dataSource is "" or ":memory:") return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static void ValidateKey(string key)
    {
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Cache key must be a SHA-256 hex string.", nameof(key));
    }

    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public void Dispose() => _gate.Dispose();
}
