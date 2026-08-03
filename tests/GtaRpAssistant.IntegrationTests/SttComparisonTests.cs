namespace GtaRpAssistant.IntegrationTests;

public sealed class SttComparisonTests
{
    [Fact]
    public void DifferentDatasetFingerprint_IsRejected()
    {
        var first = Passing("pack-a", "model-a") with { DatasetSha256 = new string('a', 64) };
        var second = Passing("pack-b", "model-b") with { DatasetSha256 = new string('b', 64) };

        var error = Assert.Throws<InvalidDataException>(() => SttComparison.Compare(first, second));

        Assert.Contains("same dataset", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentGate_IsRejected()
    {
        var first = Passing("pack-a", "model-a");
        var second = Passing("pack-b", "model-b") with
        {
            Gate = new SttGate(1, .4, .5, 5_000, 1_100L * 1024 * 1024),
        };

        var error = Assert.Throws<InvalidDataException>(() => SttComparison.Compare(first, second));

        Assert.Contains("different quality gates", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoFailedCandidates_AreRejected()
    {
        var result = SttComparison.Compare(Failing("pack-a", "model-a"), Failing("pack-b", "model-b"));

        Assert.Equal("reject-both", result.Decision);
        Assert.Null(result.RecommendedPackId);
    }

    [Fact]
    public void SolePassingCandidate_IsRecommended()
    {
        var result = SttComparison.Compare(Passing("pack-a", "model-a"), Failing("pack-b", "model-b"));

        Assert.Equal("pack-a", result.RecommendedPackId);
    }

    [Fact]
    public void MateriallyLowerWerWinsWhenBothPass()
    {
        var exactCase = Case("один два три четыре", "один два три четыре");
        var exact = Report("pack-a", "model-a", true, [exactCase], 700L * 1024 * 1024);
        var weakerCase = Case("один два три четыре", "один два три");
        var weaker = Report("pack-b", "model-b", true, [weakerCase], 700L * 1024 * 1024);

        var result = SttComparison.Compare(exact, weaker);

        Assert.Equal("pack-a", result.RecommendedPackId);
        Assert.Contains("WER", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowerMemoryWinsWhenQualityIsEquivalent()
    {
        var light = Passing("pack-a", "model-a", 600L * 1024 * 1024);
        var heavy = Passing("pack-b", "model-b", 900L * 1024 * 1024);

        var result = SttComparison.Compare(light, heavy);

        Assert.Equal("pack-a", result.RecommendedPackId);
        Assert.Contains("памяти", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TamperedAggregate_IsRejected()
    {
        var report = Passing("pack-a", "model-a") with { AverageWordErrorRate = .2 };

        var error = Assert.Throws<InvalidDataException>(() => SttComparison.ValidateReport(report));

        Assert.Contains("aggregate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandWritesMachineReadableRecommendation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GtaRpAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var firstPath = Path.Combine(directory, "first.json");
            var secondPath = Path.Combine(directory, "second.json");
            var outputPath = Path.Combine(directory, "comparison.json");
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            await File.WriteAllTextAsync(firstPath, System.Text.Json.JsonSerializer.Serialize(Passing("pack-a", "model-a", 600L * 1024 * 1024), options));
            await File.WriteAllTextAsync(secondPath, System.Text.Json.JsonSerializer.Serialize(Passing("pack-b", "model-b", 900L * 1024 * 1024), options));

            var exitCode = await SttComparison.RunAsync([firstPath, secondPath, outputPath]);
            var result = System.Text.Json.JsonSerializer.Deserialize<SttComparisonReport>(await File.ReadAllTextAsync(outputPath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal(0, exitCode);
            Assert.Equal("pack-a", result?.RecommendedPackId);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static SttBenchmarkReport Passing(string pack, string model, long memory = 700L * 1024 * 1024) =>
        Report(pack, model, true, [Case("тестовая фраза", "тестовая фраза")], memory);

    private static SttBenchmarkReport Failing(string pack, string model)
    {
        var item = new SttCaseReport("case-1", "audio/case-1.wav", "тестовая фраза", "", 1, 0,
            100, 0, 0, "runtime error", []);
        return Report(pack, model, false, [item], 0);
    }

    private static SttBenchmarkReport Report(string pack, string model, bool passed,
        IReadOnlyList<SttCaseReport> cases, long memory)
    {
        var failures = cases.Count(item => item.Error is not null || string.IsNullOrWhiteSpace(item.Transcript));
        return new(DateTimeOffset.UtcNow, pack, model, "dataset", passed,
            cases.Average(item => item.WordErrorRate), cases.Average(item => item.TermRecall),
            cases.Max(item => item.LatencyMs), failures, memory, memory, cases,
            new string('a', 64), cases.Count,
            new SttGate(1, MaximumAverageWordErrorRate: .5, MinimumTermRecall: .5,
                MaximumP95LatencyMs: 5_000, MaximumMemoryBytes: 1_100L * 1024 * 1024));
    }

    private static SttCaseReport Case(string reference, string transcript)
    {
        var terms = Array.Empty<string>();
        return new("case-1", "audio/case-1.wav", reference, transcript,
            SttTextMetrics.WordErrorRate(reference, transcript), SttTextMetrics.TermRecall(terms, transcript),
            100, 500L * 1024 * 1024, 500L * 1024 * 1024, null, terms);
    }
}
