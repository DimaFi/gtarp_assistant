using System.Diagnostics;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.IntegrationTests;

public sealed class ProcessLoopbackTests
{
    [Fact]
    public async Task ProcessLoopback_ActivatesAndStopsForCurrentProcess()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
        await using var capture = new ProcessLoopbackCaptureService(Environment.ProcessId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var frame = new TaskCompletionSource<(AudioSourceKind Source, int Rate, int Count)>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameCaptured += (_, e) => frame.TrySetResult((e.Source, e.SampleRate, e.Samples.Length));
        await capture.StartAsync(timeout.Token);
        var received = await frame.Task.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token);
        Assert.Equal(AudioSourceKind.GameAudio, received.Source); Assert.Equal(16_000, received.Rate); Assert.True(received.Count > 0);
        await capture.StopAsync(timeout.Token);
    }
}
