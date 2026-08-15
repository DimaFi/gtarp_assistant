using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GtaRpAssistant.Core;
using GtaRpAssistant.Knowledge;

namespace GtaRpAssistant.ProductBenchmark;

public sealed record ProductEvaluationDataset
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string Language { get; init; } = "";
    public string Description { get; init; } = "";
    public ProductCaseGeneration Generation { get; init; } = new();
    public ProductBenchmarkThresholds Thresholds { get; init; } = new();
    public IReadOnlyList<ProductEvaluationCase> Cases { get; init; } = [];
}

public sealed record ProductCaseGeneration
{
    public bool IncludeOfficialPreparedAnswers { get; init; } = true;
    public bool IncludeCommunityPreparedAnswers { get; init; } = true;
    public bool IncludeUniqueAliases { get; init; }
}

public sealed record ProductBenchmarkThresholds
{
    public int MinimumCases { get; init; } = 250;
    public double MinimumBlockingPassRate { get; init; } = .98;
    public double MinimumBlockingDecisionAccuracy { get; init; } = .99;
    public double MinimumBlockingArticleAccuracy { get; init; } = .98;
    public double MinimumBlockingCitationCoverage { get; init; } = 1;
    public int MaximumBlockingFalseAnswers { get; init; }
    public int MaximumBlockingUnsupportedNumberCases { get; init; }
    public int MaximumBlockingWrongServerCases { get; init; }
    public double MaximumP95LatencyMs { get; init; } = 500;
}

public sealed record ProductEvaluationCase
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Question { get; init; } = "";
    public string Server { get; init; } = "all";
    public string ExpectedDecision { get; init; } = "";
    public IReadOnlyList<string> AllowedArticleIds { get; init; } = [];
    public IReadOnlyList<string> RequiredFactIds { get; init; } = [];
    public IReadOnlyList<string> RequiredTerms { get; init; } = [];
    public IReadOnlyList<string> ForbiddenTerms { get; init; } = [];
    public bool Blocking { get; init; } = true;
    public string Note { get; init; } = "";
}

public sealed record ProductBenchmarkCaseResult
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Question { get; init; } = "";
    public bool Blocking { get; init; }
    public string ExpectedDecision { get; init; } = "";
    public string ActualDecision { get; init; } = "";
    public bool DecisionMatched { get; init; }
    public bool ArticleMatched { get; init; }
    public bool CitationPresent { get; init; }
    public bool RequiredFactsPresent { get; init; }
    public bool RequiredTermsPresent { get; init; }
    public bool ForbiddenTermsAbsent { get; init; }
    public bool UnsupportedNumbersPresent { get; init; }
    public bool WrongServer { get; init; }
    public bool FalseAnswer { get; init; }
    public bool FalseAbstain { get; init; }
    public bool Passed { get; init; }
    public double LatencyMs { get; init; }
    public string SourceTitle { get; init; } = "";
    public IReadOnlyList<string> UsedFactIds { get; init; } = [];
    public string DiagnosticReason { get; init; } = "";
    public string Route { get; init; } = "unresolved";
    public bool CacheHit { get; init; }
    public bool AvoidedLlm { get; init; }
    public int ProviderAvailabilityChecks { get; init; }
    public int LlmCalls { get; init; }
    public int EstimatedInputTokens { get; init; }
    public int EstimatedOutputBudgetTokens { get; init; }
}

public sealed record ProductBenchmarkMetrics
{
    public int TotalCases { get; init; }
    public int BlockingCases { get; init; }
    public int PassedCases { get; init; }
    public int BlockingPassedCases { get; init; }
    public double PassRate { get; init; }
    public double BlockingPassRate { get; init; }
    public double DecisionAccuracy { get; init; }
    public double BlockingDecisionAccuracy { get; init; }
    public double ArticleAccuracy { get; init; }
    public double BlockingArticleAccuracy { get; init; }
    public double CitationCoverage { get; init; }
    public double BlockingCitationCoverage { get; init; }
    public int FalseAnswers { get; init; }
    public int BlockingFalseAnswers { get; init; }
    public int FalseAbstains { get; init; }
    public int BlockingFalseAbstains { get; init; }
    public int UnsupportedNumberCases { get; init; }
    public int BlockingUnsupportedNumberCases { get; init; }
    public int WrongServerCases { get; init; }
    public int BlockingWrongServerCases { get; init; }
    public double AverageLatencyMs { get; init; }
    public double P95LatencyMs { get; init; }
    public double AvoidedLlmRate { get; init; }
    public double CacheHitRate { get; init; }
    public int ProviderAvailabilityChecks { get; init; }
    public int LlmCalls { get; init; }
    public int EstimatedInputTokens { get; init; }
    public int EstimatedOutputBudgetTokens { get; init; }
}

