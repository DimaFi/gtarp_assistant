using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record PerformanceActions(bool ExperimentalProactivity, bool GameAudioStt, bool Embeddings, bool HeavyAnimations);

public sealed class PerformanceController
{
    public PerformanceActions Evaluate(PerformanceProfile profile, double processCpuPercent, long workingSetBytes)
    {
        var high = processCpuPercent >= 15 || workingSetBytes >= 200 * 1024 * 1024;
        if (high) return new(false, false, false, false);
        return profile switch
        {
            PerformanceProfile.CloudLite => new(false, false, false, false),
            PerformanceProfile.Balanced => new(false, true, false, true),
            PerformanceProfile.LocalHybrid => new(true, true, true, true),
            _ => new(false, true, false, true),
        };
    }
}
