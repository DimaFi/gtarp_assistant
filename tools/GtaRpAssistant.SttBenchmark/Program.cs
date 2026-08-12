using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;

if (args.Length > 0 && string.Equals(args[0], "record", StringComparison.OrdinalIgnoreCase))
    return await SttDatasetRecorder.RunAsync(args[1..]);
if (args.Length == 1 && string.Equals(args[0], "devices", StringComparison.OrdinalIgnoreCase))
    return SttDatasetRecorder.ListDevices();
if (args.Length > 0 && string.Equals(args[0], "lifecycle", StringComparison.OrdinalIgnoreCase))
    return await SttLifecycleBenchmark.RunAsync(args[1..]);
if (args.Length > 0 && string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
    return await SttComparison.RunAsync(args[1..]);
if (args.Length > 0 && string.Equals(args[0], "finalize", StringComparison.OrdinalIgnoreCase))
    return await SttProductionGate.RunAsync(args[1..]);
if (args.Length > 0 && string.Equals(args[0], "vosk-evaluate", StringComparison.OrdinalIgnoreCase))
    return await VoskSttBenchmark.RunAsync(args[1..]);

if (args.Length != 4 || !string.Equals(args[0], "evaluate", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark evaluate <pack-directory> <dataset.json> <report.json>");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark record <dataset.json> [device-id] [--overwrite]");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark devices");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark lifecycle <pack-directory> <audio.wav> <iterations> <report.json> [hardware-profile]");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark compare <first-report.json> <second-report.json> <comparison.json>");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark finalize <comparison.json> <pack-directory> <output.zip> <attestation.json> <lifecycle.json> [more lifecycle reports...]");
    Console.Error.WriteLine("  GtaRpAssistant.SttBenchmark vosk-evaluate <model-directory> <dataset.json> <report.json>");
    return 1;
}

var packDirectory = Path.GetFullPath(args[1]);
var datasetPath = Path.GetFullPath(args[2]);
var reportPath = Path.GetFullPath(args[3]);
var dataset = JsonSerializer.Deserialize<SttDataset>(await File.ReadAllTextAsync(datasetPath), JsonOptions())
    ?? throw new InvalidDataException("STT dataset is empty.");
SttDatasetValidation.Validate(dataset);

var packLocator = new EmbeddedSttPackLocator(() => packDirectory, packDirectory);
var inspection = await packLocator.InspectAsync(CancellationToken.None);
if (!inspection.IsValid) throw new InvalidDataException(inspection.Message);

var cases = new List<SttCaseReport>();
var memoryPeak = new MemoryPeak();
var datasetDirectory = Path.GetDirectoryName(datasetPath)!;
await using (var provider = new WhisperCppSpeechToTextProvider(packLocator))
{
    foreach (var item in dataset.Cases)
    {
        var audioPath = SafeDatasetPath(datasetDirectory, item.AudioFile);
        var stopwatch = Stopwatch.StartNew();
        using var monitorCancellation = new CancellationTokenSource();
        var monitor = ObserveMemoryAsync(provider, memoryPeak, monitorCancellation.Token);
        try
        {
            var result = await provider.TranscribeAsync(ReadWave(audioPath), CancellationToken.None);
            stopwatch.Stop();
            var metrics = provider.GetRuntimeMetrics();
            memoryPeak.Observe(metrics);
            var wordErrorRate = SttTextMetrics.WordErrorRate(item.Reference, result.Text);
            var termRecall = SttTextMetrics.TermRecall(item.RequiredTerms, result.Text);
            cases.Add(new(item.Id, item.AudioFile, item.Reference, result.Text, wordErrorRate, termRecall,
                stopwatch.Elapsed.TotalMilliseconds, metrics.WorkingSetBytes, metrics.PrivateBytes, null, item.RequiredTerms));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            cases.Add(new(item.Id, item.AudioFile, item.Reference, "", 1, 0, stopwatch.Elapsed.TotalMilliseconds, 0, 0,
                $"{exception.GetType().Name}: {exception.Message}", item.RequiredTerms));
        }
        finally
        {
            monitorCancellation.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
        }
    }
}

var averageWer = cases.Average(item => item.WordErrorRate);
var averageTermRecall = cases.Average(item => item.TermRecall);
var p95Latency = Percentile(cases.Select(item => item.LatencyMs), .95);
var failures = cases.Count(item => item.Error is not null || string.IsNullOrWhiteSpace(item.Transcript));
var passed = failures == 0
    && averageWer <= dataset.Gate.MaximumAverageWordErrorRate
    && averageTermRecall >= dataset.Gate.MinimumTermRecall
    && p95Latency <= dataset.Gate.MaximumP95LatencyMs
    && Math.Max(memoryPeak.WorkingSetBytes, memoryPeak.PrivateBytes) <= dataset.Gate.MaximumMemoryBytes;
var report = new SttBenchmarkReport(
    DateTimeOffset.UtcNow,
    inspection.Manifest!.Id,
    inspection.Manifest.ModelId,
    dataset.Id,
    passed,
    averageWer,
    averageTermRecall,
    p95Latency,
    failures,
    memoryPeak.WorkingSetBytes,
    memoryPeak.PrivateBytes,
    cases,
    await ComputeSha256Async(datasetPath),
    dataset.Cases.Count,
    dataset.Gate);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions(writeIndented: true)));
