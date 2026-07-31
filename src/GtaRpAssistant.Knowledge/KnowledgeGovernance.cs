namespace GtaRpAssistant.Knowledge;

public enum KnowledgeIssueSeverity { Warning, Error }

public sealed record KnowledgeGovernanceIssue(KnowledgeIssueSeverity Severity, string Code, string ArticleId, string Message);

public sealed record KnowledgeGovernanceReport(IReadOnlyList<KnowledgeGovernanceIssue> Issues)
{
    public bool HasErrors => Issues.Any(x => x.Severity == KnowledgeIssueSeverity.Error);
    public bool HasWarnings => Issues.Any(x => x.Severity == KnowledgeIssueSeverity.Warning);
}

public static class KnowledgeGovernanceValidator
{
    public static KnowledgeGovernanceReport Inspect(IReadOnlyList<KnowledgePackArticle> articles, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var issues = new List<KnowledgeGovernanceIssue>();
        foreach (var article in articles)
        {
            if (article.Demo && article.Verified)
                Error("verified-demo", "A demo article cannot be verified.");
            if (article.Verified && string.IsNullOrWhiteSpace(article.VerifiedBy))
                Error("missing-reviewer", "A verified article requires verifiedBy.");
            if (!article.Verified && !string.IsNullOrWhiteSpace(article.VerifiedBy))
                Warning("reviewer-on-unverified", "An unverified article should not name a reviewer.");
            if (article.Verified && !IsHttps(article.Source.Url))
                Error("invalid-source", "A verified article requires an absolute HTTPS source URL.");
            else if (!string.IsNullOrWhiteSpace(article.Source.Url) && !Uri.TryCreate(article.Source.Url, UriKind.Absolute, out _))
                Warning("invalid-source", "Source URL is not absolute.");
            if (article.Verified && article.ValidUntil is null)
                Warning("missing-expiry", "A verified article should define validUntil or document a review policy.");
            if (article.ValidUntil is { } validUntil && validUntil <= article.UpdatedAt)
                Error("invalid-validity", "validUntil must be later than updatedAt.");
            if (article.Verified && article.ValidUntil is { } expiry && expiry < currentTime)
                Error("expired", "Verified article has expired and must be reviewed or revoked.");
            if (article.UpdatedAt > currentTime.AddMinutes(5))
                Error("future-update", "updatedAt is in the future.");
            if (article.ServerScope.Distinct(StringComparer.OrdinalIgnoreCase).Count() != article.ServerScope.Count)
                Warning("duplicate-scope", "Server scope contains duplicates.");
            if (article.Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != article.Aliases.Count)
                Warning("duplicate-alias", "Aliases contain duplicates.");

            void Error(string code, string message) => issues.Add(new(KnowledgeIssueSeverity.Error, code, article.Id, message));
            void Warning(string code, string message) => issues.Add(new(KnowledgeIssueSeverity.Warning, code, article.Id, message));
        }

        return new(issues);
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host);
}

public sealed record KnowledgePackInspection(
    string Id,
    string Project,
    int Version,
    DateTimeOffset CreatedAt,
    int Articles,
    int Facts,
    int PreparedAnswers,
    int VerifiedArticles,
    int DemoArticles,
    int ExpiredArticles,
    IReadOnlyList<string> Servers,
    IReadOnlyList<string> SourceHosts)
{
    public static KnowledgePackInspection Create(LoadedKnowledgePack pack, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        return new(
            pack.Manifest.Id,
            pack.Manifest.Project,
            pack.Manifest.Version,
            pack.Manifest.CreatedAt,
            pack.Articles.Count,
            pack.Articles.Sum(x => x.Facts.Count),
            pack.Articles.Sum(x => x.PreparedAnswers.Count),
            pack.Articles.Count(x => x.Verified),
            pack.Articles.Count(x => x.Demo),
            pack.Articles.Count(x => x.ValidUntil is { } expiry && expiry < currentTime),
            pack.Articles.SelectMany(x => x.ServerScope).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            pack.Articles.Select(x => Uri.TryCreate(x.Source.Url, UriKind.Absolute, out var uri) ? uri.Host : null).OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
