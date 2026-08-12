using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public sealed class OverlayService : IOverlayService
{
    private readonly OverlayWindow _compact;
    private readonly ExpandedOverlayWindow _expanded;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _positionSaveGate = new(1, 1);
    private AssistantAnswer? _currentAnswer;
    private OverlayPresentation? _currentPresentation;

    public OverlayService(OverlayWindow compact, ExpandedOverlayWindow expanded, SettingsService settings)
    {
        _compact = compact;
        _expanded = expanded;
        _settings = settings;
        _compact.ExpandRequested += (_, _) => ExpandCurrent();
        _compact.PositionChangedByUser += (_, position) => _ = SavePositionAsync(position);
        _expanded.TechnicalDetailsRequested += (_, _) => { if (_currentAnswer is not null) DetailsRequested?.Invoke(this, _currentAnswer); };
        _expanded.IncorrectReported += (_, _) => { if (_currentAnswer is not null) IncorrectReported?.Invoke(this, _currentAnswer); };
        _expanded.SnoozeRequested += (_, _) => SnoozeRequested?.Invoke(this, EventArgs.Empty);
        _expanded.CollapseRequested += (_, _) => _ = ShowCompactCurrentAsync();
        _expanded.VoicePreviewConfirmed += (_, text) => VoicePreviewConfirmed?.Invoke(this, text);
        _expanded.VoicePreviewCancelled += (_, _) => VoicePreviewCancelled?.Invoke(this, EventArgs.Empty);
    }

    public nint TargetWindowHandle { get; set; }
    public bool IsVisible => _compact.IsVisible || _expanded.IsVisible;
    public event EventHandler<AssistantAnswer>? DetailsRequested;
    public event EventHandler<AssistantAnswer>? IncorrectReported;
    public event EventHandler? SnoozeRequested;
    public event EventHandler<string>? VoicePreviewConfirmed;
    public event EventHandler? VoicePreviewCancelled;

    public async Task ShowAsync(AssistantAnswer answer, CancellationToken cancellationToken)
    {
        _currentAnswer = answer;
        _currentPresentation = OverlayPresentationFactory.Create(answer);
        await ShowPresentationAsync(
            _currentPresentation,
            TimeSpan.FromSeconds(Math.Clamp(_settings.Current.OverlaySeconds, 2, 60)),
            cancellationToken,
            allowPin: true);
    }

    public async Task ShowMicroModelStatusAsync(MicroModelStatus status, CancellationToken cancellationToken)
    {
        _currentAnswer = null;
        _currentPresentation = OverlayPresentationFactory.Create(status);
        var duration = status.State is MicroModelState.Starting or MicroModelState.Generating ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
        await ShowPresentationAsync(_currentPresentation, duration, cancellationToken, allowPin: false);
    }

    public async Task ShowListeningAsync(CancellationToken cancellationToken)
    {
        _currentAnswer = null;
        _currentPresentation = OverlayPresentationFactory.CreateListening();
        await ShowPresentationAsync(_currentPresentation, TimeSpan.FromSeconds(20), cancellationToken, allowPin: false);
    }

    public Task ShowVoicePreviewAsync(string transcript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _currentAnswer = null;
        _currentPresentation = null;
        var current = _settings.Current;
        return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _compact.HideOverlay();
            _expanded.ShowVoicePreview(transcript, current.OverlayPosition, TargetWindowHandle);
        }).Task;
    }

    private async Task ShowPresentationAsync(OverlayPresentation presentation, TimeSpan duration, CancellationToken cancellationToken, bool allowPin)
    {
        var current = _settings.Current;
        if (!current.OverlayEnabled) return;
        var useCustomPosition = current.OverlayPosition.Equals("Custom", StringComparison.OrdinalIgnoreCase);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            _expanded.HideOverlay();
            await _compact.ShowAsync(
                presentation,
                duration,
                current.OverlayPosition,
                TargetWindowHandle,
                cancellationToken,
                autoHide: !(allowPin && current.OverlayPinned),
                customLeft: useCustomPosition ? current.OverlayLeft : null,
                customTop: useCustomPosition ? current.OverlayTop : null);
        }).Task.Unwrap();
    }

    public Task HideAsync()
    {
        void Hide()
        {
            _compact.HideOverlay();
            _expanded.HideOverlay();
        }
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) Hide();
        else System.Windows.Application.Current.Dispatcher.Invoke(Hide);
        return Task.CompletedTask;
    }

    private void ExpandCurrent()
    {
        if (_currentPresentation is null) return;
        _compact.HideOverlay();
        _expanded.ShowPresentation(_currentPresentation, _settings.Current.OverlayPosition, TargetWindowHandle);
    }

    private async Task ShowCompactCurrentAsync()
    {
        if (_currentPresentation is null) return;
        var current = _settings.Current;
        var duration = TimeSpan.FromSeconds(Math.Clamp(current.OverlaySeconds, 2, 60));
        var useCustomPosition = current.OverlayPosition.Equals("Custom", StringComparison.OrdinalIgnoreCase);
        await _compact.ShowAsync(
            _currentPresentation,
            duration,
            current.OverlayPosition,
            TargetWindowHandle,
            autoHide: !current.OverlayPinned,
            customLeft: useCustomPosition ? current.OverlayLeft : null,
            customTop: useCustomPosition ? current.OverlayTop : null);
    }

    private async Task SavePositionAsync(OverlayPositionChangedEventArgs position)
    {
        await _positionSaveGate.WaitAsync();
        try
        {
            var current = _settings.Current;
            await _settings.SaveAsync(current with
            {
                OverlayPosition = "Custom",
                OverlayLeft = position.Left,
                OverlayTop = position.Top,
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // A failed preference write must never interrupt the in-game overlay.
        }
        finally
        {
            _positionSaveGate.Release();
        }
    }
}
