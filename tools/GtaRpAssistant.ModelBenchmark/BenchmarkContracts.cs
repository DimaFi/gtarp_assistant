using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GtaRpAssistant.ModelBenchmark;

public sealed record CandidateCatalog
{
    public int SchemaVersion { get; init; }
    public string RuntimeFamily { get; init; } = "";
    public string RequiredFormat { get; init; } = "";
    public BenchmarkThresholds Thresholds { get; init; } = new();
    public IReadOnlyList<ModelCandidate> Candidates { get; init; } = [];
}

public sealed record BenchmarkThresholds
{
    public int SoftMemoryLimitMb { get; init; } = 750;
    public int HardMemoryLimitMb { get; init; } = 900;
    public double MinimumStrictJsonRate { get; init; } = .98;
    public double MinimumSchemaComplianceRate { get; init; } = .98;
    public double MaximumHallucinatedFactRate { get; init; }
    public double MaximumUnsupportedNumberRate { get; init; }
    public double MinimumDecisionAccuracy { get; init; } = .80;
    public double MinimumIntentAccuracy { get; init; } = .80;
    public double MinimumRussianResponseRate { get; init; } = .80;
}

public sealed record BenchmarkExecutionOptions
{
    public int ContextTokens { get; init; } = 1024;
    public int CpuThreads { get; init; } = 2;
    public int MaxOutputTokens { get; init; } = 150;
    public int GpuLayers { get; init; }
    public TimeSpan CaseTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public void Validate()
    {
        if (ContextTokens is not (512 or 1024)) throw new ArgumentOutOfRangeException(nameof(ContextTokens), "Context must be 512 or 1024 tokens.");
        if (CpuThreads is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(CpuThreads), "CPU threads must be 1 or 2.");
        if (MaxOutputTokens is not (120 or 150)) throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), "Output must be 120 or 150 tokens.");
        if (GpuLayers != 0) throw new ArgumentOutOfRangeException(nameof(GpuLayers), "Benchmark GPU offload must remain disabled.");
        if (CaseTimeout < TimeSpan.FromSeconds(5) || CaseTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(CaseTimeout));
    }
}

public sealed record ModelCandidate
{
    public string Id { get; init; } = "";
    public string Family { get; init; } = "";
    public int ParametersMillions { get; init; }
    public string Format { get; init; } = "";
    public IReadOnlyList<string> Quantizations { get; init; } = [];
    public string SourceUrl { get; init; } = "";
    public string LicenseId { get; init; } = "";
    public string LicenseUrl { get; init; } = "";
    public string LicenseReview { get; init; } = "pending";
    public string DistributionPolicy { get; init; } = "user-download-only";
    public string RussianSupport { get; init; } = "unverified";
    public bool DisableThinking { get; init; } = true;
    public bool BenchmarkEnabled { get; init; } = true;
    public string BenchmarkArtifactUrl { get; init; } = "";
    public string BenchmarkArtifactRevision { get; init; } = "";
    public string BenchmarkArtifactSha256 { get; init; } = "";
    public string Notes { get; init; } = "";
}

public sealed record EvaluationDataset
{
    public int SchemaVersion { get; init; }
    public string Language { get; init; } = "";
    public IReadOnlyList<EvaluationCase> Cases { get; init; } = [];
}

public sealed record EvaluationCase
{
    public string Id { get; init; } = "";
    public string Task { get; init; } = "";
    public string Question { get; init; } = "";
    public IReadOnlyList<string> Transcript { get; init; } = [];
    public IReadOnlyList<EvaluationFact> Facts { get; init; } = [];
    public string ExpectedDecision { get; init; } = "";
    public string ExpectedIntent { get; init; } = "";
    public string Server { get; init; } = "";
    public IReadOnlyList<string> AllowedFactIds { get; init; } = [];
    public bool PromptInjection { get; init; }
}

public sealed record EvaluationFact(string Id, string Text, string Server = "");

