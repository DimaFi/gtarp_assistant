using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.Knowledge;

public static class KnowledgeDatabaseMigrator
{
    public const int CurrentVersion = 3;

    public static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
        var version = Convert.ToInt32(await ScalarAsync(connection, null, "PRAGMA user_version;", cancellationToken));
        if (version > CurrentVersion)
            throw new InvalidDataException($"Knowledge DB version {version} is newer than supported version {CurrentVersion}.");

        using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS articles(
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                project TEXT NOT NULL,
                server_scope TEXT NOT NULL,
                summary TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                verified INTEGER NOT NULL,
                demo INTEGER NOT NULL,
                valid_until TEXT NULL,
                source_title TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS facts(id TEXT PRIMARY KEY, article_id TEXT NOT NULL, text TEXT NOT NULL, verified INTEGER NOT NULL, FOREIGN KEY(article_id) REFERENCES articles(id));
            CREATE TABLE IF NOT EXISTS aliases(article_id TEXT NOT NULL, alias TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS article_scopes(article_id TEXT NOT NULL, server TEXT NOT NULL, PRIMARY KEY(article_id, server));
            CREATE TABLE IF NOT EXISTS prepared_answers(article_id TEXT NOT NULL, pattern TEXT NOT NULL, answer TEXT NOT NULL);
            CREATE VIRTUAL TABLE IF NOT EXISTS article_fts USING fts5(article_id UNINDEXED, title, aliases, summary, tokenize='unicode61');
            CREATE VIRTUAL TABLE IF NOT EXISTS fact_fts USING fts5(article_id UNINDEXED, fact_id UNINDEXED, text, tokenize='unicode61');
            """, cancellationToken);

        await EnsureColumnAsync(connection, transaction, "articles", "source_url", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "articles", "article_version", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "articles", "verified_by", "TEXT NULL", cancellationToken);
        await BackfillScopesAsync(connection, transaction, cancellationToken);
        await ExecuteAsync(connection, transaction, "INSERT INTO fact_fts(article_id,fact_id,text) SELECT f.article_id,f.id,f.text FROM facts f WHERE NOT EXISTS(SELECT 1 FROM fact_fts x WHERE x.fact_id=f.id);", cancellationToken);
        await ExecuteAsync(connection, transaction, $"PRAGMA user_version={CurrentVersion};", cancellationToken);
        transaction.Commit();
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string column, string definition, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        }

        await reader.DisposeAsync();
        await ExecuteAsync(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", cancellationToken);
    }

    private static async Task BackfillScopesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var rows = new List<(string ArticleId, string Scope)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id, server_scope FROM articles WHERE NOT EXISTS (SELECT 1 FROM article_scopes WHERE article_id=articles.id);";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (articleId, scope) in rows)
        {
            foreach (var server in scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO article_scopes(article_id, server) VALUES($article,$server);";
                insert.Parameters.AddWithValue("$article", articleId);
                insert.Parameters.AddWithValue("$server", server);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