Console.WriteLine($"STT gate: {(passed ? "PASS" : "FAIL")}; WER={averageWer:P1}; terms={averageTermRecall:P1}; p95={p95Latency:F0} ms; failures={failures}; private={memoryPeak.PrivateBytes / 1024d / 1024d:F0} MiB");
return passed ? 0 : 2;

static AudioSegment ReadWave(string path)
{
    var bytes = File.ReadAllBytes(path);
    if (bytes.Length < 44 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        throw new InvalidDataException($"Not a RIFF/WAVE file: {path}");
    var channels = BitConverter.ToInt16(bytes, 22);
    var sampleRate = BitConverter.ToInt32(bytes, 24);
    var bits = BitConverter.ToInt16(bytes, 34);
    if (channels != 1 || sampleRate != 16_000 || bits != 16) throw new InvalidDataException("STT benchmark expects PCM16 mono 16 kHz WAV.");
    var data = FindChunk(bytes, "data"u8);
    var length = BitConverter.ToInt32(bytes, data + 4);
    if (length <= 0 || data + 8 + length > bytes.Length) throw new InvalidDataException("Invalid WAV data chunk.");
    var endedAt = DateTimeOffset.UtcNow;
    var duration = TimeSpan.FromSeconds(length / 2d / 16_000d);
    return new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, endedAt - duration, endedAt, 16_000, 1, bytes.AsMemory(data + 8, length));
}

static int FindChunk(byte[] bytes, ReadOnlySpan<byte> name)
{
    for (var offset = 12; offset <= bytes.Length - 8;)
    {
        if (bytes.AsSpan(offset, 4).SequenceEqual(name)) return offset;
        var length = BitConverter.ToInt32(bytes, offset + 4);
        if (length < 0) break;
        offset += 8 + length + (length & 1);
    }
    throw new InvalidDataException("WAV data chunk is missing.");
}

static string SafeDatasetPath(string directory, string relativePath)
{
    if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) throw new InvalidDataException("Dataset audio paths must be relative.");
    var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var path = Path.GetFullPath(Path.Combine(root, relativePath));
    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Dataset audio path escapes the dataset directory.");
    return path;
}

static double Percentile(IEnumerable<double> source, double percentile)
{
    var values = source.Order().ToArray();
    if (values.Length == 0) return 0;
    return values[(int)Math.Ceiling(percentile * values.Length) - 1];
}

static async Task ObserveMemoryAsync(WhisperCppSpeechToTextProvider provider, MemoryPeak peak, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        peak.Observe(provider.GetRuntimeMetrics());
        await Task.Delay(50, cancellationToken);
    }
}

static JsonSerializerOptions JsonOptions(bool writeIndented = false) => new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = writeIndented,
};

static async Task<string> ComputeSha256Async(string path)
{
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
}

public sealed record SttDataset(string Id, SttGate Gate, IReadOnlyList<SttCase> Cases);
public sealed record SttGate(
    int MinimumCases = 12,
    double MaximumAverageWordErrorRate = .25,
    double MinimumTermRecall = .85,
    double MaximumP95LatencyMs = 5_000,
    long MaximumMemoryBytes = 1_100L * 1024 * 1024);
public sealed record SttCase(string Id, string AudioFile, string Reference, IReadOnlyList<string> RequiredTerms);
public sealed record SttCaseReport(
    string Id, string AudioFile, string Reference, string Transcript, double WordErrorRate, double TermRecall,
    double LatencyMs, long WorkingSetBytes, long PrivateBytes, string? Error, IReadOnlyList<string> RequiredTerms);
public sealed record SttBenchmarkReport(
    DateTimeOffset CreatedAt, string PackId, string ModelId, string DatasetId, bool Passed,
    double AverageWordErrorRate, double TermRecall, double P95LatencyMs, int Failures,
    long PeakWorkingSetBytes, long PeakPrivateBytes, IReadOnlyList<SttCaseReport> Cases,
    string DatasetSha256, int DatasetCaseCount, SttGate Gate, int SchemaVersion = 2);

public sealed class MemoryPeak
{
    private long _workingSetBytes;
    private long _privateBytes;
    public long WorkingSetBytes => Interlocked.Read(ref _workingSetBytes);
    public long PrivateBytes => Interlocked.Read(ref _privateBytes);
    public void Observe(EmbeddedSttRuntimeMetrics metrics)
    {
        UpdateMaximum(ref _workingSetBytes, metrics.WorkingSetBytes);
        UpdateMaximum(ref _privateBytes, metrics.PrivateBytes);
    }
    private static void UpdateMaximum(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current) return;
            current = observed;
        }
    }
}

public static partial class SttTextMetrics
{
    [GeneratedRegex("[^\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();

    public static double WordErrorRate(string expected, string actual)
    {
        var left = Words(expected);
        var right = Words(actual);
        if (left.Length == 0) return right.Length == 0 ? 0 : 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1] / (double)left.Length;
    }

    public static double TermRecall(IReadOnlyList<string> terms, string actual)
    {
        if (terms.Count == 0) return 1;
        var normalized = $" {string.Join(' ', Words(actual))} ";
        return terms.Count(term => normalized.Contains($" {string.Join(' ', Words(term))} ", StringComparison.Ordinal)) / (double)terms.Count;
    }

    private static string[] Words(string value) => Separators()
        .Split(GtaRpSttLexicon.NormalizeTranscript(value).ToLowerInvariant().Replace("репутаций", "репутации", StringComparison.Ordinal))
        .Where(word => word.Length > 0)
        .ToArray();
}
