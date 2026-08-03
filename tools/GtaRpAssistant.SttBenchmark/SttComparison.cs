using System.Text.Json;

public static class SttComparison
{
    private const double QualityMargin = .02;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: GtaRpAssistant.SttBenchmark compare <first-report.json> <second-report.json> <comparison.json>");
            return 1;
        }
        var firstPath = Path.GetFullPath(args[0]);
        var secondPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[2]);
        var first = await ReadAsync(firstPath);
        var second = await ReadAsync(secondPath);
        var comparison = Compare(first, second, firstPath, secondPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(comparison, Json));
        Console.WriteLine($"STT comparison: {comparison.Decision}; {comparison.Reason}");
        return comparison.RecommendedPackId is null ? 2 : 0;
    }

    public static SttComparisonReport Compare(SttBenchmarkReport first, SttBenchmarkReport second,
        string firstReportPath = "first.json", string secondReportPath = "second.json")
    {
        ValidateReport(first);
        ValidateReport(second);
        if (first.PackId.Equals(second.PackId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("STT comparison requires two different pack IDs.");
        if (!first.DatasetId.Equals(second.DatasetId, StringComparison.Ordinal)
            || !first.DatasetSha256.Equals(second.DatasetSha256, StringComparison.OrdinalIgnoreCase)
            || first.DatasetCaseCount != second.DatasetCaseCount)
            throw new InvalidDataException("STT reports were not produced from the exact same dataset.");
        if (first.Gate != second.Gate)
            throw new InvalidDataException("STT reports use different quality gates.");
        var firstIds = first.Cases.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        var secondIds = second.Cases.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        if (!firstIds.SequenceEqual(secondIds, StringComparer.Ordinal))
            throw new InvalidDataException("STT reports contain different case IDs.");
        var firstDefinitions = first.Cases.OrderBy(item => item.Id, StringComparer.Ordinal).Select(Definition).ToArray();
        var secondDefinitions = second.Cases.OrderBy(item => item.Id, StringComparer.Ordinal).Select(Definition).ToArray();
        if (!firstDefinitions.SequenceEqual(secondDefinitions, StringComparer.Ordinal))
            throw new InvalidDataException("STT reports contain different case definitions.");

        var (decision, selected, reason) = Select(first, second);
        return new(DateTimeOffset.UtcNow, first.DatasetId, first.DatasetSha256, first.DatasetCaseCount,
            decision, selected?.PackId, selected?.ModelId, reason,
            [Summary(first, firstReportPath), Summary(second, secondReportPath)]);
    }

    public static void ValidateReport(SttBenchmarkReport report)
    {
        if (report.SchemaVersion != 2) throw new InvalidDataException($"Unsupported STT report schema: {report.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(report.PackId) || string.IsNullOrWhiteSpace(report.ModelId)
            || string.IsNullOrWhiteSpace(report.DatasetId))
            throw new InvalidDataException("STT report identity fields are required.");
        if (report.DatasetSha256?.Length != 64 || !report.DatasetSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("STT report dataset SHA-256 is missing or invalid.");
        if (report.Gate is null || report.Cases is null || report.DatasetCaseCount != report.Cases.Count || report.Cases.Count == 0)
            throw new InvalidDataException("STT report case count or gate is invalid.");
        SttDatasetValidation.Validate(new(report.DatasetId, report.Gate,
            report.Cases.Select(item => new SttCase(item.Id, item.AudioFile, item.Reference, item.RequiredTerms)).ToArray()));
        EnsureFinite(report.AverageWordErrorRate, nameof(report.AverageWordErrorRate));
        EnsureFinite(report.TermRecall, nameof(report.TermRecall));
        EnsureFinite(report.P95LatencyMs, nameof(report.P95LatencyMs));
        foreach (var item in report.Cases)
        {
            EnsureFinite(item.WordErrorRate, $"{item.Id}.wordErrorRate");
            EnsureFinite(item.TermRecall, $"{item.Id}.termRecall");
            EnsureFinite(item.LatencyMs, $"{item.Id}.latencyMs");
            if (item.LatencyMs < 0 || item.WorkingSetBytes < 0 || item.PrivateBytes < 0)
                throw new InvalidDataException($"STT case '{item.Id}' contains negative runtime metrics.");
            var expectedWer = item.Error is null ? SttTextMetrics.WordErrorRate(item.Reference, item.Transcript) : 1;
            var expectedRecall = item.Error is null ? SttTextMetrics.TermRecall(item.RequiredTerms, item.Transcript) : 0;
            RequireClose(item.WordErrorRate, expectedWer, $"{item.Id}.wordErrorRate");
            RequireClose(item.TermRecall, expectedRecall, $"{item.Id}.termRecall");
        }

        var averageWer = report.Cases.Average(item => item.WordErrorRate);
        var averageRecall = report.Cases.Average(item => item.TermRecall);
        var p95 = Percentile(report.Cases.Select(item => item.LatencyMs), .95);
        var failures = report.Cases.Count(item => item.Error is not null || string.IsNullOrWhiteSpace(item.Transcript));
        RequireClose(report.AverageWordErrorRate, averageWer, nameof(report.AverageWordErrorRate));
        RequireClose(report.TermRecall, averageRecall, nameof(report.TermRecall));
        RequireClose(report.P95LatencyMs, p95, nameof(report.P95LatencyMs));
        if (report.Failures != failures) throw new InvalidDataException("STT report failure count does not match its cases.");
        if (report.PeakWorkingSetBytes < report.Cases.Max(item => item.WorkingSetBytes)
            || report.PeakPrivateBytes < report.Cases.Max(item => item.PrivateBytes))
            throw new InvalidDataException("STT report peak memory is lower than a case metric.");
        var passed = failures == 0
            && averageWer <= report.Gate.MaximumAverageWordErrorRate
            && averageRecall >= report.Gate.MinimumTermRecall
            && p95 <= report.Gate.MaximumP95LatencyMs
            && Math.Max(report.PeakWorkingSetBytes, report.PeakPrivateBytes) <= report.Gate.MaximumMemoryBytes;
        if (report.Passed != passed) throw new InvalidDataException("STT report pass flag does not match the declared gate.");
    }

    private static (string Decision, SttBenchmarkReport? Selected, string Reason) Select(
        SttBenchmarkReport first, SttBenchmarkReport second)
    {
        if (!first.Passed && !second.Passed)
            return ("reject-both", null, "Ни один кандидат не прошёл обязательный quality/resource gate.");
        if (first.Passed && !second.Passed)
            return ("recommend", first, $"Только {first.ModelId} прошёл обязательный gate.");
        if (!first.Passed && second.Passed)
            return ("recommend", second, $"Только {second.ModelId} прошёл обязательный gate.");

        var werDifference = first.AverageWordErrorRate - second.AverageWordErrorRate;
        if (Math.Abs(werDifference) >= QualityMargin)
        {
            var selected = werDifference < 0 ? first : second;
            return ("recommend", selected, $"Обе модели прошли gate; {selected.ModelId} выбран по материально меньшему WER.");
        }
        var recallDifference = first.TermRecall - second.TermRecall;
        if (Math.Abs(recallDifference) >= QualityMargin)
        {
            var selected = recallDifference > 0 ? first : second;
            return ("recommend", selected, $"Обе модели прошли gate при близком WER; {selected.ModelId} выбран по term recall.");
        }
        var firstMemory = Math.Max(first.PeakWorkingSetBytes, first.PeakPrivateBytes);
        var secondMemory = Math.Max(second.PeakWorkingSetBytes, second.PeakPrivateBytes);
        var resourceWinner = firstMemory != secondMemory
            ? (firstMemory < secondMemory ? first : second)
            : (first.P95LatencyMs <= second.P95LatencyMs ? first : second);
        return ("recommend", resourceWinner,
            $"Обе модели прошли gate с качеством в пределах {QualityMargin:P0}; {resourceWinner.ModelId} выбран по памяти и p95.");
    }

    private static SttCandidateSummary Summary(SttBenchmarkReport report, string reportPath) => new(
        report.PackId, report.ModelId, Path.GetFullPath(reportPath), report.Passed, report.AverageWordErrorRate,
        report.TermRecall, report.P95LatencyMs, report.Failures, report.PeakWorkingSetBytes, report.PeakPrivateBytes);

    private static string Definition(SttCaseReport item) =>
        string.Join('\u001f', item.Id, item.AudioFile, item.Reference, string.Join('\u001e', item.RequiredTerms));

    private static async Task<SttBenchmarkReport> ReadAsync(string path) =>
        JsonSerializer.Deserialize<SttBenchmarkReport>(await File.ReadAllTextAsync(path), Json)
        ?? throw new InvalidDataException($"STT report is empty: {path}");

    private static void EnsureFinite(double value, string field)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException($"STT report field is not finite: {field}");
    }

    private static void RequireClose(double actual, double expected, string field)
    {
        if (Math.Abs(actual - expected) > 0.000001)
            throw new InvalidDataException($"STT report aggregate does not match cases: {field}");
    }

    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.Order().ToArray();
        return values[(int)Math.Ceiling(percentile * values.Length) - 1];
    }
}

public sealed record SttCandidateSummary(string PackId, string ModelId, string ReportPath, bool Passed,
    double AverageWordErrorRate, double TermRecall, double P95LatencyMs, int Failures,
    long PeakWorkingSetBytes, long PeakPrivateBytes);
public sealed record SttComparisonReport(DateTimeOffset CreatedAt, string DatasetId, string DatasetSha256,
    int DatasetCaseCount, string Decision, string? RecommendedPackId, string? RecommendedModelId,
    string Reason, IReadOnlyList<SttCandidateSummary> Candidates, int SchemaVersion = 1);
