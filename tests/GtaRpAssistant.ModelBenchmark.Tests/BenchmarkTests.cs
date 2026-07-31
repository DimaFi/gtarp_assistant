using System.Text.Json;
using GtaRpAssistant.ModelBenchmark;

namespace GtaRpAssistant.ModelBenchmark.Tests;

public sealed class BenchmarkTests
{
    [Fact]
    public void BundledCandidateCatalogIsValid()
    {
        var catalog = BenchmarkJson.Load<CandidateCatalog>(Path.Combine(RepositoryRoot(), "ml", "configs", "micro-model-candidates.json"));
        Assert.Empty(BenchmarkValidation.Validate(catalog));
        Assert.Equal(3, catalog.Candidates.Count);
    }

    [Fact]
    public void BundledRussianEvaluationDatasetIsValid()
    {
        var dataset = BenchmarkJson.Load<EvaluationDataset>(Path.Combine(RepositoryRoot(), "ml", "evaluation", "micro-model-eval.json"));
        Assert.Empty(BenchmarkValidation.Validate(dataset));
        Assert.Contains(dataset.Cases, x => x.PromptInjection);
        Assert.Contains(dataset.Cases, x => !string.IsNullOrWhiteSpace(x.ExpectedIntent));
        Assert.Empty(BenchmarkValidation.ValidateResponseSchema(Path.Combine(RepositoryRoot(), "ml", "evaluation", "micro-model-response.schema.json")));
    }

