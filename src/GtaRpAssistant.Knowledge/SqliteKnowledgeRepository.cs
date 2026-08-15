using GtaRpAssistant.Core;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.Knowledge;

public sealed class SqliteKnowledgeRepository(string connectionString) : IKnowledgeRepository
{
    public async Task RebuildAsync(IEnumerable<KnowledgePackArticle> articles, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await KnowledgeDatabaseMigrator.MigrateAsync(connection, cancellationToken);
        foreach (var table in new[] { "facts", "aliases", "article_scopes", "prepared_answers", "article_fts", "fact_fts", "articles" })
        {
            await using var clear = connection.CreateCommand();
            clear.CommandText = $"DELETE FROM {table}";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var article in articles) await UpsertAsync(connection, article, cancellationToken);
    }

    public async Task InitializeAsync(IEnumerable<KnowledgePackArticle> articles, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await KnowledgeDatabaseMigrator.MigrateAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var article in articles) await UpsertAsync(connection, article, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpsertAsync(SqliteConnection connection, KnowledgePackArticle a, CancellationToken ct)
    {
        foreach (var sql in new[] { "DELETE FROM facts WHERE article_id=$id", "DELETE FROM aliases WHERE article_id=$id", "DELETE FROM article_scopes WHERE article_id=$id", "DELETE FROM prepared_answers WHERE article_id=$id", "DELETE FROM article_fts WHERE article_id=$id", "DELETE FROM fact_fts WHERE article_id=$id", "DELETE FROM articles WHERE id=$id" })
        { await using var delete = connection.CreateCommand(); delete.CommandText = sql; delete.Parameters.AddWithValue("$id", a.Id); await delete.ExecuteNonQueryAsync(ct); }
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO articles(id,title,project,server_scope,summary,updated_at,verified,demo,valid_until,source_title,source_url,article_version,verified_by) VALUES($id,$title,$project,$scope,$summary,$updated,$verified,$demo,$valid,$source,$sourceUrl,$version,$verifiedBy)";
            cmd.Parameters.AddWithValue("$id", a.Id); cmd.Parameters.AddWithValue("$title", a.Title); cmd.Parameters.AddWithValue("$project", a.Project); cmd.Parameters.AddWithValue("$scope", string.Join(',', a.ServerScope)); cmd.Parameters.AddWithValue("$summary", a.Summary); cmd.Parameters.AddWithValue("$updated", a.UpdatedAt.ToString("O")); cmd.Parameters.AddWithValue("$verified", a.Verified); cmd.Parameters.AddWithValue("$demo", a.Demo); cmd.Parameters.AddWithValue("$valid", a.ValidUntil?.ToString("O") ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("$source", a.Source.Title); cmd.Parameters.AddWithValue("$sourceUrl", a.Source.Url ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("$version", a.Version); cmd.Parameters.AddWithValue("$verifiedBy", a.VerifiedBy ?? (object)DBNull.Value); await cmd.ExecuteNonQueryAsync(ct);
        }
        foreach (var fact in a.Facts)
        {
            await using (var cmd = connection.CreateCommand()) { cmd.CommandText = "INSERT INTO facts VALUES($id,$article,$text,$verified)"; cmd.Parameters.AddWithValue("$id", fact.Id); cmd.Parameters.AddWithValue("$article", a.Id); cmd.Parameters.AddWithValue("$text", fact.Text); cmd.Parameters.AddWithValue("$verified", fact.Verified); await cmd.ExecuteNonQueryAsync(ct); }
            await using (var factIndex = connection.CreateCommand()) { factIndex.CommandText = "INSERT INTO fact_fts(article_id,fact_id,text) VALUES($article,$id,$text)"; factIndex.Parameters.AddWithValue("$article", a.Id); factIndex.Parameters.AddWithValue("$id", fact.Id); factIndex.Parameters.AddWithValue("$text", fact.Text); await factIndex.ExecuteNonQueryAsync(ct); }
        }
        foreach (var alias in a.Aliases) { await using var cmd = connection.CreateCommand(); cmd.CommandText = "INSERT INTO aliases VALUES($article,$alias)"; cmd.Parameters.AddWithValue("$article", a.Id); cmd.Parameters.AddWithValue("$alias", Normalize(alias)); await cmd.ExecuteNonQueryAsync(ct); }
        foreach (var scope in a.ServerScope) { await using var cmd = connection.CreateCommand(); cmd.CommandText = "INSERT INTO article_scopes VALUES($article,$server)"; cmd.Parameters.AddWithValue("$article", a.Id); cmd.Parameters.AddWithValue("$server", scope); await cmd.ExecuteNonQueryAsync(ct); }
        foreach (var p in a.PreparedAnswers) { await using var cmd = connection.CreateCommand(); cmd.CommandText = "INSERT INTO prepared_answers VALUES($article,$pattern,$answer)"; cmd.Parameters.AddWithValue("$article", a.Id); cmd.Parameters.AddWithValue("$pattern", Normalize(p.QuestionPattern)); cmd.Parameters.AddWithValue("$answer", p.Answer); await cmd.ExecuteNonQueryAsync(ct); }
        await using var fts = connection.CreateCommand(); fts.CommandText = "INSERT INTO article_fts VALUES($id,$title,$aliases,$summary)"; fts.Parameters.AddWithValue("$id", a.Id); fts.Parameters.AddWithValue("$title", a.Title); fts.Parameters.AddWithValue("$aliases", string.Join(' ', a.Aliases)); fts.Parameters.AddWithValue("$summary", a.Summary); await fts.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken)
    {
        var normalized = Normalize(query.Text);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var ids = new List<(string Id, double Score, string? Prepared)>();
        await using (var exact = connection.CreateCommand())
        {
            exact.CommandText = "SELECT article_id, 1.0, answer FROM prepared_answers WHERE pattern=$q UNION ALL SELECT article_id, 0.95, NULL FROM aliases WHERE alias=$q ORDER BY 2 DESC LIMIT $limit";
            exact.Parameters.AddWithValue("$q", normalized); exact.Parameters.AddWithValue("$limit", query.Limit);
            await using var reader = await exact.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) ids.Add((reader.GetString(0), reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        if (ids.Count == 0)
        {
            var terms = string.Join(" OR ", SearchTokens(normalized).Select(x => $"\"{x.Replace("\"", "\"\"")}\"*"));
            if (terms.Length > 0) { await using var fts = connection.CreateCommand(); fts.CommandText = "SELECT article_id, MIN(rank) FROM (SELECT article_id, bm25(article_fts) AS rank FROM article_fts WHERE article_fts MATCH $q UNION ALL SELECT article_id, bm25(fact_fts) AS rank FROM fact_fts WHERE fact_fts MATCH $q) GROUP BY article_id ORDER BY MIN(rank) LIMIT $limit"; fts.Parameters.AddWithValue("$q", terms); fts.Parameters.AddWithValue("$limit", query.Limit); await using var reader = await fts.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) ids.Add((reader.GetString(0), Math.Clamp(0.75 - reader.GetDouble(1) / 100, 0, 0.9), null)); }
        }
        var results = new List<KnowledgeMatch>();
        foreach (var item in ids.DistinctBy(x => x.Id))
        {
            if (item.Score < 0.95)
            {
                var searchText = await ReadSearchTextAsync(connection, item.Id, cancellationToken);
                if (!IsRelevant(normalized, searchText)) continue;
            }
            var article = await ReadArticleAsync(connection, item.Id, query.Server, cancellationToken);
            if (article is not null) results.Add(article with { Score = item.Score, PreparedAnswer = item.Prepared, HasVerifiedPreparedAnswer = item.Prepared is not null && article.Facts.Any(f => f.Verified) });
        }
        results = results
            .OrderByDescending(x => x.Score)
            .ThenBy(x => IsCommunityArticle(x.ArticleId))
            .ThenBy(x => x.ArticleId, StringComparer.Ordinal)
            .ToList();
        // Different FTS hits usually contain complementary facts, not contradictory ones.
        // Treat them as a conflict only when the same query matched both articles exactly.
        if (results.Count >= 2 && results[0].Score >= 0.95 && results[1].Score >= 0.95
            && IsCommunityArticle(results[0].ArticleId) == IsCommunityArticle(results[1].ArticleId)
            && HasConflictingVerifiedFacts(results[0], results[1]))
        {
            results[0] = results[0] with { HasConflict = true };
            results[1] = results[1] with { HasConflict = true };
        }
        return results;
    }

    public async Task<KnowledgeArticle?> GetArticleAsync(string articleId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString); await connection.OpenAsync(cancellationToken);
        var match = await ReadArticleAsync(connection, articleId, "*", cancellationToken);
        return match is null ? null : new(match.ArticleId, match.Title, match.Facts, match.Facts.Max(f => f.UpdatedAt), match.Facts.All(f => f.Verified), false);
    }

