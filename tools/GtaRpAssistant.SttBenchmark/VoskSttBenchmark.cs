using System.Diagnostics;
using System.Text.Json;
using GtaRpAssistant.Infrastructure.Windows;
using Vosk;

public static class VoskSttBenchmark
{
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
            Console.Error.WriteLine("Usage: GtaRpAssistant.SttBenchmark vosk-evaluate <model-directory> <dataset.json> <report.json>");
            return 1;
        }
        var modelDirectory = Path.GetFullPath(args[0]);
        var datasetPath = Path.GetFullPath(args[1]);
        var reportPath = Path.GetFullPath(args[2]);
        if (!Directory.Exists(modelDirectory)) throw new DirectoryNotFoundException(modelDirectory);
        var dataset = JsonSerializer.Deserialize<SttDataset>(await File.ReadAllTextAsync(datasetPath), Json)
            ?? throw new InvalidDataException("STT dataset is empty.");
        SttDatasetValidation.Validate(dataset);

        Vosk.Vosk.SetLogLevel(-1);
        using var process = Process.GetCurrentProcess();
        using var model = new Model(modelDirectory);
        var cases = new List<SttCaseReport>();
        long peakWorkingSet = 0;
        long peakPrivate = 0;
        var root = Path.GetDirectoryName(datasetPath)!;
        foreach (var item in dataset.Cases)
        {
            var stopwatch = Stopwatch.StartNew();
            string transcript = "";
            string? error = null;
            try
            {
                var pcm = ReadPcm(Path.Combine(root, item.AudioFile));
                using var recognizer = new VoskRecognizer(model, 16_000f);
                recognizer.SetWords(false);
                recognizer.AcceptWaveform(pcm, pcm.Length);
                using var result = JsonDocument.Parse(recognizer.FinalResult());
                transcript = result.RootElement.GetProperty("text").GetString()?.Trim() ?? "";
                transcript = GtaRpSttLexicon.NormalizeTranscript(transcript);
            }
            catch (Exception exception) { error = $"{exception.GetType().Name}: {exception.Message}"; }
            stopwatch.Stop();
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            cases.Add(new(item.Id, item.AudioFile, item.Reference, transcript,
                error is null ? SttTextMetrics.WordErrorRate(item.Reference, transcript) : 1,
                error is null ? SttTextMetrics.TermRecall(item.RequiredTerms, transcript) : 0,
                stopwatch.Elapsed.TotalMilliseconds, process.WorkingSet64, process.PrivateMemorySize64, error, item.RequiredTerms));
        }

        var averageWer = cases.Average(item => item.WordErrorRate);
        var averageRecall = cases.Average(item => item.TermRecall);
        var p95 = Percentile(cases.Select(item => item.LatencyMs), .95);
        var failures = cases.Count(item => item.Error is not null || string.IsNullOrWhiteSpace(item.Transcript));
        var passed = failures == 0 && averageWer <= dataset.Gate.MaximumAverageWordErrorRate
            && averageRecall >= dataset.Gate.MinimumTermRecall && p95 <= dataset.Gate.MaximumP95LatencyMs
            && Math.Max(peakWorkingSet, peakPrivate) <= dataset.Gate.MaximumMemoryBytes;
        var report = new VoskBenchmarkReport(DateTimeOffset.UtcNow, "vosk-model-small-ru-0.22", dataset.Id,
            passed, averageWer, averageRecall, p95, failures, peakWorkingSet, peakPrivate, cases);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, Json));
        Console.WriteLine($"Vosk STT gate: {(passed ? "PASS" : "FAIL")}; WER={averageWer:P1}; terms={averageRecall:P1}; p95={p95:F0} ms; failures={failures}; private={peakPrivate / 1024d / 1024d:F0} MiB");
        return passed ? 0 : 2;
    }

    private static byte[] ReadPcm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || BitConverter.ToInt16(bytes, 22) != 1 || BitConverter.ToInt32(bytes, 24) != 16_000
            || BitConverter.ToInt16(bytes, 34) != 16)
            throw new InvalidDataException("Vosk benchmark expects PCM16 mono 16 kHz WAV.");
        var offset = FindChunk(bytes);
        var length = BitConverter.ToInt32(bytes, offset + 4);
        if (length <= 0 || offset + 8 + length > bytes.Length) throw new InvalidDataException("Invalid WAV data chunk.");
        return bytes.AsSpan(offset + 8, length).ToArray();
    }

    private static int FindChunk(byte[] bytes)
    {
        for (var offset = 12; offset <= bytes.Length - 8;)
        {
            if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8)) return offset;
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

public sealed record VoskBenchmarkReport(DateTimeOffset CreatedAt, string ModelId, string DatasetId, bool Passed,
    double AverageWordErrorRate, double TermRecall, double P95LatencyMs, int Failures,
    long PeakWorkingSetBytes, long PeakPrivateBytes, IReadOnlyList<SttCaseReport> Cases, int SchemaVersion = 1);
