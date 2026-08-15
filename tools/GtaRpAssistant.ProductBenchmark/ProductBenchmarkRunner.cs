using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GtaRpAssistant.Core;
using GtaRpAssistant.Knowledge;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.ProductBenchmark;

public sealed class ProductBenchmarkRunner
{
    public async Task<ProductBenchmarkReport> RunAsync(
        ProductEvaluationDataset dataset,
        string packDirectory,
        string communityDirectory,
        CancellationToken cancellationToken)
    {
        var validation = ProductBenchmarkValidation.Validate(dataset);
        if (validation.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, validation));

        var pack = await new KnowledgePackLoader().LoadPackAsync(packDirectory, cancellationToken);
        var community = await new CommunityReferenceLoader().LoadAsync(communityDirectory, cancellationToken);
        var articles = pack.Articles.Concat(community).ToArray();
        var cases = ProductCaseCatalog.Build(dataset, articles);
        var articleMap = articles.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var factMap = articles
            .SelectMany(x => x.Facts.Select(f => new KnowledgeFact(
                f.Id,
                x.Id,
                f.Text,
                x.Verified && f.Verified,
                x.UpdatedAt,
                x.ServerScope.FirstOrDefault() ?? "all")))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        var databasePath = Path.Combine(Path.GetTempPath(), $"gta-rp-product-benchmark-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteKnowledgeRepository($"Data Source={databasePath}");
            await repository.InitializeAsync(articles, cancellationToken);
            var eventSink = new MetricsEventSink();
            await using var coordinator = CreateCoordinator(repository, eventSink);
            coordinator.Start(true);

            var results = new List<ProductBenchmarkCaseResult>(cases.Count);
            foreach (var item in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                eventSink.Clear();
                coordinator.ClearContext();
                coordinator.StartNewConversation();
                var now = DateTimeOffset.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                var answer = await coordinator.ProcessAsync(new(
                    new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, item.Question, 1),
                    AssistantActivationKind.ManualText,
                    item.Server,
                    false,
                    false), cancellationToken);
                stopwatch.Stop();
                results.Add(Score(item, answer, stopwatch.Elapsed.TotalMilliseconds, articleMap, factMap, eventSink.LastMetrics));
            }