    private static async Task<KnowledgeMatch?> ReadArticleAsync(SqliteConnection connection, string id, string server, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT title,updated_at,verified,valid_until FROM articles WHERE id=$id AND ($server='*' OR EXISTS(SELECT 1 FROM article_scopes s WHERE s.article_id=articles.id AND (s.server='all' OR s.server=$server)))"; cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$server", server); await using var reader = await cmd.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return null;
        var title = reader.GetString(0); var updated = DateTimeOffset.Parse(reader.GetString(1)); var articleVerified = reader.GetBoolean(2); DateTimeOffset? validUntil = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)); await reader.DisposeAsync();
        await using var scopeCommand = connection.CreateCommand(); scopeCommand.CommandText = "SELECT server FROM article_scopes WHERE article_id=$id AND ($server='*' OR server='all' OR server=$server) ORDER BY CASE server WHEN 'all' THEN 0 ELSE 1 END LIMIT 1"; scopeCommand.Parameters.AddWithValue("$id", id); scopeCommand.Parameters.AddWithValue("$server", server); var storedScope = (string?)await scopeCommand.ExecuteScalarAsync(ct) ?? server;
        var facts = new List<KnowledgeFact>(); await using var fact = connection.CreateCommand(); fact.CommandText = "SELECT id,text,verified FROM facts WHERE article_id=$id"; fact.Parameters.AddWithValue("$id", id); await using var factReader = await fact.ExecuteReaderAsync(ct); while (await factReader.ReadAsync(ct)) facts.Add(new(factReader.GetString(0), id, factReader.GetString(1), articleVerified && factReader.GetBoolean(2), updated, storedScope));
        return new(id, title, 0, facts, false, validUntil is not null && validUntil < DateTimeOffset.UtcNow);
    }

    private static bool HasConflictingVerifiedFacts(KnowledgeMatch left, KnowledgeMatch right)
    {
        var leftFacts = left.Facts.Where(x => x.Verified).Select(x => Normalize(x.Text)).ToHashSet(StringComparer.Ordinal);
        var rightFacts = right.Facts.Where(x => x.Verified).Select(x => Normalize(x.Text)).ToHashSet(StringComparer.Ordinal);
        return leftFacts.Count > 0 && rightFacts.Count > 0 && !leftFacts.SetEquals(rightFacts);
    }

    private static bool IsCommunityArticle(string articleId) =>
        articleId.StartsWith("community.", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadSearchTextAsync(SqliteConnection connection, string articleId, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var (sql, column) in new[]
        {
            ("SELECT title || ' ' || summary FROM articles WHERE id=$id", 0),
            ("SELECT alias FROM aliases WHERE article_id=$id", 0),
            ("SELECT pattern FROM prepared_answers WHERE article_id=$id", 0),
            ("SELECT text FROM facts WHERE article_id=$id", 0),
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", articleId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) parts.Add(reader.GetString(column));
        }
        return string.Join(' ', parts);
    }

    private static bool IsRelevant(string query, string candidate)
    {
        var queryTokens = SearchTokens(query).Distinct(StringComparer.Ordinal).ToArray();
        if (queryTokens.Length == 0) return false;
        var candidateTokens = SearchTokens(Normalize(candidate)).ToHashSet(StringComparer.Ordinal);
        var matched = queryTokens.Count(candidateTokens.Contains);
        if (queryTokens.Length == 1) return matched == 1;
        return matched >= 2 && (double)matched / queryTokens.Length >= .5;
    }

    private static IEnumerable<string> SearchTokens(string normalized)
    {
        foreach (var token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 3 || SearchStopWords.Contains(token)) continue;
            yield return SearchStem(token);
        }
    }

    private static string SearchStem(string token)
    {
        if (token.Any(char.IsDigit) || token.Any(x => x is >= 'a' and <= 'z')) return token;
        foreach (var suffix in SearchSuffixes)
        {
            if (token.EndsWith(suffix, StringComparison.Ordinal) && token.Length - suffix.Length >= 4)
                return token[..^suffix.Length];
        }
        return token;
    }

    private static readonly HashSet<string> SearchStopWords = new(StringComparer.Ordinal)
    {
        "как", "что", "кто", "где", "когда", "какой", "какая", "какие", "какое",
        "сколько", "почему", "зачем", "можно", "нужно", "нужен", "нужна", "нужны",
        "сделать", "делать", "получить", "найти", "взять", "скажи", "подскажи",
        "для", "при", "или", "это", "этот", "эта", "эти", "мой", "моя", "мое",
        "его", "ее", "их", "над", "под", "без", "про", "через", "после", "перед",
    };

    private static readonly string[] SearchSuffixes =
    [
        "иями", "ями", "ами", "иями", "иях", "ого", "его", "ому", "ему", "ыми", "ими",
        "ать", "ять", "ить", "еть", "уть", "ах", "ях", "ов", "ев", "ом", "ем", "ам", "ям",
        "ой", "ей", "ый", "ий", "ая", "яя", "ое", "ее", "ие", "ые", "ую", "юю", "ии",
        "ы", "и", "а", "я", "о", "е", "у", "ю",
    ];

    private static string Normalize(string value) => Core.TranscriptDeduplicator.Normalize(value);
}
