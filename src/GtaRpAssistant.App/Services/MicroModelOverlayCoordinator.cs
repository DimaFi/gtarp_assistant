using GtaRpAssistant.Core;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App.Services;

public sealed class MicroModelOverlayCoordinator : IDisposable
{
    private readonly IMicroModelManager _manager;
    private readonly OverlayService _overlay;
    private readonly ILogger<MicroModelOverlayCoordinator> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    public MicroModelOverlayCoordinator(IMicroModelManager manager, OverlayService overlay, ILogger<MicroModelOverlayCoordinator> logger)
    {
        _manager = manager;
        _overlay = overlay;
        _logger = logger;
        _manager.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, MicroModelStateChangedEventArgs args) => _ = PresentAsync(args.Status);

    private async Task PresentAsync(MicroModelStatus status)
    {
        try
        {
            if (status.State == MicroModelState.Stopped)
            {
                await _overlay.HideAsync();
                return;
            }
            if (status.State is MicroModelState.Starting or MicroModelState.Generating or MicroModelState.Ready or MicroModelState.Idle or MicroModelState.Faulted or MicroModelState.MemoryLimitExceeded)
                await _overlay.ShowMicroModelStatusAsync(status, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning("MicroModel overlay state failed; state={State}; type={ErrorType}", status.State, ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        _manager.StateChanged -= OnStateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
