using System.Text.Json;
using GtaRpAssistant.Providers;

var options = Arguments.Parse(args);
using var client = new HttpClient();
var provider = new OpenAiCompatibleChatProvider(client, new(
    options.Endpoint,
    options.Model,
    Timeout: options.Timeout,
    IsLocal: options.Endpoint.IsLoopback,
    ProviderId: "local-ai-check",
    MaxOutputTokens: options.MaxOutputTokens));

using var cancellation = new CancellationTokenSource(options.OverallTimeout);
try
{
    var report = await new LocalAiCapabilityTester().TestAsync(provider, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return report.IsCompatible ? 0 : 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"Проверка не завершилась за {options.OverallTimeout.TotalSeconds:0} с.");
    return 3;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
    return 1;
}

internal sealed record Arguments(Uri Endpoint, string Model, TimeSpan Timeout, TimeSpan OverallTimeout, int MaxOutputTokens)
{
    public static Arguments Parse(string[] args)
    {
        var values = args
            .Chunk(2)
            .Where(pair => pair.Length == 2 && pair[0].StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
        var endpoint = new Uri(Get(values, "--endpoint", "http://127.0.0.1:1234/v1"), UriKind.Absolute);
        var model = Get(values, "--model", "qwen/qwen3-vl-4b");
        var timeoutSeconds = ParsePositiveInt(Get(values, "--timeout-seconds", "45"), "--timeout-seconds");
        var overallSeconds = ParsePositiveInt(Get(values, "--overall-timeout-seconds", "180"), "--overall-timeout-seconds");
        var maxOutputTokens = ParsePositiveInt(Get(values, "--max-output-tokens", "420"), "--max-output-tokens");
        return new(endpoint, model, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(overallSeconds), maxOutputTokens);
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string name, string fallback) =>
        values.TryGetValue(name, out var value) ? value : fallback;

    private static int ParsePositiveInt(string value, string name) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{name} должен быть положительным целым числом.");
}