            var metrics = Summarize(results);
            var failures = ProductBenchmarkValidation.EvaluateGate(metrics, dataset.Thresholds);
            return new()
            {
                DatasetId = dataset.Id,
                KnowledgePackId = pack.Manifest.Id,
                KnowledgePackVersion = pack.Manifest.Version,
                GeneratedAt = DateTimeOffset.UtcNow,
                PassedReleaseGate = failures.Count == 0,
                Metrics = metrics,
                GateFailures = failures,
                Cases = results,
            };
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    public static ProductBenchmarkCaseResult Score(
        ProductEvaluationCase item,
        AssistantAnswer? answer,
        double latencyMs,
        IReadOnlyDictionary<string, KnowledgePackArticle> articles,
        IReadOnlyDictionary<string, KnowledgeFact> facts,
        AssistantRequestMetrics? requestMetrics = null)
    {
        var expected = item.ExpectedDecision;
        var actual = answer?.Decision switch
        {
            AnswerDecision.Show => "show",
            AnswerDecision.AskForMoreInformation => "clarify",
            _ => "abstain",
        };
        var decisionMatched = expected == actual;
        var expectedShow = expected == "show";
        var actualShow = actual == "show";
        var usedFactIds = answer?.UsedFactIds ?? [];
        var allowedFacts = item.AllowedArticleIds
            .Where(articles.ContainsKey)
            .SelectMany(x => articles[x].Facts.Select(y => y.Id))
            .ToHashSet(StringComparer.Ordinal);
        var allowedTitles = item.AllowedArticleIds
            .Where(articles.ContainsKey)
            .Select(x => articles[x].Title)
            .ToHashSet(StringComparer.Ordinal);
        var articleMatched = !expectedShow || actualShow
            && usedFactIds.Count > 0
            && usedFactIds.All(allowedFacts.Contains)
            && answer is not null
            && allowedTitles.Contains(answer.SourceTitle ?? "");
        var citationPresent = !expectedShow
            || answer is not null
            && !string.IsNullOrWhiteSpace(answer.SourceTitle)
            && answer.SourceUpdatedAt is not null
            && answer.UsedFactIds.Count > 0;
        var requiredFactsPresent = item.RequiredFactIds.All(usedFactIds.Contains);
        var userText = UserText(answer);
        var requiredTermsPresent = item.RequiredTerms.All(x => userText.Contains(x, StringComparison.OrdinalIgnoreCase));
        var forbiddenTermsAbsent = item.ForbiddenTerms.All(x => !userText.Contains(x, StringComparison.OrdinalIgnoreCase));

        var usedFactText = string.Join(' ', usedFactIds
            .Where(facts.ContainsKey)
            .Select(x => facts[x].Text));
        var unsupportedNumbers = actualShow && NumberRegex().Matches(userText).Cast<Match>()
            .Select(x => x.Value)
            .Any(x => !usedFactText.Contains(x, StringComparison.Ordinal));
        var wrongServer = actualShow && usedFactIds
            .Where(facts.ContainsKey)
            .Select(x => facts[x])
            .Any(x => x.ServerScope != "all" && !x.ServerScope.Equals(item.Server, StringComparison.OrdinalIgnoreCase));
        var falseAnswer = expected == "abstain" && actual != "abstain";
        var falseAbstain = expectedShow && actual == "abstain";
        var passed = decisionMatched
            && articleMatched
            && citationPresent
            && requiredFactsPresent
            && requiredTermsPresent
            && forbiddenTermsAbsent
            && !unsupportedNumbers
            && !wrongServer;

        return new()
        {
            Id = item.Id,
            Category = item.Category,
            Question = item.Question,
            Blocking = item.Blocking,
            ExpectedDecision = expected,
            ActualDecision = actual,
            DecisionMatched = decisionMatched,
            ArticleMatched = articleMatched,
            CitationPresent = citationPresent,
            RequiredFactsPresent = requiredFactsPresent,
            RequiredTermsPresent = requiredTermsPresent,
            ForbiddenTermsAbsent = forbiddenTermsAbsent,
            UnsupportedNumbersPresent = unsupportedNumbers,
            WrongServer = wrongServer,
            FalseAnswer = falseAnswer,
            FalseAbstain = falseAbstain,
            Passed = passed,
            LatencyMs = latencyMs,
            SourceTitle = answer?.SourceTitle ?? "",
            UsedFactIds = usedFactIds,
            DiagnosticReason = answer?.DiagnosticReason ?? "Pipeline returned no answer.",
            Route = requestMetrics?.Route ?? "unresolved",
            CacheHit = requestMetrics?.CacheHits > 0,
            AvoidedLlm = requestMetrics?.AvoidedLlm ?? true,
            ProviderAvailabilityChecks = requestMetrics?.ProviderAvailabilityChecks ?? 0,
            LlmCalls = requestMetrics?.LlmCalls ?? 0,
            EstimatedInputTokens = requestMetrics?.EstimatedInputTokens ?? 0,
            EstimatedOutputBudgetTokens = requestMetrics?.EstimatedOutputBudgetTokens ?? 0,
        };
    }

