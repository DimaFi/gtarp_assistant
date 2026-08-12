namespace GtaRpAssistant.IntegrationTests;

public sealed class SttProductionGateTests
{
    private const string ManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ReferenceAndWeakPcProfilesWithOneHundredCleanCycles_AreAccepted()
    {
        var errors = SttProductionGate.ValidateLifecycleEvidence(Quality(), ManifestHash,
            [Lifecycle("reference"), Lifecycle("weak-pc")]);

        Assert.Empty(errors);
    }

    [Fact]
    public void MissingWeakPcProfile_IsRejected()
    {
        var errors = SttProductionGate.ValidateLifecycleEvidence(Quality(), ManifestHash,
            [Lifecycle("reference")]);

        Assert.Contains(errors, error => error.Contains("weak-pc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LifecycleForDifferentPackManifest_IsRejected()
    {
        var reports = new[]
        {
            Lifecycle("reference") with { PackManifestSha256 = new string('b', 64) },
            Lifecycle("weak-pc"),
        };

        var errors = SttProductionGate.ValidateLifecycleEvidence(Quality(), ManifestHash, reports);

        Assert.Contains(errors, error => error.Contains("different pack manifest", StringComparison.OrdinalIgnoreCase));
    }

    private static SttBenchmarkReport Quality()
    {
        var item = new SttCaseReport("case-1", "audio/case-1.wav", "тестовая фраза", "тестовая фраза",
            0, 1, 100, 500L * 1024 * 1024, 500L * 1024 * 1024, null, []);
        return new(DateTimeOffset.UtcNow, "pack-a", "model-a", "dataset", true, 0, 1, 100, 0,
            500L * 1024 * 1024, 500L * 1024 * 1024, [item], new string('c', 64), 1,
            new SttGate(1, .25, .85, 5_000, 1_100L * 1024 * 1024));
    }

    private static SttLifecycleReport Lifecycle(string profile)
    {
        var iterations = Enumerable.Range(1, 100)
            .Select(index => new SttLifecycleIteration(index, index + 1000, 1_000,
                500L * 1024 * 1024, 500L * 1024 * 1024, false, null))
            .ToArray();
        var weak = profile == "weak-pc";
        return new(DateTimeOffset.UtcNow, "pack-a", "model-a", ManifestHash, new string('d', 64), profile,
            new SttHardwareSnapshot("Windows", "X64", weak ? "Weak CPU" : "Reference CPU", weak ? 2 : 4,
                (weak ? 4L : 8L) * 1024 * 1024 * 1024), true, 0,
            1_000, 500L * 1024 * 1024, 500L * 1024 * 1024, iterations);
    }
}
