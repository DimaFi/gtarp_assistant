using System.Diagnostics;
using System.Security.Cryptography;

namespace GtaRpAssistant.ModelBenchmark;

public sealed class LlamaCliBenchmarkRunner
{
    public async Task<BenchmarkReport> RunAsync(
        string runtimePath,
        string modelPath,
        ModelCandidate candidate,
        EvaluationDataset dataset,
        BenchmarkThresholds thresholds,
        string responseSchemaPath,
        BenchmarkExecutionOptions execution,
        CancellationToken cancellationToken)
    {
        runtimePath = ResolveCompletionRuntime(runtimePath);
        modelPath = Path.GetFullPath(modelPath);
        responseSchemaPath = Path.GetFullPath(responseSchemaPath);
        execution.Validate();
        if (!File.Exists(runtimePath)) throw new FileNotFoundException("llama.cpp completion runtime was not found.", runtimePath);
        if (!File.Exists(modelPath)) throw new FileNotFoundException("GGUF model was not found.", modelPath);
        if (!string.Equals(Path.GetExtension(modelPath), ".gguf", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Model path must point to a GGUF file.");
        var schemaErrors = BenchmarkValidation.ValidateResponseSchema(responseSchemaPath);
        if (schemaErrors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, schemaErrors));
        var caseResults = new List<BenchmarkCaseResult>(dataset.Cases.Count);
        foreach (var item in dataset.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var measurement = await RunCaseAsync(runtimePath, modelPath, PromptBuilder.Build(item, candidate.DisableThinking), responseSchemaPath, candidate.DisableThinking, execution, cancellationToken);
            caseResults.Add(BenchmarkValidation.Score(
                item,
                measurement.Output,
                measurement.ExitCode,
                measurement.Elapsed.TotalMilliseconds,
                measurement.TimeToFirstOutput.TotalMilliseconds,
                measurement.PeakWorkingSetBytes,
                measurement.PeakPrivateBytes,
                measurement.PeakCommittedBytes,
                measurement.PeakCpuPercent,
                measurement.TimedOut,
                measurement.Diagnostic,
                thresholds));
        }

        var metrics = BenchmarkValidation.Summarize(caseResults);
        var failures = BenchmarkValidation.EvaluateGate(candidate, metrics, thresholds);
        return new()
        {
            CandidateId = candidate.Id,
            ModelFileName = Path.GetFileName(modelPath),
            ModelFileBytes = new FileInfo(modelPath).Length,
            ModelSha256 = await ComputeSha256Async(modelPath, cancellationToken),
            RuntimeFileName = Path.GetFileName(runtimePath),
            Execution = execution,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Machine = $"{Environment.MachineName}; {Environment.OSVersion}; CPU={Environment.ProcessorCount}",
            LicenseReview = candidate.LicenseReview,
            DistributionPolicy = candidate.DistributionPolicy,
            Metrics = metrics,
            Cases = caseResults,
            PassedReleaseGate = failures.Count == 0,
            GateFailures = failures,
        };
    }

    private static async Task<RuntimeMeasurement> RunCaseAsync(
        string runtimePath,
        string modelPath,
        string prompt,
        string responseSchemaPath,
        bool disableThinking,
        BenchmarkExecutionOptions execution,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(runtimePath)
        {
            WorkingDirectory = Path.GetDirectoryName(runtimePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-m", modelPath,
            "-c", execution.ContextTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-t", execution.CpuThreads.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-n", execution.MaxOutputTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ngl", execution.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-s", "1",
            "--temp", "0.1",
            "--top-p", "0.8",
            "--offline",
            "--single-turn",
            "--no-display-prompt",
            "-lv", "2",
            "-jf", responseSchemaPath,
            "-p", prompt,
        }) startInfo.ArgumentList.Add(argument);
        if (disableThinking)
        {
            startInfo.ArgumentList.Insert(startInfo.ArgumentList.Count - 2, "--reasoning");
            startInfo.ArgumentList.Insert(startInfo.ArgumentList.Count - 2, "off");
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException("llama.cpp CLI did not start.");
        var outputTask = ReadOutputAsync(process.StandardOutput, stopwatch, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        long peakWorkingSet = 0;
        long peakPrivate = 0;
        long peakCommitted = 0;
        double peakCpuPercent = 0;
        var timedOut = false;
        var previousCpu = TimeSpan.Zero;
        var previousSample = stopwatch.Elapsed;
        try
        {
            while (!process.HasExited)
            {
                TrySampleProcess(process, stopwatch.Elapsed, ref previousSample, ref previousCpu, ref peakWorkingSet, ref peakPrivate, ref peakCommitted, ref peakCpuPercent);
                if (stopwatch.Elapsed >= execution.CaseTimeout)
                {
                    timedOut = true;
                    TryKill(process);
                    break;
                }
                await Task.Delay(25, cancellationToken);
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        stopwatch.Stop();
        var (output, firstOutput) = await outputTask;
        var error = await errorTask;
        return new(process.ExitCode, NormalizeGeneratedOutput(output), error, stopwatch.Elapsed, firstOutput, peakWorkingSet, peakPrivate, peakCommitted, peakCpuPercent, timedOut);
    }

    private static void TrySampleProcess(
        Process process,
        TimeSpan elapsed,
        ref TimeSpan previousSample,
        ref TimeSpan previousCpu,
        ref long peakWorkingSet,
        ref long peakPrivate,
        ref long peakCommitted,
        ref double peakCpuPercent)
    {
        try
        {
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            // On Windows, PrivateMemorySize64 is the process-private committed charge available via System.Diagnostics.
            peakCommitted = Math.Max(peakCommitted, process.PrivateMemorySize64);
            var cpu = process.TotalProcessorTime;
            var sampleDuration = elapsed - previousSample;
            if (sampleDuration > TimeSpan.Zero)
            {
                var cpuPercent = (cpu - previousCpu).TotalMilliseconds / sampleDuration.TotalMilliseconds / Environment.ProcessorCount * 100d;
                peakCpuPercent = Math.Max(peakCpuPercent, Math.Max(0, cpuPercent));
            }
            previousCpu = cpu;
            previousSample = elapsed;
        }
        catch (InvalidOperationException)
        {
            // A very short CLI run can exit between HasExited and metric access.
        }
    }

    private static string ResolveCompletionRuntime(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(fullPath), "llama-cli.exe", StringComparison.OrdinalIgnoreCase)) return fullPath;
        var completion = Path.Combine(Path.GetDirectoryName(fullPath)!, "llama-completion.exe");
        return File.Exists(completion) ? completion : fullPath;
    }

    private static string NormalizeGeneratedOutput(string output)
    {
        var normalized = output.Trim();
        const string endMarker = "[end of text]";
        if (normalized.EndsWith(endMarker, StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^endMarker.Length].TrimEnd();
        return normalized;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) { }
    }

    private static async Task<(string Output, TimeSpan FirstOutput)> ReadOutputAsync(StreamReader reader, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        var output = new System.Text.StringBuilder();
        var firstOutput = TimeSpan.Zero;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length == 0) firstOutput = stopwatch.Elapsed;
            output.Append(buffer, 0, read);
        }
        return (output.ToString(), firstOutput);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record RuntimeMeasurement(
        int ExitCode,
        string Output,
        string Diagnostic,
        TimeSpan Elapsed,
        TimeSpan TimeToFirstOutput,
        long PeakWorkingSetBytes,
        long PeakPrivateBytes,
        long PeakCommittedBytes,
        double PeakCpuPercent,
        bool TimedOut);
}
