using System.IO.Pipes;
using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.MicroModelHost;

var options = HostOptions.Parse(args);
var runtime = new MockMicroModelRuntime();
using var lifetime = new CancellationTokenSource();
await using var pipe = new NamedPipeServerStream(
    options.PipeName,
    PipeDirection.InOut,
    1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous);
await pipe.WaitForConnectionAsync(lifetime.Token);
using var reader = new StreamReader(pipe, leaveOpen: true);
using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

while (pipe.IsConnected)
{
    string? line;
    try
    {
        line = await reader.ReadLineAsync(lifetime.Token).AsTask().WaitAsync(options.IdleTtl, lifetime.Token);
    }
    catch (TimeoutException)
    {
        break;
    }
    if (line is null) break;

    MicroModelPipeResponse response;
    try
    {
        var request = JsonSerializer.Deserialize<MicroModelPipeRequest>(line) ?? throw new InvalidDataException("Empty protocol request.");
        if (string.Equals(request.Command, "shutdown", StringComparison.Ordinal))
        {
            response = new(request.RequestId, true, MicroModelState.Stopping);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            break;
        }
        if (!string.Equals(request.Command, "generate", StringComparison.Ordinal) || request.Request is null)
            throw new InvalidDataException("Unsupported protocol command.");
        var generated = await runtime.GenerateAsync(request.Request, lifetime.Token);
        response = new(request.RequestId, true, MicroModelState.Idle, generated);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        var requestId = TryGetRequestId(line);
        response = new(requestId, false, MicroModelState.Faulted, Error: ex.GetType().Name);
    }
    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
}

static string TryGetRequestId(string json)
{
    try
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("RequestId", out var id) ? id.GetString() ?? "unknown" : "unknown";
    }
    catch (JsonException) { return "unknown"; }
}

internal sealed record HostOptions(string PipeName, TimeSpan IdleTtl)
{
    public static HostOptions Parse(string[] args)
    {
        string? pipe = null;
        var idleTtlMs = 25_000;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--pipe" && index + 1 < args.Length) pipe = args[++index];
            else if (args[index] == "--idle-ttl-ms" && index + 1 < args.Length && int.TryParse(args[++index], out var parsed)) idleTtlMs = parsed;
        }
        if (string.IsNullOrWhiteSpace(pipe)) throw new ArgumentException("--pipe is required.");
        if (idleTtlMs is < 100 or > 300_000) throw new ArgumentOutOfRangeException(nameof(idleTtlMs));
        return new(pipe, TimeSpan.FromMilliseconds(idleTtlMs));
    }
}