public sealed record MicroModelEvaluationOutput
{
    public string Decision { get; init; } = "";
    public string Intent { get; init; } = "";
    public string PresentationType { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public IReadOnlyList<string> UsedFactIds { get; init; } = [];
    public IReadOnlyList<string> EvidenceTranscriptIds { get; init; } = [];
    public double Confidence { get; init; }
    public bool NeedsVisualContext { get; init; }
    public bool NeedsSmartModel { get; init; }
}

public sealed record BenchmarkCaseResult
{
    public string CaseId { get; init; } = "";
    public bool StrictJson { get; init; }
    public bool SchemaCompliant { get; init; }
    public bool DecisionCorrect { get; init; }
    public bool IntentApplicable { get; init; }
    public bool IntentCorrect { get; init; }
    public string ExpectedIntent { get; init; } = "";
    public string ActualIntent { get; init; } = "";
    public bool HallucinatedFact { get; init; }
    public bool UnsupportedNumber { get; init; }
    public bool WrongServer { get; init; }
    public bool RussianResponse { get; init; }
    public bool IntentFalsePositive { get; init; }
    public bool IntentFalseNegative { get; init; }
    public bool TimedOut { get; init; }
    public bool SoftMemoryLimitObserved { get; init; }
    public bool HardMemoryLimitExceeded { get; init; }
    public int ExitCode { get; init; }
    public double ElapsedMs { get; init; }
    public double TimeToFirstOutputMs { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long PeakPrivateBytes { get; init; }
    public long PeakCommittedBytes { get; init; }
    public long PeakMemoryBytes { get; init; }
    public double PeakCpuPercent { get; init; }
    public string Failure { get; init; } = "";
    public string Diagnostic { get; init; } = "";
    public string ResponsePreview { get; init; } = "";
}

public sealed record BenchmarkMetrics
{
    public int CaseCount { get; init; }
    public double StrictJsonRate { get; init; }
    public double SchemaComplianceRate { get; init; }
    public double DecisionAccuracy { get; init; }
    public int IntentCaseCount { get; init; }
    public double IntentAccuracy { get; init; }
    public double IntentMacroF1 { get; init; }
    public double HallucinatedFactRate { get; init; }
    public double UnsupportedNumberRate { get; init; }
    public double WrongServerRate { get; init; }
    public double RussianResponseRate { get; init; }
    public double IntentFalsePositiveRate { get; init; }
    public double IntentFalseNegativeRate { get; init; }
    public double RuntimeFailureRate { get; init; }
    public int SoftMemoryLimitCaseCount { get; init; }
    public double AverageLatencyMs { get; init; }
    public double AverageTimeToFirstOutputMs { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long PeakPrivateBytes { get; init; }
    public long PeakCommittedBytes { get; init; }
    public long PeakMemoryBytes { get; init; }
    public double PeakCpuPercent { get; init; }
}

public sealed record BenchmarkReport
{
    public int SchemaVersion { get; init; } = 1;
    public string CandidateId { get; init; } = "";
    public string ModelFileName { get; init; } = "";
    public long ModelFileBytes { get; init; }
    public string ModelSha256 { get; init; } = "";
    public string RuntimeFileName { get; init; } = "";
    public BenchmarkExecutionOptions Execution { get; init; } = new();
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Machine { get; init; } = "";
    public string LicenseReview { get; init; } = "";
    public string DistributionPolicy { get; init; } = "";
    public BenchmarkMetrics Metrics { get; init; } = new();
    public IReadOnlyList<BenchmarkCaseResult> Cases { get; init; } = [];
    public bool PassedReleaseGate { get; init; }
    public IReadOnlyList<string> GateFailures { get; init; } = [];
}

public static class BenchmarkJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T Load<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"JSON file '{path}' is empty or invalid.");
}

public static class BenchmarkValidation
{
    private static readonly HashSet<string> Tasks = new(StringComparer.Ordinal)
    {
        "intent", "reranking", "grounded_answer", "follow_up", "abstain", "escalation", "prompt_injection",
    };

