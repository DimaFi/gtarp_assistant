using GtaRpAssistant.Core;
using System.Text.Json;

namespace GtaRpAssistant.Core.Tests;

public sealed class SemanticRerankingTests
{
    [Fact]
    public void Apply_ReordersOnlyExistingCandidates()
    {
        var candidates = new[] { Match("a", .75), Match("b", .74) };
        var reranked = SemanticRerankPolicy.Apply(candidates,
            [new("invented", 1), new("b", .9), new("a", .2)]);

        Assert.Equal(["b", "a"], reranked.Select(x => x.ArticleId));
        Assert.Same(candidates[1].Facts, reranked[0].Facts);
    }

    [Fact]
    public void Apply_IgnoresIncompleteOrInvalidScores()
    {
        var candidates = new[] { Match("a", .75), Match("b", .74) };
        Assert.Same(candidates, SemanticRerankPolicy.Apply(candidates, [new("b", double.NaN)]));
        Assert.Same(candidates, SemanticRerankPolicy.Apply(candidates, [new("b", .9)]));
    }

    [Fact]
    public async Task EmbeddingReranker_BatchesAndCachesCandidateDocuments()
    {
        var provider = new FakeEmbeddingProvider();
        var reranker = new EmbeddingSemanticReranker(provider);
        var candidates = new[] { Match("работа", .6) with { Title = "работа таксистом" }, Match("бизнес", .59) with { Title = "покупка бизнеса" } };

        var first = await reranker.ScoreAsync("как заработать на перевозках", candidates, default);
        var second = await reranker.ScoreAsync("хочу возить пассажиров", candidates, default);

        Assert.True(first.Single(x => x.ArticleId == "работа").Score > first.Single(x => x.ArticleId == "бизнес").Score);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(3, provider.BatchSizes[0]);
        Assert.Equal(1, provider.BatchSizes[1]);
        Assert.Equal(2, second.Count);
    }

    [Fact]
    public async Task OfflineParaphraseDataset_MustImproveWithoutForbiddenTopResult()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "ml/evaluation/semantic-relevance-eval.json")));
        var source = json.RootElement;
        var cases = source.GetProperty("cases").EnumerateArray().Select(item =>
        {
            var expected = item.GetProperty("expected").GetString()!;
            var distractor = item.GetProperty("distractor").GetString()!;
            return new SemanticRelevanceCase(item.GetProperty("query").GetString()!, expected,
                [Match(distractor, .7) with { Title = item.GetProperty("distractorText").GetString()! }, Match(expected, .6) with { Title = item.GetProperty("expectedText").GetString()! }],
                new HashSet<string> { "wrong-server" });
        }).ToArray();

        var result = await SemanticRelevanceGate.EvaluateAsync(new DatasetReranker(), cases,
            source.GetProperty("minimumTop1Rate").GetDouble(), default);

        Assert.True(result.Passed);
        Assert.Equal(0, result.LexicalTop1Hits);
        Assert.Equal(cases.Length, result.SemanticTop1Hits);
        Assert.Equal(0, result.ForbiddenTop1Hits);
    }

    private static KnowledgeMatch Match(string id, double score) =>
        new(id, id, score, [new($"{id}.fact", id, "verified", true, DateTimeOffset.UtcNow)], false, false);

    private sealed class FakeEmbeddingProvider : IBatchEmbeddingProvider
    {
        public int Calls { get; private set; }
        public List<int> BatchSizes { get; } = [];
        public string Id => "fake";
        public ProviderKind Kind => ProviderKind.BuiltIn;
        public ProviderCapabilities Capabilities => new() { SupportsEmbeddings = true, IsLocal = true };
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderHealth(true, "ok"));
        public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelInfo>>([]);
        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult(Vector(text));
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Calls++;
            BatchSizes.Add(texts.Count);
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(texts.Select(Vector).ToArray());
        }
        private static ReadOnlyMemory<float> Vector(string text) => text.Contains("перевоз", StringComparison.OrdinalIgnoreCase)
            || text.Contains("такс", StringComparison.OrdinalIgnoreCase)
            || text.Contains("пассаж", StringComparison.OrdinalIgnoreCase)
                ? new float[] { 1, 0 }
                : new float[] { 0, 1 };
    }

    private sealed class DatasetReranker : ISemanticReranker
    {
        public Task<IReadOnlyList<SemanticRelevanceScore>> ScoreAsync(string question, IReadOnlyList<KnowledgeMatch> candidates, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SemanticRelevanceScore>>(candidates.Select(x => new SemanticRelevanceScore(x.ArticleId,
                WordOverlap(question, x.Title))).ToArray());

        private static double WordOverlap(string left, string right)
        {
            var concepts = new[]
            {
                new[] { "возить", "перевозка", "пассажиров", "людей" },
                new[] { "трудиться", "работа", "новичка", "без транспорта", "без своей машины" },
                new[] { "правила", "поведения", "не нарушить" },
                new[] { "ограбили", "жертвы", "что делать", "действия" },
            };
            return concepts.Any(group => group.Any(left.Contains) && group.Any(right.Contains)) ? .95 : .1;
        }
    }
}
