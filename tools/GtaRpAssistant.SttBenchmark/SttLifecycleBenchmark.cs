using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using GtaRpAssistant.Infrastructure.Windows;

public static class SttLifecycleBenchmark
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 4 or > 5 || !int.TryParse(args[2], out var iterations) || iterations is < 1 or > 100)
        {
            Console.Error.WriteLine("Usage: GtaRpAssistant.SttBenchmark lifecycle <pack-directory> <audio.wav> <iterations:1-100> <report.json> [hardware-profile]");
            return 1;
        }
        var packDirectory = Path.GetFullPath(args[0]);
        var wavePath = Path.GetFullPath(args[1]);
        var reportPath = Path.GetFullPath(args[3]);
        var hardwareProfile = args.Length == 5 ? args[4].Trim() : "unspecified";
        if (string.IsNullOrWhiteSpace(hardwareProfile)) throw new InvalidDataException("Lifecycle hardware profile is required.");
        var segment = ReadWave(wavePath);
        var results = new List<SttLifecycleIteration>();
        var locator = new EmbeddedSttPackLocator(() => packDirectory, packDirectory);
        var inspection = await locator.InspectAsync(CancellationToken.None);
        if (!inspection.IsValid) throw new InvalidDataException(inspection.Message);
        var manifestPath = Path.Combine(packDirectory, "stt-pack.json");

        for (var index = 1; index <= iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            int? processId = null;
            long workingSet = 0;
            long privateBytes = 0;
            string? error = null;
            var orphaned = false;
            try
            {
                await using (var provider = new WhisperCppSpeechToTextProvider(locator))
                {
                    _ = await provider.TranscribeAsync(segment with { Id = Guid.NewGuid() }, CancellationToken.None);
                    var metrics = provider.GetRuntimeMetrics();
                    processId = metrics.ProcessId;
                    workingSet = metrics.WorkingSetBytes;
                    privateBytes = metrics.PrivateBytes;
                }
                orphaned = processId is not null && await IsStillRunningAsync(processId.Value);
                if (orphaned) error = $"Runtime process {processId} survived provider disposal.";
            }
            catch (Exception exception) { error = $"{exception.GetType().Name}: {exception.Message}"; }
            stopwatch.Stop();
            results.Add(new(index, processId, stopwatch.Elapsed.TotalMilliseconds, workingSet, privateBytes, orphaned, error));
            Console.WriteLine($"Lifecycle {index}/{iterations}: {(error is null ? "PASS" : "FAIL")}, {stopwatch.Elapsed.TotalMilliseconds:F0} ms, private {privateBytes / 1024d / 1024d:F0} MiB");
        }

        var report = new SttLifecycleReport(DateTimeOffset.UtcNow, inspection.Manifest!.Id, inspection.Manifest.ModelId,
            await ComputeSha256Async(manifestPath), await ComputeSha256Async(wavePath), hardwareProfile, CaptureHardware(),
            results.All(item => item.Error is null),
            results.Count(item => item.Error is not null), Percentile(results.Select(item => item.ElapsedMs), .95),
            results.Max(item => item.WorkingSetBytes), results.Max(item => item.PrivateBytes), results);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        return report.Passed ? 0 : 2;
    }

    private static SttHardwareSnapshot CaptureHardware() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static async Task<bool> IsStillRunningAsync(int processId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) return false;
            }
            catch (ArgumentException) { return false; }
            await Task.Delay(100);
        }
        return true;
    }

    private static GtaRpAssistant.Core.AudioSegment ReadWave(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException($"Not a RIFF/WAVE file: {path}");
        if (BitConverter.ToInt16(bytes, 22) != 1 || BitConverter.ToInt32(bytes, 24) != 16_000 || BitConverter.ToInt16(bytes, 34) != 16)
            throw new InvalidDataException("Lifecycle benchmark expects PCM16 mono 16 kHz WAV.");
        var data = FindChunk(bytes, "data"u8);
        var length = BitConverter.ToInt32(bytes, data + 4);
        if (length <= 0 || data + 8 + length > bytes.Length) throw new InvalidDataException("Invalid WAV data chunk.");
        var endedAt = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromSeconds(length / 2d / 16_000d);
        return new(Guid.NewGuid(), GtaRpAssistant.Core.AudioSourceKind.UserMicrophone, endedAt - duration, endedAt, 16_000, 1, bytes.AsMemory(data + 8, length));
    }

    private static int FindChunk(byte[] bytes, ReadOnlySpan<byte> name)
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

    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.Order().ToArray();
        return values[(int)Math.Ceiling(percentile * values.Length) - 1];
    }
}

public sealed record SttLifecycleIteration(int Iteration, int? ProcessId, double ElapsedMs, long WorkingSetBytes,
    long PrivateBytes, bool OrphanedProcess, string? Error);
public sealed record SttHardwareSnapshot(string OperatingSystem, string Architecture, string Processor,
    int LogicalProcessorCount, long AvailableMemoryBytes);
public sealed record SttLifecycleReport(DateTimeOffset CreatedAt, string PackId, string ModelId,
    string PackManifestSha256, string AudioSha256, string HardwareProfile, SttHardwareSnapshot Hardware, bool Passed,
    int Failures, double P95ElapsedMs, long PeakWorkingSetBytes, long PeakPrivateBytes,
    IReadOnlyList<SttLifecycleIteration> Iterations, int SchemaVersion = 2);
