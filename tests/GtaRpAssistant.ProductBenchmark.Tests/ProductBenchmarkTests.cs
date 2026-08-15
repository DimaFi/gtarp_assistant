using GtaRpAssistant.ProductBenchmark;
using GtaRpAssistant.Knowledge;

namespace GtaRpAssistant.ProductBenchmark.Tests;

public sealed class ProductBenchmarkTests
{
    [Fact]
    public async Task BundledDatasetIsValidAndExpandsPastMinimumCaseCount()
    {
        var root = RepositoryRoot();
        var dataset = ProductBenchmarkJson.LoadDataset(Path.Combine(root, "ml", "evaluation", "product-pipeline-eval.json"));
        Assert.Empty(ProductBenchmarkValidation.Validate(dataset));
        var official = await new KnowledgePackLoader().LoadAsync(Path.Combine(root, "knowledge", "packs", "gta5rp"), default);
        var community = await new CommunityReferenceLoader().LoadAsync(Path.Combine(root, "knowledge", "reference", "community"), default);

        var cases = ProductCaseCatalog.Build(dataset, official.Concat(community).ToArray());

        Assert.True(cases.Count >= dataset.Thresholds.MinimumCases, $"Expanded dataset contains only {cases.Count} cases.");
        Assert.Equal(cases.Count, cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void InvalidShowCaseWithoutArticleIsRejected()
    {
        var dataset = new ProductEvaluationDataset
        {
            SchemaVersion = 1,
            Id = "invalid",
            Language = "ru",
            Cases = Enumerable.Range(0, 8).Select(i => new ProductEvaluationCase
            {
                Id = $"case-{i}",
                Category = "test",
                Question = $"question {i}",
                ExpectedDecision = "show",
            }).ToArray(),
        };

        Assert.Contains(ProductBenchmarkValidation.Validate(dataset), x => x.Contains("allowedArticleIds", StringComparison.Ordinal));
    }

    [Fact]
    public void GateRejectsFalseAnswersUnsupportedNumbersAndWrongServer()
    {
        var metrics = new ProductBenchmarkMetrics
        {
            TotalCases = 300,
            BlockingPassRate = 1,
            BlockingDecisionAccuracy = 1,
            BlockingArticleAccuracy = 1,
            BlockingCitationCoverage = 1,
            BlockingFalseAnswers = 1,
            BlockingUnsupportedNumberCases = 1,
            BlockingWrongServerCases = 1,
            P95LatencyMs = 10,
        };

        var failures = ProductBenchmarkValidation.EvaluateGate(metrics, new());

        Assert.Contains(failures, x => x.Contains("false answers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, x => x.Contains("unsupported-number", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, x => x.Contains("wrong-server", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExploratoryFailureDoesNotReduceBlockingMetrics()
    {
        var results = new[]
        {
            new ProductBenchmarkCaseResult { Id = "blocking", Blocking = true, ExpectedDecision = "show", ActualDecision = "show", Passed = true, DecisionMatched = true, ArticleMatched = true, CitationPresent = true, AvoidedLlm = true, ProviderAvailabilityChecks = 0 },
            new ProductBenchmarkCaseResult { Id = "exploratory", Blocking = false, ExpectedDecision = "show", ActualDecision = "abstain", Passed = false, FalseAbstain = true, AvoidedLlm = false, ProviderAvailabilityChecks = 1, LlmCalls = 1, EstimatedInputTokens = 900, EstimatedOutputBudgetTokens = 420 },
        };

        var metrics = ProductBenchmarkRunner.Summarize(results);

        Assert.Equal(1, metrics.BlockingPassRate);
        Assert.Equal(1, metrics.BlockingDecisionAccuracy);
        Assert.Equal(1, metrics.BlockingArticleAccuracy);
        Assert.Equal(1, metrics.BlockingCitationCoverage);
        Assert.Equal(.5, metrics.PassRate);
        Assert.Equal(.5, metrics.AvoidedLlmRate);
        Assert.Equal(1, metrics.ProviderAvailabilityChecks);
        Assert.Equal(1, metrics.LlmCalls);
        Assert.Equal(900, metrics.EstimatedInputTokens);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
