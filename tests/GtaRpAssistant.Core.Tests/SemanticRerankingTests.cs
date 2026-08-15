using GtaRpAssistant.Core;

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

    private static KnowledgeMatch Match(string id, double score) =>
        new(id, id, score, [new($"{id}.fact", id, "verified", true, DateTimeOffset.UtcNow)], false, false);
}
