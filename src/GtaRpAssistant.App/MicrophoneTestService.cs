using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.App;

public sealed record MicrophoneTestResult(double PeakLevel, bool SignalDetected);

public sealed class MicrophoneTestService
{
    public async Task<MicrophoneTestResult> RunAsync(
        string deviceId,
        TimeSpan duration,
        Action<double> levelChanged,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Не выбран микрофон.", nameof(deviceId));
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(duration));

        var peak = 0d;
        await using var capture = new WasapiMicrophoneCaptureService(deviceId);
        void OnFrame(object? sender, GtaRpAssistant.Core.AudioFrameEventArgs args)
        {
            var level = CalculateLevel(args.Samples.Span);
            peak = Math.Max(peak, level);
            levelChanged(level);
        }

        capture.FrameCaptured += OnFrame;
        try
        {
            await capture.StartAsync(cancellationToken);
            await Task.Delay(duration, cancellationToken);
            await capture.StopAsync(CancellationToken.None);
        }
        finally
        {
            capture.FrameCaptured -= OnFrame;
            levelChanged(0);
        }

        return new(peak, peak >= 0.02);
    }

    public static double CalculateLevel(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += (double)sample * sample;
        var rms = Math.Sqrt(sum / samples.Length);
        return Math.Clamp(rms / 8000d, 0, 1);
    }
}
