using System.Text.Json;
using GtaRpAssistant.ModelBenchmark;

return await MainAsync(args);

static async Task<int> MainAsync(string[] arguments)
{
    try
    {
        if (arguments.Length == 0) return Usage();
        return arguments[0] switch
        {
            "validate" => ValidateAll(arguments),
            "validate-candidates" => ValidateCandidates(arguments),
            "validate-dataset" => ValidateDataset(arguments),
            "benchmark-model" or "evaluate-model" or "memory-test" or "cold-start-test" => await BenchmarkAsync(arguments),
            "compare-models" => Compare(arguments),
            _ => Usage(),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int ValidateAll(IReadOnlyList<string> arguments)
{
    var catalogPath = arguments.Count > 1 ? arguments[1] : "ml/configs/micro-model-candidates.json";
    var datasetPath = arguments.Count > 2 ? arguments[2] : "ml/evaluation/micro-model-eval.json";
    var schemaPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(datasetPath)) ?? ".", "micro-model-response.schema.json");
    var errors = BenchmarkValidation.Validate(BenchmarkJson.Load<CandidateCatalog>(catalogPath))
        .Concat(BenchmarkValidation.Validate(BenchmarkJson.Load<EvaluationDataset>(datasetPath)))
        .Concat(BenchmarkValidation.ValidateResponseSchema(schemaPath))
        .ToArray();
    return PrintValidation(errors, "Candidate catalog and evaluation dataset are valid.");
}

static int ValidateCandidates(IReadOnlyList<string> arguments)
{
    var path = arguments.Count > 1 ? arguments[1] : "ml/configs/micro-model-candidates.json";
    return PrintValidation(BenchmarkValidation.Validate(BenchmarkJson.Load<CandidateCatalog>(path)), "Candidate catalog is valid.");
}

static int ValidateDataset(IReadOnlyList<string> arguments)
{
    var path = arguments.Count > 1 ? arguments[1] : "ml/evaluation/micro-model-eval.json";
    return PrintValidation(BenchmarkValidation.Validate(BenchmarkJson.Load<EvaluationDataset>(path)), "Evaluation dataset is valid.");
}

static int PrintValidation(IReadOnlyCollection<string> errors, string success)
{
    if (errors.Count == 0)
    {
        Console.WriteLine(success);
        return 0;
    }
    foreach (var error in errors) Console.Error.WriteLine(error);
    return 1;
}

static async Task<int> BenchmarkAsync(IReadOnlyList<string> arguments)
{
    if (arguments.Count < 4)
    {
        Console.Error.WriteLine("Expected: <command> <model.gguf> <llama-completion.exe> <candidate-id> [catalog.json] [dataset.json] [report.json] [--context 512|1024] [--threads 1|2] [--output 120|150] [--timeout-seconds N]");
        return 2;
    }
    var optionalArguments = arguments.Skip(4).ToArray();
    var optionIndex = Array.FindIndex(optionalArguments, value => value.StartsWith("--", StringComparison.Ordinal));
    var positional = optionIndex < 0 ? optionalArguments : optionalArguments[..optionIndex];
    var options = optionIndex < 0 ? Array.Empty<string>() : optionalArguments[optionIndex..];
    var catalogPath = positional.Length > 0 ? positional[0] : "ml/configs/micro-model-candidates.json";
    var datasetPath = positional.Length > 1 ? positional[1] : "ml/evaluation/micro-model-eval.json";
    var catalog = BenchmarkJson.Load<CandidateCatalog>(catalogPath);
    var dataset = BenchmarkJson.Load<EvaluationDataset>(datasetPath);
    var schemaPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(datasetPath)) ?? ".", "micro-model-response.schema.json");
    var validationErrors = BenchmarkValidation.Validate(catalog)
        .Concat(BenchmarkValidation.Validate(dataset))
        .Concat(BenchmarkValidation.ValidateResponseSchema(schemaPath))
        .ToArray();
    if (validationErrors.Length > 0) return PrintValidation(validationErrors, "");
    var candidate = catalog.Candidates.SingleOrDefault(x => string.Equals(x.Id, arguments[3], StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Candidate '{arguments[3]}' is not present in the catalog.");
    if (!candidate.BenchmarkEnabled) throw new InvalidOperationException($"Candidate '{candidate.Id}' is disabled for benchmark.");

    var execution = new BenchmarkExecutionOptions
    {
        ContextTokens = GetIntOption(options, "--context", 1024),
        CpuThreads = GetIntOption(options, "--threads", 2),
        MaxOutputTokens = GetIntOption(options, "--output", 150),
        GpuLayers = 0,
        CaseTimeout = TimeSpan.FromSeconds(GetIntOption(options, "--timeout-seconds", 60)),
    };
    execution.Validate();

    if (arguments[0] == "cold-start-test") dataset = dataset with { Cases = dataset.Cases.Take(1).ToArray() };
    var report = await new LlamaCliBenchmarkRunner().RunAsync(arguments[2], arguments[1], candidate, dataset, catalog.Thresholds, schemaPath, execution, CancellationToken.None);
    var outputPath = positional.Length > 2
        ? Path.GetFullPath(positional[2])
        : Path.GetFullPath(Path.Combine("artifacts", "model-benchmarks", $"{candidate.Id}-c{execution.ContextTokens}-t{execution.CpuThreads}-o{execution.MaxOutputTokens}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"));
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
    await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(report, BenchmarkJson.Options));
    Console.WriteLine(outputPath);
    Console.WriteLine(report.PassedReleaseGate ? "Release gate: PASS" : $"Release gate: FAIL ({string.Join(", ", report.GateFailures)})");
    return report.PassedReleaseGate ? 0 : 3;
}

static int Compare(IReadOnlyList<string> arguments)
{
    if (arguments.Count < 2)
    {
        Console.Error.WriteLine("Expected: compare-models <report.json> [report.json ...]");
        return 2;
    }
    var ranked = BenchmarkComparison.Rank(arguments.Skip(1).Select(BenchmarkJson.Load<BenchmarkReport>));
    for (var index = 0; index < ranked.Count; index++)
    {
        var report = ranked[index];
        Console.WriteLine($"{index + 1}. {report.CandidateId}: gate={(report.PassedReleaseGate ? "PASS" : "FAIL")}; profile=c{report.Execution.ContextTokens}/t{report.Execution.CpuThreads}/o{report.Execution.MaxOutputTokens}; accuracy={report.Metrics.DecisionAccuracy:P1}; JSON={report.Metrics.StrictJsonRate:P1}; peak={report.Metrics.PeakMemoryBytes / 1024d / 1024d:F1} MB; latency={report.Metrics.AverageLatencyMs:F0} ms");
    }
    return ranked.Count > 0 && ranked[0].PassedReleaseGate ? 0 : 3;
}

static int Usage()
{
    Console.Error.WriteLine("Commands: validate, validate-candidates, validate-dataset, benchmark-model, evaluate-model, memory-test, cold-start-test, compare-models");
    return 2;
}

static int GetIntOption(IReadOnlyList<string> options, string name, int fallback)
{
    for (var index = 0; index < options.Count; index++)
    {
        if (!string.Equals(options[index], name, StringComparison.Ordinal)) continue;
        if (index + 1 >= options.Count || !int.TryParse(options[index + 1], out var value)) throw new ArgumentException($"{name} requires an integer value.");
        return value;
    }
    return fallback;
}