public sealed record ProductBenchmarkReport
{
    public int SchemaVersion { get; init; } = 1;
    public string DatasetId { get; init; } = "";
    public string KnowledgePackId { get; init; } = "";
    public int KnowledgePackVersion { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public bool PassedReleaseGate { get; init; }
    public ProductBenchmarkMetrics Metrics { get; init; } = new();
    public IReadOnlyList<string> GateFailures { get; init; } = [];
    public IReadOnlyList<ProductBenchmarkCaseResult> Cases { get; init; } = [];
}

public static class ProductBenchmarkJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static ProductEvaluationDataset LoadDataset(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<ProductEvaluationDataset>(stream, Options)
            ?? throw new InvalidDataException("Product evaluation dataset is empty.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class ProductBenchmarkValidation
{
    private static readonly HashSet<string> Decisions = new(StringComparer.Ordinal)
    {
        "show",
        "clarify",
        "abstain",
    };

    public static IReadOnlyList<string> Validate(ProductEvaluationDataset dataset)
    {
        var errors = new List<string>();
        if (dataset.SchemaVersion != 1) errors.Add("Product evaluation schemaVersion must be 1.");
        if (string.IsNullOrWhiteSpace(dataset.Id)) errors.Add("Product evaluation id is required.");
        if (!dataset.Language.Equals("ru", StringComparison.OrdinalIgnoreCase)) errors.Add("Product evaluation language must be ru.");
        if (dataset.Cases.Count < 8) errors.Add("Product evaluation needs at least 8 curated cases.");

        ValidateRatio(dataset.Thresholds.MinimumBlockingPassRate, nameof(dataset.Thresholds.MinimumBlockingPassRate), errors);
        ValidateRatio(dataset.Thresholds.MinimumBlockingDecisionAccuracy, nameof(dataset.Thresholds.MinimumBlockingDecisionAccuracy), errors);
        ValidateRatio(dataset.Thresholds.MinimumBlockingArticleAccuracy, nameof(dataset.Thresholds.MinimumBlockingArticleAccuracy), errors);
        ValidateRatio(dataset.Thresholds.MinimumBlockingCitationCoverage, nameof(dataset.Thresholds.MinimumBlockingCitationCoverage), errors);
        if (dataset.Thresholds.MinimumCases < 1) errors.Add("minimumCases must be positive.");
        if (dataset.Thresholds.MaximumP95LatencyMs <= 0) errors.Add("maximumP95LatencyMs must be positive.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var questions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in dataset.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) errors.Add($"Case id is empty or duplicated: '{item.Id}'.");
            if (string.IsNullOrWhiteSpace(item.Category)) errors.Add($"Case '{item.Id}' needs category.");
            if (string.IsNullOrWhiteSpace(item.Question)) errors.Add($"Case '{item.Id}' needs question.");
            if (string.IsNullOrWhiteSpace(item.Server)) errors.Add($"Case '{item.Id}' needs server.");
            if (!Decisions.Contains(item.ExpectedDecision)) errors.Add($"Case '{item.Id}' has unsupported expectedDecision.");
            if (item.ExpectedDecision == "show" && item.AllowedArticleIds.Count == 0) errors.Add($"Show case '{item.Id}' needs allowedArticleIds.");
            var questionKey = $"{TranscriptDeduplicator.Normalize(item.Question)}\n{item.Server}";
            if (!questions.Add(questionKey)) errors.Add($"Curated question is duplicated: '{item.Question}'.");
        }
        return errors;
    }

    public static IReadOnlyList<string> EvaluateGate(ProductBenchmarkMetrics metrics, ProductBenchmarkThresholds thresholds)
    {
        var failures = new List<string>();
        if (metrics.TotalCases < thresholds.MinimumCases) failures.Add($"Total cases {metrics.TotalCases} < {thresholds.MinimumCases}.");
        if (metrics.BlockingPassRate < thresholds.MinimumBlockingPassRate) failures.Add($"Blocking pass rate {metrics.BlockingPassRate:P2} < {thresholds.MinimumBlockingPassRate:P2}.");
        if (metrics.BlockingDecisionAccuracy < thresholds.MinimumBlockingDecisionAccuracy) failures.Add($"Blocking decision accuracy {metrics.BlockingDecisionAccuracy:P2} < {thresholds.MinimumBlockingDecisionAccuracy:P2}.");
        if (metrics.BlockingArticleAccuracy < thresholds.MinimumBlockingArticleAccuracy) failures.Add($"Blocking article accuracy {metrics.BlockingArticleAccuracy:P2} < {thresholds.MinimumBlockingArticleAccuracy:P2}.");
        if (metrics.BlockingCitationCoverage < thresholds.MinimumBlockingCitationCoverage) failures.Add($"Blocking citation coverage {metrics.BlockingCitationCoverage:P2} < {thresholds.MinimumBlockingCitationCoverage:P2}.");
        if (metrics.BlockingFalseAnswers > thresholds.MaximumBlockingFalseAnswers) failures.Add($"Blocking false answers {metrics.BlockingFalseAnswers} > {thresholds.MaximumBlockingFalseAnswers}.");
        if (metrics.BlockingUnsupportedNumberCases > thresholds.MaximumBlockingUnsupportedNumberCases) failures.Add($"Blocking unsupported-number cases {metrics.BlockingUnsupportedNumberCases} > {thresholds.MaximumBlockingUnsupportedNumberCases}.");
        if (metrics.BlockingWrongServerCases > thresholds.MaximumBlockingWrongServerCases) failures.Add($"Blocking wrong-server cases {metrics.BlockingWrongServerCases} > {thresholds.MaximumBlockingWrongServerCases}.");
        if (metrics.P95LatencyMs > thresholds.MaximumP95LatencyMs) failures.Add($"P95 latency {metrics.P95LatencyMs:F2} ms > {thresholds.MaximumP95LatencyMs:F2} ms.");
        return failures;
    }

    private static void ValidateRatio(double value, string name, ICollection<string> errors)
    {
        if (value is < 0 or > 1) errors.Add($"{name} must be between 0 and 1.");
    }
}

public static class ProductCaseCatalog
{
    public static IReadOnlyList<ProductEvaluationCase> Build(ProductEvaluationDataset dataset, IReadOnlyList<KnowledgePackArticle> articles)
    {
        var cases = new List<ProductEvaluationCase>(dataset.Cases);
        var explicitQuestions = dataset.Cases
            .Select(x => $"{TranscriptDeduplicator.Normalize(x.Question)}\n{x.Server}")
            .ToHashSet(StringComparer.Ordinal);

        var candidates = new List<(string Question, KnowledgePackArticle Article, string Kind)>();
        foreach (var article in articles.Where(x => x.Verified && !x.Demo))
        {
            var community = article.Id.StartsWith("community.", StringComparison.OrdinalIgnoreCase);
            if (community && !dataset.Generation.IncludeCommunityPreparedAnswers) continue;
            if (!community && !dataset.Generation.IncludeOfficialPreparedAnswers) continue;
            candidates.AddRange(article.PreparedAnswers.Select(x => (x.QuestionPattern, article, "prepared")));
            if (dataset.Generation.IncludeUniqueAliases)
                candidates.AddRange(article.Aliases.Select(x => (x, article, "alias")));
        }

        var unique = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.Question))
            .GroupBy(x => TranscriptDeduplicator.Normalize(x.Question), StringComparer.Ordinal)
            .Where(x => x.Select(y => y.Article.Id).Distinct(StringComparer.Ordinal).Count() == 1)
            .Select(x => x.OrderBy(y => y.Kind, StringComparer.Ordinal).First())
            .OrderBy(x => x.Article.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Question, StringComparer.Ordinal);

        foreach (var candidate in unique)
        {
            var key = $"{TranscriptDeduplicator.Normalize(candidate.Question)}\nall";
            if (!explicitQuestions.Add(key)) continue;
            cases.Add(new()
            {
                Id = $"generated.{candidate.Kind}.{StableId(key)}",
                Category = candidate.Article.Category,
                Question = candidate.Question,
                Server = "all",
                ExpectedDecision = "show",
                AllowedArticleIds = [candidate.Article.Id],
                Blocking = true,
                Note = "Generated from a unique reviewed knowledge-pack question.",
            });
        }
        return cases;
    }

    private static string StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
