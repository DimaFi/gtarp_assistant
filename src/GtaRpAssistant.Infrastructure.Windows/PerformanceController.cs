using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record PerformanceActions(bool ExperimentalProactivity, bool GameAudioStt, bool Embeddings, bool HeavyAnimations);

public sealed class PerformanceController
{
    public PerformanceActions Evaluate(PerformanceProfile profile, ResourcePressureLevel pressure)
    {
        if (pressure == ResourcePressureLevel.Hard) return new(false, false, false, false);
        if (pressure == ResourcePressureLevel.Soft) return new(false, false, false, false);
        return Defaults(profile);
    }

    // Compatibility fallback for callers that cannot provide system memory telemetry yet.
    public PerformanceActions Evaluate(PerformanceProfile profile, double processCpuPercent, long workingSetBytes)
    {
        var high = processCpuPercent >= 50 || workingSetBytes >= 2L * 1024 * 1024 * 1024;
        return high ? new(false, false, false, false) : Defaults(profile);
    }

    private static PerformanceActions Defaults(PerformanceProfile profile) => profile switch
        {
            PerformanceProfile.CloudLite => new(false, false, false, false),
            PerformanceProfile.Balanced => new(false, true, false, true),
            PerformanceProfile.LocalHybrid => new(true, true, true, true),
            _ => new(false, true, false, true),
        };
}