    public static IReadOnlyList<string> Validate(CandidateCatalog catalog)
    {
        var errors = new List<string>();
        if (catalog.SchemaVersion != 1) errors.Add("Candidate catalog schemaVersion must be 1.");
        if (!string.Equals(catalog.RuntimeFamily, "llama.cpp", StringComparison.Ordinal)) errors.Add("runtimeFamily must be llama.cpp.");
        if (!string.Equals(catalog.RequiredFormat, "GGUF", StringComparison.Ordinal)) errors.Add("requiredFormat must be GGUF.");
        if (catalog.Candidates.Count < 2) errors.Add("At least two candidates are required.");
        if (catalog.Thresholds.SoftMemoryLimitMb <= 0 || catalog.Thresholds.SoftMemoryLimitMb >= catalog.Thresholds.HardMemoryLimitMb || catalog.Thresholds.HardMemoryLimitMb > 900)
            errors.Add("Memory thresholds must be positive, ordered and keep the hard limit at or below 900 MB.");
        if (catalog.Thresholds.MinimumRussianResponseRate is < 0 or > 1) errors.Add("minimumRussianResponseRate must be between 0 and 1.");

        foreach (var duplicate in catalog.Candidates.GroupBy(x => x.Id, StringComparer.Ordinal).Where(x => x.Count() > 1))
            errors.Add($"Duplicate candidate id '{duplicate.Key}'.");
        foreach (var candidate in catalog.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Id)) errors.Add("Candidate id is required.");
            if (candidate.ParametersMillions is < 270 or > 600) errors.Add($"Candidate '{candidate.Id}' is outside the 270-600M range.");
            if (!string.Equals(candidate.Format, "GGUF", StringComparison.Ordinal)) errors.Add($"Candidate '{candidate.Id}' must use GGUF.");
            if (!IsHttps(candidate.SourceUrl) || !IsHttps(candidate.LicenseUrl)) errors.Add($"Candidate '{candidate.Id}' must use HTTPS source and license URLs.");
            if (candidate.Quantizations.Count == 0 || candidate.Quantizations.Any(x => !x.StartsWith('Q'))) errors.Add($"Candidate '{candidate.Id}' has no valid quantization target.");
            if (candidate.LicenseReview is not ("approved" or "conditional" or "pending")) errors.Add($"Candidate '{candidate.Id}' has an unknown licenseReview value.");
            if (candidate.DistributionPolicy is not ("redistributable" or "redistributable-with-notice" or "user-download-only")) errors.Add($"Candidate '{candidate.Id}' has an unknown distributionPolicy value.");
            if (!string.IsNullOrWhiteSpace(candidate.BenchmarkArtifactUrl) && !IsHttps(candidate.BenchmarkArtifactUrl)) errors.Add($"Candidate '{candidate.Id}' benchmarkArtifactUrl must use HTTPS.");
            if (!string.IsNullOrWhiteSpace(candidate.BenchmarkArtifactSha256) && !Regex.IsMatch(candidate.BenchmarkArtifactSha256, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)) errors.Add($"Candidate '{candidate.Id}' benchmarkArtifactSha256 is invalid.");
        }
        return errors;
    }

    public static IReadOnlyList<string> Validate(EvaluationDataset dataset)
    {
        var errors = new List<string>();
        if (dataset.SchemaVersion != 1) errors.Add("Evaluation dataset schemaVersion must be 1.");
        if (!string.Equals(dataset.Language, "ru", StringComparison.OrdinalIgnoreCase)) errors.Add("Evaluation dataset language must be ru.");
        if (dataset.Cases.Count < 8) errors.Add("Evaluation dataset must contain at least 8 cases.");
        foreach (var duplicate in dataset.Cases.GroupBy(x => x.Id, StringComparer.Ordinal).Where(x => x.Count() > 1)) errors.Add($"Duplicate case id '{duplicate.Key}'.");
        foreach (var item in dataset.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Question)) errors.Add("Every evaluation case needs id and question.");
            if (!Tasks.Contains(item.Task)) errors.Add($"Case '{item.Id}' has unsupported task '{item.Task}'.");
            if (item.Transcript.Count > 6) errors.Add($"Case '{item.Id}' exceeds the 6-line transcript budget.");
            if (item.Facts.Count > 8) errors.Add($"Case '{item.Id}' exceeds the 8-fact budget.");
            if (item.Facts.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1)) errors.Add($"Case '{item.Id}' has duplicate fact ids.");
            if (item.AllowedFactIds.Except(item.Facts.Select(x => x.Id), StringComparer.Ordinal).Any()) errors.Add($"Case '{item.Id}' allows an unknown fact id.");
        }
        var required = new[] { "intent", "grounded_answer", "abstain", "escalation", "prompt_injection" };
        foreach (var task in required.Where(task => dataset.Cases.All(x => !string.Equals(x.Task, task, StringComparison.Ordinal))))
            errors.Add($"Evaluation dataset has no '{task}' case.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateResponseSchema(string path)
    {
        var errors = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "object") errors.Add("Response schema root type must be object.");
            if (!root.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False) errors.Add("Response schema must reject additional properties.");
            var requiredNames = root.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
                ? required.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).ToHashSet(StringComparer.Ordinal)
                : [];
            foreach (var name in new[] { "decision", "intent", "presentationType", "title", "message", "usedFactIds", "evidenceTranscriptIds", "confidence", "needsVisualContext", "needsSmartModel" })
                if (!requiredNames.Contains(name)) errors.Add($"Response schema must require '{name}'.");
            if (!root.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("message", out var message)
                || !message.TryGetProperty("maxLength", out var maxLength)
                || maxLength.GetInt32() > 350)
                errors.Add("Response schema must limit message to 350 characters.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            errors.Add($"Response schema is invalid: {ex.Message}");
        }
        return errors;
    }

    public static BenchmarkMetrics Summarize(IReadOnlyList<BenchmarkCaseResult> cases)
    {
        if (cases.Count == 0) return new();
        return new()
        {
            CaseCount = cases.Count,
            StrictJsonRate = cases.Count(x => x.StrictJson) / (double)cases.Count,
            SchemaComplianceRate = cases.Count(x => x.SchemaCompliant) / (double)cases.Count,
            DecisionAccuracy = cases.Count(x => x.DecisionCorrect) / (double)cases.Count,
            IntentCaseCount = cases.Count(x => x.IntentApplicable),
            IntentAccuracy = IntentCases(cases).Count == 0 ? 0 : IntentCases(cases).Count(x => x.IntentCorrect) / (double)IntentCases(cases).Count,
            IntentMacroF1 = CalculateIntentMacroF1(IntentCases(cases)),
            HallucinatedFactRate = cases.Count(x => x.HallucinatedFact) / (double)cases.Count,
            UnsupportedNumberRate = cases.Count(x => x.UnsupportedNumber) / (double)cases.Count,
            WrongServerRate = cases.Count(x => x.WrongServer) / (double)cases.Count,
            RussianResponseRate = cases.Count(x => x.RussianResponse) / (double)cases.Count,
            IntentFalsePositiveRate = IntentCases(cases).Count == 0 ? 0 : IntentCases(cases).Count(x => x.IntentFalsePositive) / (double)IntentCases(cases).Count,
            IntentFalseNegativeRate = IntentCases(cases).Count == 0 ? 0 : IntentCases(cases).Count(x => x.IntentFalseNegative) / (double)IntentCases(cases).Count,
            RuntimeFailureRate = cases.Count(x => x.ExitCode != 0 || x.TimedOut) / (double)cases.Count,
            SoftMemoryLimitCaseCount = cases.Count(x => x.SoftMemoryLimitObserved),
            AverageLatencyMs = cases.Average(x => x.ElapsedMs),
            AverageTimeToFirstOutputMs = cases.Average(x => x.TimeToFirstOutputMs),
            PeakWorkingSetBytes = cases.Max(x => x.PeakWorkingSetBytes),
            PeakPrivateBytes = cases.Max(x => x.PeakPrivateBytes),
            PeakCommittedBytes = cases.Max(x => x.PeakCommittedBytes),
            PeakMemoryBytes = cases.Max(x => x.PeakMemoryBytes),
            PeakCpuPercent = cases.Max(x => x.PeakCpuPercent),
        };
    }

    public static IReadOnlyList<string> EvaluateGate(ModelCandidate candidate, BenchmarkMetrics metrics, BenchmarkThresholds thresholds)
    {
        var failures = new List<string>();
        if (candidate.LicenseReview != "approved") failures.Add("license_not_approved");
        if (candidate.DistributionPolicy == "user-download-only") failures.Add("distribution_not_approved");
        var peakMemory = Math.Max(metrics.PeakMemoryBytes, Math.Max(metrics.PeakWorkingSetBytes, Math.Max(metrics.PeakPrivateBytes, metrics.PeakCommittedBytes)));
        if (peakMemory >= thresholds.HardMemoryLimitMb * 1024L * 1024L) failures.Add("hard_memory_limit_exceeded");
        if (metrics.RuntimeFailureRate > 0) failures.Add("runtime_failure_rate");
        if (metrics.StrictJsonRate < thresholds.MinimumStrictJsonRate) failures.Add("strict_json_rate");
        if (metrics.SchemaComplianceRate < thresholds.MinimumSchemaComplianceRate) failures.Add("schema_compliance_rate");
        if (metrics.HallucinatedFactRate > thresholds.MaximumHallucinatedFactRate) failures.Add("hallucinated_fact_rate");
        if (metrics.UnsupportedNumberRate > thresholds.MaximumUnsupportedNumberRate) failures.Add("unsupported_number_rate");
        if (metrics.WrongServerRate > 0) failures.Add("wrong_server_rate");
        if (metrics.RussianResponseRate < thresholds.MinimumRussianResponseRate) failures.Add("russian_response_rate");
        if (metrics.DecisionAccuracy < thresholds.MinimumDecisionAccuracy) failures.Add("decision_accuracy");
        if (metrics.IntentCaseCount == 0 || metrics.IntentAccuracy < thresholds.MinimumIntentAccuracy) failures.Add("intent_accuracy");
        return failures;
    }

    public static BenchmarkCaseResult Score(
        EvaluationCase item,
        string output,
        int exitCode,
        double elapsedMs,
        double firstOutputMs,
        long peakWorkingSet,
        long peakPrivate,
        long peakCommitted = 0,
        double peakCpuPercent = 0,
        bool timedOut = false,
        string diagnostic = "",
        BenchmarkThresholds? thresholds = null)
    {
        var trimmed = output.Trim();
        MicroModelEvaluationOutput? parsed = null;
        var strictJson = false;
        JsonDocument? parsedDocument = null;
        try
        {
            parsedDocument = JsonDocument.Parse(trimmed);
            parsed = parsedDocument.RootElement.Deserialize<MicroModelEvaluationOutput>(BenchmarkJson.Options);
            strictJson = exitCode == 0 && !timedOut && parsed is not null && IsStrictResponseObject(parsedDocument.RootElement);
        }
        catch (JsonException) { }

        var allowedDecisions = new HashSet<string>(["show", "clarify", "abstain", "escalate"], StringComparer.Ordinal);
        var allowedPresentationTypes = new HashSet<string>(["rule_warning", "next_step", "mechanic_help", "context_answer"], StringComparer.Ordinal);
        var allowedIntents = new HashSet<string>(["possible_robbery", "possible_police_stop", "rule_question", "mechanic_question", "none", "unknown", ""], StringComparer.Ordinal);
        var allowedFactIds = item.AllowedFactIds.ToHashSet(StringComparer.Ordinal);
        var transcriptIds = Enumerable.Range(1, item.Transcript.Count).Select(index => $"tr.{index}").ToHashSet(StringComparer.Ordinal);
        var schemaCompliant = strictJson && parsed is not null
            && allowedDecisions.Contains(parsed.Decision)
            && allowedPresentationTypes.Contains(parsed.PresentationType)
            && allowedIntents.Contains(parsed.Intent)
            && parsed.Title.Length <= 80
            && parsed.Message.Length <= 350
            && parsed.Confidence is >= 0 and <= 1
            && parsed.UsedFactIds.Count <= 8
            && parsed.EvidenceTranscriptIds.Count <= 6
            && parsed.UsedFactIds.Distinct(StringComparer.Ordinal).Count() == parsed.UsedFactIds.Count
            && parsed.EvidenceTranscriptIds.Distinct(StringComparer.Ordinal).Count() == parsed.EvidenceTranscriptIds.Count
            && parsed.UsedFactIds.All(allowedFactIds.Contains)
            && parsed.EvidenceTranscriptIds.All(transcriptIds.Contains)
            && (parsed.Decision != "show" || allowedFactIds.Count == 0 || parsed.UsedFactIds.Count > 0)
            && (string.IsNullOrWhiteSpace(item.ExpectedIntent) || !string.IsNullOrWhiteSpace(parsed.Intent));
        var hallucinatedFact = parsed?.UsedFactIds.Any(x => !allowedFactIds.Contains(x)) == true
            || parsed is { Decision: "show" } && allowedFactIds.Count > 0 && parsed.UsedFactIds.Count == 0;
        var supportedNumbers = item.Facts.SelectMany(x => Numbers(x.Text)).ToHashSet(StringComparer.Ordinal);
        var unsupportedNumber = parsed is not null && Numbers($"{parsed.Title} {parsed.Message}").Any(x => !supportedNumbers.Contains(x));
        var factsById = item.Facts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var wrongServer = parsed is not null
            && !string.IsNullOrWhiteSpace(item.Server)
            && parsed.UsedFactIds.Any(id => factsById.TryGetValue(id, out var fact)
                && !string.IsNullOrWhiteSpace(fact.Server)
                && !string.Equals(fact.Server, item.Server, StringComparison.OrdinalIgnoreCase));
        var russianResponse = parsed is not null && Regex.IsMatch($"{parsed.Title} {parsed.Message}", @"\p{IsCyrillic}", RegexOptions.CultureInvariant);
        var decisionCorrect = parsed is not null && string.Equals(parsed.Decision, item.ExpectedDecision, StringComparison.Ordinal);
        var intentApplicable = !string.IsNullOrWhiteSpace(item.ExpectedIntent);
        var intentCorrect = !intentApplicable || parsed is not null && string.Equals(parsed.Intent, item.ExpectedIntent, StringComparison.Ordinal);
        var actualIntent = parsed?.Intent ?? "";
        var intentFalsePositive = intentApplicable && item.ExpectedIntent == "none" && actualIntent is not ("" or "none" or "unknown");
        var intentFalseNegative = intentApplicable && item.ExpectedIntent != "none" && actualIntent is ("" or "none" or "unknown");
        var peakMemory = Math.Max(peakWorkingSet, Math.Max(peakPrivate, peakCommitted));
        thresholds ??= new BenchmarkThresholds();
        var softObserved = peakMemory >= thresholds.SoftMemoryLimitMb * 1024L * 1024L;
        var hardExceeded = peakMemory >= thresholds.HardMemoryLimitMb * 1024L * 1024L;
        var failure = timedOut ? "runtime_timeout" : exitCode != 0 ? $"runtime_exit_{exitCode}" : !strictJson ? "invalid_json" : !schemaCompliant ? "schema_violation" : "";

        parsedDocument?.Dispose();
        return new()
        {
            CaseId = item.Id,
            StrictJson = strictJson,
            SchemaCompliant = schemaCompliant,
            DecisionCorrect = decisionCorrect,
            IntentApplicable = intentApplicable,
            IntentCorrect = intentCorrect,
            ExpectedIntent = item.ExpectedIntent,
            ActualIntent = actualIntent,
            HallucinatedFact = hallucinatedFact,
            UnsupportedNumber = unsupportedNumber,
            WrongServer = wrongServer,
            RussianResponse = russianResponse,
            IntentFalsePositive = intentFalsePositive,
            IntentFalseNegative = intentFalseNegative,
            TimedOut = timedOut,
            SoftMemoryLimitObserved = softObserved,
            HardMemoryLimitExceeded = hardExceeded,
            ExitCode = exitCode,
            ElapsedMs = elapsedMs,
            TimeToFirstOutputMs = firstOutputMs,
            PeakWorkingSetBytes = peakWorkingSet,
            PeakPrivateBytes = peakPrivate,
            PeakCommittedBytes = peakCommitted,
            PeakMemoryBytes = peakMemory,
            PeakCpuPercent = peakCpuPercent,
            Failure = failure,
            Diagnostic = NormalizeDiagnostic(diagnostic),
            ResponsePreview = NormalizePreview(trimmed),
        };
    }

    private static bool IsStrictResponseObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        var allowed = new HashSet<string>(["decision", "intent", "presentationType", "title", "message", "usedFactIds", "evidenceTranscriptIds", "confidence", "needsVisualContext", "needsSmartModel"], StringComparer.Ordinal);
        var required = new HashSet<string>(["decision", "intent", "presentationType", "title", "message", "usedFactIds", "evidenceTranscriptIds", "confidence", "needsVisualContext", "needsSmartModel"], StringComparer.Ordinal);
        var names = root.EnumerateObject().Select(x => x.Name).ToArray();
        return names.All(allowed.Contains) && required.IsSubsetOf(names);
    }

    private static string NormalizeDiagnostic(string value)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    private static string NormalizePreview(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return normalized.Length <= 800 ? normalized : normalized[..800];
    }

    private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    private static IEnumerable<string> Numbers(string value) => Regex.Matches(value, @"(?<![\p{L}\p{N}])\d+(?:[.,]\d+)?(?![\p{L}\p{N}])").Select(x => x.Value.Replace(',', '.'));

    private static IReadOnlyList<BenchmarkCaseResult> IntentCases(IReadOnlyList<BenchmarkCaseResult> cases) => cases.Where(x => x.IntentApplicable).ToArray();

    private static double CalculateIntentMacroF1(IReadOnlyList<BenchmarkCaseResult> cases)
    {
        if (cases.Count == 0) return 0;
        var labels = cases.SelectMany(x => new[] { x.ExpectedIntent, x.ActualIntent }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        if (labels.Length == 0) return 0;
        return labels.Average(label =>
        {
            var truePositive = cases.Count(x => x.ExpectedIntent == label && x.ActualIntent == label);
            var falsePositive = cases.Count(x => x.ExpectedIntent != label && x.ActualIntent == label);
            var falseNegative = cases.Count(x => x.ExpectedIntent == label && x.ActualIntent != label);
            var precision = truePositive + falsePositive == 0 ? 0 : truePositive / (double)(truePositive + falsePositive);
            var recall = truePositive + falseNegative == 0 ? 0 : truePositive / (double)(truePositive + falseNegative);
            return precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        });
    }
}

public static class BenchmarkComparison
{
    public static IReadOnlyList<BenchmarkReport> Rank(IEnumerable<BenchmarkReport> reports) => reports
        .OrderByDescending(x => x.PassedReleaseGate)
        .ThenByDescending(x => x.Metrics.DecisionAccuracy)
        .ThenByDescending(x => x.Metrics.SchemaComplianceRate)
        .ThenBy(x => x.Metrics.HallucinatedFactRate)
        .ThenBy(x => x.Metrics.WrongServerRate)
        .ThenBy(x => x.Metrics.PeakMemoryBytes)
        .ThenBy(x => x.Metrics.AverageLatencyMs)
        .ToArray();
}
