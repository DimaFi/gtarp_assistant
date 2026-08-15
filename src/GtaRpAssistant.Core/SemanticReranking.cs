namespace GtaRpAssistant.Core;

public sealed record SemanticRelevanceScore(string ArticleId, double Score);

public interface ISemanticReranker
{
    Task<IReadOnlyList<SemanticRelevanceScore>> ScoreAsync(
        string question,
        IReadOnlyList<KnowledgeMatch> candidates,
        CancellationToken cancellationToken);
}

public static class SemanticRerankPolicy
{
    public static IReadOnlyList<KnowledgeMatch> Apply(
        IReadOnlyList<KnowledgeMatch> candidates,
        IReadOnlyList<SemanticRelevanceScore> scores)
    {
        if (candidates.Count < 2 || scores.Count == 0) return candidates;
        var candidateIds = candidates.Select(x => x.ArticleId).ToHashSet(StringComparer.Ordinal);
        var validScores = scores
            .Where(x => candidateIds.Contains(x.ArticleId) && double.IsFinite(x.Score) && x.Score is >= 0 and <= 1)
            .GroupBy(x => x.ArticleId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Max(y => y.Score), StringComparer.Ordinal);
        if (validScores.Count < 2) return candidates;
        return candidates
            .Select((match, index) => (Match: match, Index: index, Score: validScores.GetValueOrDefault(match.ArticleId, -1)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Match)
            .ToArray();
    }
}
