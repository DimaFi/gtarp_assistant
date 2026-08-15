namespace GtaRpAssistant.Core;

public sealed record SemanticRelevanceCase(
    string Query,
    string ExpectedArticleId,
    IReadOnlyList<KnowledgeMatch> Candidates,
    IReadOnlySet<string>? ForbiddenArticleIds = null);

public sealed record SemanticRelevanceGateResult(
    int Cases,
    int LexicalTop1Hits,
    int SemanticTop1Hits,
    int ForbiddenTop1Hits,
    bool Passed);

public static class SemanticRelevanceGate
{
    public static async Task<SemanticRelevanceGateResult> EvaluateAsync(
        ISemanticReranker reranker,
        IReadOnlyList<SemanticRelevanceCase> cases,
        double minimumTop1Rate,
        CancellationToken cancellationToken)
    {
        if (cases.Count == 0) throw new ArgumentException("Relevance dataset пуст.", nameof(cases));
        var lexicalHits = 0;
        var semanticHits = 0;
        var forbiddenHits = 0;
        foreach (var item in cases)
        {
            if (item.Candidates.Count < 2) throw new ArgumentException("Каждый relevance case должен иметь минимум двух кандидатов.", nameof(cases));
            if (string.Equals(item.Candidates[0].ArticleId, item.ExpectedArticleId, StringComparison.Ordinal)) lexicalHits++;
            var scores = await reranker.ScoreAsync(item.Query, item.Candidates, cancellationToken);
            var top = SemanticRerankPolicy.Apply(item.Candidates, scores)[0].ArticleId;
            if (string.Equals(top, item.ExpectedArticleId, StringComparison.Ordinal)) semanticHits++;
            if (item.ForbiddenArticleIds?.Contains(top) == true) forbiddenHits++;
        }
        var passed = semanticHits >= lexicalHits
            && semanticHits / (double)cases.Count >= minimumTop1Rate
            && forbiddenHits == 0;
        return new(cases.Count, lexicalHits, semanticHits, forbiddenHits, passed);
    }
}
