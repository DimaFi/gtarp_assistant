using System.Diagnostics;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record ProcessPerformanceSnapshot(double CpuPercent, long WorkingSetBytes, PerformanceActions Actions);

public sealed class ProcessPerformanceMonitor(PerformanceController controller, Func<PerformanceProfile> profile, TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(5);
    private CancellationTokenSource? _cancellation;
    private Task? _worker;

    public event EventHandler<ProcessPerformanceSnapshot>? SnapshotAvailable;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_worker is not null) return Task.CompletedTask;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = MonitorAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var lastCpu = process.TotalProcessorTime;
        var lastAt = Stopwatch.GetTimestamp();
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            process.Refresh();
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastAt, now);
            var cpuDelta = process.TotalProcessorTime - lastCpu;
            var cpu = elapsed <= TimeSpan.Zero ? 0 : cpuDelta.TotalMilliseconds / elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            lastCpu = process.TotalProcessorTime;
            lastAt = now;
            var actions = controller.Evaluate(profile(), cpu, process.WorkingSet64);
            SnapshotAvailable?.Invoke(this, new(cpu, process.WorkingSet64, actions));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation?.Cancel();
        if (_worker is not null)
        {
            try { await _worker; }
            catch (OperationCanceledException) { }
        }
        _worker = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
