namespace GtaRpAssistant.Core;

public sealed record SemanticRelevanceScore(string ArticleId, double Score);

public interface ISemanticReranker
{
    Task<IReadOnlyList<SemanticRelevanceScore>> ScoreAsync(
        string question,
        IReadOnlyList<KnowledgeMatch> candidates,
        CancellationToken cancellationToken);
}

public sealed class EmbeddingSemanticReranker(IEmbeddingProvider provider) : ISemanticReranker
{
    private const int MaxCachedDocuments = 128;
    private const int MaxDocumentCharacters = 1800;
    private readonly Dictionary<string, ReadOnlyMemory<float>> _documentCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<SemanticRelevanceScore>> ScoreAsync(
        string question,
        IReadOnlyList<KnowledgeMatch> candidates,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || candidates.Count < 2) return [];
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var documents = candidates.Select(DocumentText).ToArray();
            var missing = documents.Where(x => !_documentCache.ContainsKey(x)).Distinct(StringComparer.Ordinal).ToArray();
            IReadOnlyList<ReadOnlyMemory<float>> vectors;
            if (provider is IBatchEmbeddingProvider batch)
                vectors = await batch.EmbedAsync([question, .. missing], cancellationToken);
            else
            {
                var generated = new List<ReadOnlyMemory<float>>(missing.Length + 1)
                {
                    await provider.EmbedAsync(question, cancellationToken),
                };
                foreach (var document in missing)
                    generated.Add(await provider.EmbedAsync(document, cancellationToken));
                vectors = generated;
            }

            if (vectors.Count != missing.Length + 1 || vectors[0].IsEmpty) return [];
            if (_documentCache.Count + missing.Length > MaxCachedDocuments) _documentCache.Clear();
            for (var i = 0; i < missing.Length; i++)
                _documentCache[missing[i]] = vectors[i + 1];

            var result = new List<SemanticRelevanceScore>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = _documentCache[documents[i]];
                if (candidate.Length != vectors[0].Length || candidate.IsEmpty) return [];
                result.Add(new(candidates[i].ArticleId, Cosine01(vectors[0], candidate)));
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    private static string DocumentText(KnowledgeMatch match)
    {
        var text = string.Join(' ', new[] { match.Title, match.PreparedAnswer }.Concat(match.Facts.Select(x => x.Text)).Where(x => !string.IsNullOrWhiteSpace(x)));
        return text.Length <= MaxDocumentCharacters ? text : text[..MaxDocumentCharacters];
    }

    private static double Cosine01(ReadOnlyMemory<float> leftMemory, ReadOnlyMemory<float> rightMemory)
    {
        var left = leftMemory.Span;
        var right = rightMemory.Span;
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }
        if (leftNorm <= 0 || rightNorm <= 0) return 0;
        return Math.Clamp((dot / Math.Sqrt(leftNorm * rightNorm) + 1) / 2, 0, 1);
    }
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
