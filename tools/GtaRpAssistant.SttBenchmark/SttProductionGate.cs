using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GtaRpAssistant.Infrastructure.Windows;

public static class SttProductionGate
{
    private const int RequiredLifecycleIterations = 100;
    private const double MaximumColdLifecycleP95Ms = 15_000;
    private static readonly string[] RequiredProfiles = ["reference", "weak-pc"];
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Usage: GtaRpAssistant.SttBenchmark finalize <comparison.json> <pack-directory> <output.zip> <attestation.json> <lifecycle.json> [more lifecycle reports...]");
            return 1;
        }

        var comparisonPath = Path.GetFullPath(args[0]);
        var packDirectory = Path.GetFullPath(args[1]);
        var archivePath = Path.GetFullPath(args[2]);
        var attestationPath = Path.GetFullPath(args[3]);
        var lifecyclePaths = args[4..].Select(Path.GetFullPath).ToArray();
        var comparison = await ReadAsync<SttComparisonReport>(comparisonPath);
        var candidates = comparison.Candidates ?? [];
        var errors = new List<string>();

        var qualityReports = new List<SttBenchmarkReport>();
        foreach (var candidate in candidates)
        {
            try
            {
                var report = await ReadAsync<SttBenchmarkReport>(candidate.ReportPath);
                SttComparison.ValidateReport(report);
                qualityReports.Add(report);
            }
            catch (Exception exception) { errors.Add($"Quality evidence is invalid for '{candidate.PackId}': {exception.Message}"); }
        }
        if (qualityReports.Count == 2)
        {
            try
            {
                var recomputed = SttComparison.Compare(qualityReports[0], qualityReports[1]);
                if (comparison.SchemaVersion != recomputed.SchemaVersion
                    || comparison.DatasetId != recomputed.DatasetId
                    || !string.Equals(comparison.DatasetSha256, recomputed.DatasetSha256, StringComparison.OrdinalIgnoreCase)
                    || comparison.DatasetCaseCount != recomputed.DatasetCaseCount
                    || comparison.Decision != recomputed.Decision
                    || comparison.RecommendedPackId != recomputed.RecommendedPackId
                    || comparison.RecommendedModelId != recomputed.RecommendedModelId)
                    errors.Add("Comparison report does not match its quality evidence.");
            }
            catch (Exception exception) { errors.Add($"Comparison evidence is invalid: {exception.Message}"); }
        }
        else errors.Add("Exactly two valid candidate quality reports are required.");

        var locator = new EmbeddedSttPackLocator(() => packDirectory, packDirectory);
        var inspection = await locator.InspectAsync(CancellationToken.None);
        if (!inspection.IsValid) errors.Add($"Selected pack is invalid: {inspection.Message}");
        var manifestHash = inspection.IsValid
            ? await ComputeSha256Async(Path.Combine(packDirectory, "stt-pack.json"))
            : "";
        if (inspection.IsValid && !string.Equals(inspection.Manifest!.Id, comparison.RecommendedPackId, StringComparison.Ordinal))
            errors.Add("Selected pack does not match the comparison recommendation.");

        var selectedQuality = qualityReports.FirstOrDefault(report => report.PackId == comparison.RecommendedPackId);
        if (comparison.RecommendedPackId is null) errors.Add("Quality gate did not recommend a candidate.");
        if (selectedQuality is null) errors.Add("Recommended candidate quality report is missing.");

        var lifecycleReports = new List<SttLifecycleReport>();
        foreach (var path in lifecyclePaths)
        {
            try { lifecycleReports.Add(await ReadAsync<SttLifecycleReport>(path)); }
            catch (Exception exception) { errors.Add($"Lifecycle evidence is invalid: {exception.Message}"); }
        }
        if (selectedQuality is not null)
            errors.AddRange(ValidateLifecycleEvidence(selectedQuality, manifestHash, lifecycleReports));

        var evidence = new List<SttEvidenceHash>();
        foreach (var path in new[] { comparisonPath }.Concat(qualityReports.Count == 2
                     ? candidates.Select(candidate => Path.GetFullPath(candidate.ReportPath))
                     : []).Concat(lifecyclePaths).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path)) evidence.Add(new(Path.GetFileName(path), await ComputeSha256Async(path)));
            else errors.Add($"Evidence file is missing: {path}");
        }

        string? archiveHash = null;
        if (errors.Count == 0)
        {
            CreateDeterministicArchive(packDirectory, archivePath);
            archiveHash = await ComputeSha256Async(archivePath);
        }

        var attestation = new SttProductionAttestation(DateTimeOffset.UtcNow, errors.Count == 0,
            comparison.RecommendedPackId, comparison.RecommendedModelId, manifestHash, archiveHash,
            comparison.DatasetId, comparison.DatasetSha256, lifecycleReports.Select(report => report.HardwareProfile).ToArray(),
            evidence, errors);
        Directory.CreateDirectory(Path.GetDirectoryName(attestationPath)!);
        await File.WriteAllTextAsync(attestationPath, JsonSerializer.Serialize(attestation, Json), new UTF8Encoding(false));
        Console.WriteLine(errors.Count == 0
            ? $"STT production gate: PASS; final voice pack: {archivePath}"
            : $"STT production gate: FAIL; {string.Join(" | ", errors)}");
        return errors.Count == 0 ? 0 : 2;
    }

    public static IReadOnlyList<string> ValidateLifecycleEvidence(SttBenchmarkReport selectedQuality,
        string manifestSha256, IReadOnlyList<SttLifecycleReport> reports)
    {
        var errors = new List<string>();
        foreach (var required in RequiredProfiles)
            if (!reports.Any(report => string.Equals(report.HardwareProfile, required, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Missing required hardware profile '{required}'.");
        foreach (var duplicate in reports.GroupBy(report => report.HardwareProfile, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            errors.Add($"Duplicate hardware profile '{duplicate.Key}'.");
        var requiredReports = RequiredProfiles
            .Select(profile => reports.FirstOrDefault(report => string.Equals(report.HardwareProfile, profile, StringComparison.OrdinalIgnoreCase)))
            .Where(report => report is not null)
            .Cast<SttLifecycleReport>()
            .ToArray();
        if (requiredReports.Length == RequiredProfiles.Length
            && requiredReports.All(report => report.Hardware is not null)
            && HardwareFingerprint(requiredReports[0].Hardware) == HardwareFingerprint(requiredReports[1].Hardware))
            errors.Add("Reference and weak-pc evidence must come from different hardware.");
        if (requiredReports.Select(report => report.AudioSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            errors.Add("Required hardware profiles must use the same lifecycle WAV.");
        foreach (var report in reports)
        {
            var prefix = $"Lifecycle profile '{report.HardwareProfile}'";
            if (report.SchemaVersion != 2) errors.Add($"{prefix} uses unsupported schema {report.SchemaVersion}.");
            if (report.PackId != selectedQuality.PackId || report.ModelId != selectedQuality.ModelId)
                errors.Add($"{prefix} targets a different pack or model.");
            if (!string.Equals(report.PackManifestSha256, manifestSha256, StringComparison.OrdinalIgnoreCase))
                errors.Add($"{prefix} targets a different pack manifest.");
            if (!IsSha256(report.AudioSha256)) errors.Add($"{prefix} has an invalid audio fingerprint.");
            if (report.Iterations is null || report.Iterations.Count < RequiredLifecycleIterations)
                errors.Add($"{prefix} has fewer than {RequiredLifecycleIterations} iterations.");
            if (!report.Passed || report.Failures != 0 || report.Iterations?.Any(item => item.Error is not null || item.OrphanedProcess) != false)
                errors.Add($"{prefix} contains lifecycle failures or orphan processes.");
            if (report.Iterations is { Count: > 0 })
            {
                if (report.Iterations.Any(item => !double.IsFinite(item.ElapsedMs) || item.ElapsedMs < 0
                    || item.WorkingSetBytes < 0 || item.PrivateBytes < 0))
                    errors.Add($"{prefix} contains invalid runtime metrics.");
                var expectedP95 = Percentile(report.Iterations.Select(item => item.ElapsedMs), .95);
                var expectedWorkingSet = report.Iterations.Max(item => item.WorkingSetBytes);
                var expectedPrivate = report.Iterations.Max(item => item.PrivateBytes);
                var expectedFailures = report.Iterations.Count(item => item.Error is not null || item.OrphanedProcess);
                if (Math.Abs(report.P95ElapsedMs - expectedP95) > .000001
                    || report.PeakWorkingSetBytes != expectedWorkingSet
                    || report.PeakPrivateBytes != expectedPrivate
                    || report.Failures != expectedFailures)
                    errors.Add($"{prefix} aggregate metrics do not match its iterations.");
            }
            if (!double.IsFinite(report.P95ElapsedMs) || report.P95ElapsedMs > MaximumColdLifecycleP95Ms)
                errors.Add($"{prefix} exceeds the {MaximumColdLifecycleP95Ms:F0} ms cold-start p95 gate.");
            if (Math.Max(report.PeakWorkingSetBytes, report.PeakPrivateBytes) > selectedQuality.Gate.MaximumMemoryBytes)
                errors.Add($"{prefix} exceeds the quality gate memory limit.");
            if (report.Hardware is null || report.Hardware.LogicalProcessorCount < 1 || report.Hardware.AvailableMemoryBytes <= 0)
                errors.Add($"{prefix} has incomplete hardware metadata.");
        }
        return errors;
    }

    private static string HardwareFingerprint(SttHardwareSnapshot hardware) =>
        string.Join('|', hardware.OperatingSystem, hardware.Architecture, hardware.Processor,
            hardware.LogicalProcessorCount, hardware.AvailableMemoryBytes);

    private static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.Order().ToArray();
        return values[(int)Math.Ceiling(percentile * values.Length) - 1];
    }

    private static void CreateDeterministicArchive(string directory, string archivePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(archivePath)) File.Delete(archivePath);
        using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static async Task<T> ReadAsync<T>(string path) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json)
        ?? throw new InvalidDataException($"JSON evidence is empty: {path}");

    private static bool IsSha256(string value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}

public sealed record SttEvidenceHash(string FileName, string Sha256);
public sealed record SttProductionAttestation(DateTimeOffset CreatedAt, bool Passed, string? PackId, string? ModelId,
    string PackManifestSha256, string? PackArchiveSha256, string DatasetId, string DatasetSha256,
    IReadOnlyList<string> HardwareProfiles, IReadOnlyList<SttEvidenceHash> Evidence,
    IReadOnlyList<string> Errors, int SchemaVersion = 1);