    public static ProductBenchmarkMetrics Summarize(IReadOnlyList<ProductBenchmarkCaseResult> cases)
    {
        var blocking = cases.Where(x => x.Blocking).ToArray();
        var expectedShow = cases.Where(x => x.ExpectedDecision == "show").ToArray();
        var blockingShow = blocking.Where(x => x.ExpectedDecision == "show").ToArray();
        var orderedLatency = cases.Select(x => x.LatencyMs).OrderBy(x => x).ToArray();
        var p95Index = orderedLatency.Length == 0 ? 0 : (int)Math.Ceiling(orderedLatency.Length * .95) - 1;
        return new()
        {
            TotalCases = cases.Count,
            BlockingCases = blocking.Length,
            PassedCases = cases.Count(x => x.Passed),
            BlockingPassedCases = blocking.Count(x => x.Passed),
            PassRate = Ratio(cases.Count(x => x.Passed), cases.Count),
            BlockingPassRate = Ratio(blocking.Count(x => x.Passed), blocking.Length),
            DecisionAccuracy = Ratio(cases.Count(x => x.DecisionMatched), cases.Count),
            BlockingDecisionAccuracy = Ratio(blocking.Count(x => x.DecisionMatched), blocking.Length),
            ArticleAccuracy = Ratio(expectedShow.Count(x => x.ArticleMatched), expectedShow.Length),
            BlockingArticleAccuracy = Ratio(blockingShow.Count(x => x.ArticleMatched), blockingShow.Length),
            CitationCoverage = Ratio(expectedShow.Count(x => x.CitationPresent), expectedShow.Length),
            BlockingCitationCoverage = Ratio(blockingShow.Count(x => x.CitationPresent), blockingShow.Length),
            FalseAnswers = cases.Count(x => x.FalseAnswer),
            BlockingFalseAnswers = blocking.Count(x => x.FalseAnswer),
            FalseAbstains = cases.Count(x => x.FalseAbstain),
            BlockingFalseAbstains = blocking.Count(x => x.FalseAbstain),
            UnsupportedNumberCases = cases.Count(x => x.UnsupportedNumbersPresent),
            BlockingUnsupportedNumberCases = blocking.Count(x => x.UnsupportedNumbersPresent),
            WrongServerCases = cases.Count(x => x.WrongServer),
            BlockingWrongServerCases = blocking.Count(x => x.WrongServer),
            AverageLatencyMs = cases.Count == 0 ? 0 : cases.Average(x => x.LatencyMs),
            P95LatencyMs = orderedLatency.Length == 0 ? 0 : orderedLatency[p95Index],
            AvoidedLlmRate = cases.Count == 0 ? 1 : (double)cases.Count(x => x.AvoidedLlm) / cases.Count,
            CacheHitRate = cases.Count == 0 ? 0 : (double)cases.Count(x => x.CacheHit) / cases.Count,
            ProviderAvailabilityChecks = cases.Sum(x => x.ProviderAvailabilityChecks),
            LlmCalls = cases.Sum(x => x.LlmCalls),
            EstimatedInputTokens = cases.Sum(x => x.EstimatedInputTokens),
            EstimatedOutputBudgetTokens = cases.Sum(x => x.EstimatedOutputBudgetTokens),
        };
    }

