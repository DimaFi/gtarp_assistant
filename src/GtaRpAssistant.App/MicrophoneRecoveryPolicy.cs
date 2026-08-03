using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.App;

public sealed record MicrophoneRecoveryPolicy(int MaximumAttempts, TimeSpan RetryDelay)
{
    public static MicrophoneRecoveryPolicy Default { get; } = new(10, TimeSpan.FromSeconds(2));

    public MicrophoneDeviceInfo? FindPreferred(IReadOnlyList<MicrophoneDeviceInfo> devices, string preferredDeviceId) =>
        devices.FirstOrDefault(device => string.Equals(device.Id, preferredDeviceId, StringComparison.Ordinal));
}