    [Fact]
    public void CandidateValidationRejectsUnsafeCatalog()
    {
        var candidate = new ModelCandidate
        {
            Id = "bad",
            ParametersMillions = 900,
            Format = "ONNX",
            SourceUrl = "http://example.test/model",
            LicenseUrl = "http://example.test/license",
        };
        var catalog = new CandidateCatalog { SchemaVersion = 1, RuntimeFamily = "llama.cpp", RequiredFormat = "GGUF", Candidates = [candidate, candidate] };
        var errors = BenchmarkValidation.Validate(catalog);
        Assert.Contains(errors, x => x.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Contains(errors, x => x.Contains("270-600M", StringComparison.Ordinal));
    }

    [Fact]
    public void DatasetValidationEnforcesContextBudget()
    {
        var item = new EvaluationCase { Id = "too-long", Task = "intent", Question = "?", Transcript = Enumerable.Repeat("line", 7).ToArray() };
        var dataset = new EvaluationDataset { SchemaVersion = 1, Language = "ru", Cases = Enumerable.Repeat(item, 8).ToArray() };
        Assert.Contains(BenchmarkValidation.Validate(dataset), x => x.Contains("6-line", StringComparison.Ordinal));
    }

    [Fact]
    public void StrictGroundedOutputScoresWithoutHallucination()
    {
        var item = new EvaluationCase
        {
            Id = "case",
            Task = "grounded_answer",
            Question = "Сколько?",
            Facts = [new("fact.1", "Награда 4 BP.")],
            AllowedFactIds = ["fact.1"],
            ExpectedDecision = "show",
        };
        var output = JsonSerializer.Serialize(new MicroModelEvaluationOutput
        {
            Decision = "show",
            PresentationType = "mechanic_help",
            Message = "Награда — 4 BP.",
            UsedFactIds = ["fact.1"],
            Confidence = .9,
        }, BenchmarkJson.Options);
        var result = BenchmarkValidation.Score(item, output, 0, 10, 2, 100, 100);
        Assert.True(result.StrictJson);
        Assert.True(result.SchemaCompliant);
        Assert.True(result.DecisionCorrect);
        Assert.False(result.HallucinatedFact);
        Assert.False(result.UnsupportedNumber);
    }

    [Fact]
    public void UnsupportedFactAndNumberFailScore()
    {
        var item = new EvaluationCase { Id = "case", Task = "grounded_answer", Question = "?", Facts = [new("fact.1", "Награда 4 BP.")], ExpectedDecision = "show" };
        var output = JsonSerializer.Serialize(new MicroModelEvaluationOutput { Decision = "show", Message = "Награда 500 BP.", UsedFactIds = ["fact.fake"], Confidence = .5 }, BenchmarkJson.Options);
        var result = BenchmarkValidation.Score(item, output, 0, 10, 2, 100, 100);
        Assert.True(result.HallucinatedFact);
        Assert.True(result.UnsupportedNumber);
        Assert.False(result.SchemaCompliant);
    }

    [Fact]
    public void ScoreRejectsNonZeroExitAndMissingRequiredProperties()
    {
        var item = new EvaluationCase { Id = "case", Task = "abstain", Question = "?", ExpectedDecision = "abstain" };
        var result = BenchmarkValidation.Score(item, "{\"decision\":\"abstain\"}", 7, 10, 2, 100, 100);
        Assert.False(result.StrictJson);
        Assert.False(result.SchemaCompliant);
        Assert.Equal("runtime_exit_7", result.Failure);
    }

    [Fact]
    public void ScoreRejectsUnallowedFactTranscriptAndWrongServer()
    {
        var item = new EvaluationCase
        {
            Id = "server",
            Task = "grounded_answer",
            Question = "?",
            Server = "A",
            Transcript = ["line"],
            Facts = [new("fact.a", "4 BP", "A"), new("fact.b", "8 BP", "B")],
            AllowedFactIds = ["fact.a"],
            ExpectedDecision = "show",
        };
        var output = JsonSerializer.Serialize(new MicroModelEvaluationOutput
        {
            Decision = "show",
            PresentationType = "mechanic_help",
            Title = "Ответ",
            Message = "8 BP",
            UsedFactIds = ["fact.b"],
            EvidenceTranscriptIds = ["tr.2"],
            Confidence = .5,
        }, BenchmarkJson.Options);
        var result = BenchmarkValidation.Score(item, output, 0, 10, 2, 100, 100);
        Assert.True(result.HallucinatedFact);
        Assert.True(result.WrongServer);
        Assert.False(result.SchemaCompliant);
    }

    [Fact]
    public void ScoreUsesUnifiedSoftAndHardMemoryPolicy()
    {
        var item = new EvaluationCase { Id = "memory", Task = "abstain", Question = "?", ExpectedDecision = "abstain" };
        var soft = BenchmarkValidation.Score(item, "{}", 0, 1, 1, 800L * 1024 * 1024, 100, 100);
        var hard = BenchmarkValidation.Score(item, "{}", 0, 1, 1, 100, 901L * 1024 * 1024, 100);
        Assert.True(soft.SoftMemoryLimitObserved);
        Assert.False(soft.HardMemoryLimitExceeded);
        Assert.True(hard.HardMemoryLimitExceeded);
    }

    [Fact]
    public void ExecutionOptionsEnforceApprovedMatrix()
    {
        new BenchmarkExecutionOptions { ContextTokens = 512, CpuThreads = 1, MaxOutputTokens = 120 }.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkExecutionOptions { ContextTokens = 2048 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BenchmarkExecutionOptions { GpuLayers = 1 }.Validate());
    }

    [Fact]
    public void BundledPromptsStayCompactForSmallContextProfile()
    {
        var dataset = BenchmarkJson.Load<EvaluationDataset>(Path.Combine(RepositoryRoot(), "ml", "evaluation", "micro-model-eval.json"));
        Assert.All(dataset.Cases, item =>
        {
            var prompt = PromptBuilder.Build(item, true);
            Assert.DoesNotContain("OUTPUT_SCHEMA", prompt, StringComparison.Ordinal);
            Assert.True(prompt.Length < 2_200, $"Prompt '{item.Id}' is too large: {prompt.Length} chars.");
        });
    }

    [Fact]
    public void ReleaseGateRequiresApprovedLicenseAndMemory()
    {
        var candidate = new ModelCandidate { LicenseReview = "conditional", DistributionPolicy = "redistributable-with-notice" };
        var metrics = new BenchmarkMetrics
        {
            CaseCount = 10,
            StrictJsonRate = 1,
            SchemaComplianceRate = 1,
            DecisionAccuracy = 1,
            IntentCaseCount = 4,
            IntentAccuracy = 1,
            IntentMacroF1 = 1,
            PeakPrivateBytes = 901L * 1024 * 1024,
        };
        var failures = BenchmarkValidation.EvaluateGate(candidate, metrics, new());
        Assert.Contains("license_not_approved", failures);
        Assert.Contains("hard_memory_limit_exceeded", failures);
    }

    [Fact]
    public void ComparisonPrioritizesPassingGateThenQuality()
    {
        var failed = new BenchmarkReport { CandidateId = "fast-but-failed", PassedReleaseGate = false, Metrics = new() { DecisionAccuracy = 1, AverageLatencyMs = 1 } };
        var passed = new BenchmarkReport { CandidateId = "passed", PassedReleaseGate = true, Metrics = new() { DecisionAccuracy = .8, AverageLatencyMs = 100 } };
        Assert.Equal("passed", BenchmarkComparison.Rank([failed, passed])[0].CandidateId);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GtaRpAssistant.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