    public static async Task WriteReportsAsync(ProductBenchmarkReport report, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var jsonPath = Path.Combine(outputDirectory, "product-pipeline-report.json");
        var markdownPath = Path.Combine(outputDirectory, "product-pipeline-report.md");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, ProductBenchmarkJson.Options), new UTF8Encoding(false), cancellationToken);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(report), new UTF8Encoding(false), cancellationToken);
    }

    private static AssistantSessionCoordinator CreateCoordinator(IKnowledgeRepository repository, ISessionEventSink eventSink) => new(
        new(TimeSpan.FromMinutes(3)),
        new RuleBasedIntentDetector([]),
        repository,
        new ContextSelector(),
        new AiRouter(),
        new GroundedAnswerValidator(),
        new UnavailableProviderCatalog(),
        new NullOverlay(),
        new TranscriptDeduplicator(),
        new ProactivePolicy(),
        eventSink);

    private static string UserText(AssistantAnswer? answer)
    {
        if (answer is null) return "";
        var parts = new List<string> { answer.Title, answer.Message };
        if (answer.ProblemSolution is not null)
        {
            parts.Add(answer.ProblemSolution.Summary);
            parts.AddRange(answer.ProblemSolution.Steps);
            parts.AddRange(answer.ProblemSolution.PossibleCauses);
            parts.AddRange(answer.ProblemSolution.FollowUpSuggestions);
        }
        return string.Join(' ', parts);
    }

    private static string BuildMarkdown(ProductBenchmarkReport report)
    {
        var m = report.Metrics;
        var builder = new StringBuilder();
        builder.AppendLine("# GTA RP Assistant — production pipeline benchmark");
        builder.AppendLine();
        builder.AppendLine($"- Dataset: `{report.DatasetId}`");
        builder.AppendLine($"- Knowledge pack: `{report.KnowledgePackId}` v{report.KnowledgePackVersion}");
        builder.AppendLine($"- Generated: `{report.GeneratedAt:O}`");
        builder.AppendLine($"- Release gate: **{(report.PassedReleaseGate ? "PASS" : "FAIL")}**");
        builder.AppendLine($"- Cases: {m.TotalCases}, blocking: {m.BlockingCases}");
        builder.AppendLine($"- Blocking pass rate: {m.BlockingPassRate:P2}");
        builder.AppendLine($"- Blocking decision accuracy: {m.BlockingDecisionAccuracy:P2}");
        builder.AppendLine($"- Blocking article accuracy: {m.BlockingArticleAccuracy:P2}");
        builder.AppendLine($"- Blocking citation coverage: {m.BlockingCitationCoverage:P2}");
        builder.AppendLine($"- False answers: {m.BlockingFalseAnswers}");
        builder.AppendLine($"- Unsupported-number cases: {m.BlockingUnsupportedNumberCases}");
        builder.AppendLine($"- Wrong-server cases: {m.BlockingWrongServerCases}");
        builder.AppendLine($"- Latency average/p95: {m.AverageLatencyMs:F2}/{m.P95LatencyMs:F2} ms");
        builder.AppendLine($"- LLM avoided: {m.AvoidedLlmRate:P2}; cache hit: {m.CacheHitRate:P2}");
        builder.AppendLine($"- Provider checks / LLM calls: {m.ProviderAvailabilityChecks}/{m.LlmCalls}");
        builder.AppendLine($"- Estimated input/output-budget tokens: {m.EstimatedInputTokens}/{m.EstimatedOutputBudgetTokens}");
        if (report.GateFailures.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Gate failures");
            foreach (var failure in report.GateFailures) builder.AppendLine($"- {failure}");
        }
        var failed = report.Cases.Where(x => !x.Passed).ToArray();
        if (failed.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Failed cases");
            builder.AppendLine();
            builder.AppendLine("| Blocking | ID | Expected | Actual | Question | Diagnostic |");
            builder.AppendLine("|---|---|---|---|---|---|");
            foreach (var item in failed)
                builder.AppendLine($"| {(item.Blocking ? "yes" : "no")} | `{Escape(item.Id)}` | {item.ExpectedDecision} | {item.ActualDecision} | {Escape(item.Question)} | {Escape(item.DiagnosticReason)} |");
        }
        return builder.ToString();
    }

    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 1 : (double)numerator / denominator;
    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    private static Regex NumberRegex() => new(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

    private sealed class NullOverlay : IOverlayService
    {
        public bool IsVisible => false;
        public Task ShowAsync(AssistantAnswer answer, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HideAsync() => Task.CompletedTask;
    }

    private sealed class UnavailableProviderCatalog : IChatProviderCatalog
    {
        public Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ChatProviderAvailability(null, null, false, false));
    }

    private sealed class MetricsEventSink : ISessionEventSink
    {
        public AssistantRequestMetrics? LastMetrics { get; private set; }
        public void Clear() => LastMetrics = null;
        public void Write(SessionEvent value)
        {
            if (value.Name != "Assistant request metrics" || string.IsNullOrWhiteSpace(value.Detail)) return;
            LastMetrics = JsonSerializer.Deserialize<AssistantRequestMetrics>(value.Detail);
        }
    }
}
