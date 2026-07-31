using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class GameSessionMonitor(IGameProcessDetector detector, GameProfile profile, TimeSpan? interval = null) : IAsyncDisposable
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(3);
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private GameProcessInfo? _current;

    public GameProcessInfo? Current => _current;
    public event EventHandler<GameProcessInfo?>? ProcessChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_worker is not null) return Task.CompletedTask;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = MonitorAsync(_cancellation.Token);
        return Task.CompletedTask;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!cancellationToken.IsCancellationRequested)
        {
            var found = await detector.FindAsync(profile, cancellationToken);
            if (found?.ProcessId != _current?.ProcessId)
            {
                _current = found;
                ProcessChanged?.Invoke(this, found);
            }
            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    public async Task StopAsync()
    {
        if (_worker is null) return;
        _cancellation?.Cancel();
        try { await _worker; }
        catch (OperationCanceledException) { }
        _worker = null;
        _cancellation?.Dispose();
        _cancellation = null;
        _current = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
