using GtaRpAssistant.ProductBenchmark;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    try
    {
        var command = arguments.FirstOrDefault()?.ToLowerInvariant();
        return command switch
        {
            "validate" => Validate(arguments),
            "evaluate" => await EvaluateAsync(arguments),
            _ => Usage(),
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int Validate(IReadOnlyList<string> arguments)
{
    var path = arguments.Count > 1 ? arguments[1] : "ml/evaluation/product-pipeline-eval.json";
    var errors = ProductBenchmarkValidation.Validate(ProductBenchmarkJson.LoadDataset(path));
    foreach (var error in errors) Console.Error.WriteLine(error);
    if (errors.Count > 0) return 1;
    Console.WriteLine("Product pipeline evaluation dataset is valid.");
    return 0;
}

static async Task<int> EvaluateAsync(IReadOnlyList<string> arguments)
{
    if (arguments.Count < 5) return Usage();
    var dataset = ProductBenchmarkJson.LoadDataset(arguments[1]);
    var report = await new ProductBenchmarkRunner().RunAsync(dataset, arguments[2], arguments[3], CancellationToken.None);
    await ProductBenchmarkRunner.WriteReportsAsync(report, arguments[4], CancellationToken.None);
    var metrics = report.Metrics;
    Console.WriteLine($"Product pipeline benchmark: {(report.PassedReleaseGate ? "PASS" : "FAIL")}. Cases: {metrics.TotalCases}; blocking pass: {metrics.BlockingPassRate:P2}; p95: {metrics.P95LatencyMs:F2} ms.");
    foreach (var failure in report.GateFailures) Console.Error.WriteLine(failure);
    return report.PassedReleaseGate ? 0 : 2;
}

static int Usage()
{
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  validate [dataset.json]");
    Console.Error.WriteLine("  evaluate <dataset.json> <pack-directory> <community-directory> <output-directory>");
    return 64;
}
