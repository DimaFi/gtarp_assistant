using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public sealed class OverlayService : IOverlayService
{
    private readonly OverlayWindow _compact;
    private readonly ExpandedOverlayWindow _expanded;
    private readonly SettingsService _settings;
    private AssistantAnswer? _currentAnswer;
    private OverlayPresentation? _currentPresentation;

    public OverlayService(OverlayWindow compact, ExpandedOverlayWindow expanded, SettingsService settings)
    {
        _compact = compact;
        _expanded = expanded;
        _settings = settings;
        _compact.ExpandRequested += (_, _) => ExpandCurrent();
        _expanded.TechnicalDetailsRequested += (_, _) => { if (_currentAnswer is not null) DetailsRequested?.Invoke(this, _currentAnswer); };
        _expanded.IncorrectReported += (_, _) => { if (_currentAnswer is not null) IncorrectReported?.Invoke(this, _currentAnswer); };
        _expanded.SnoozeRequested += (_, _) => SnoozeRequested?.Invoke(this, EventArgs.Empty);
        _expanded.CollapseRequested += (_, _) => _ = ShowCompactCurrentAsync();
    }

    public nint TargetWindowHandle { get; set; }
    public bool IsVisible => _compact.IsVisible || _expanded.IsVisible;
    public event EventHandler<AssistantAnswer>? DetailsRequested;
    public event EventHandler<AssistantAnswer>? IncorrectReported;
    public event EventHandler? SnoozeRequested;

    public async Task ShowAsync(AssistantAnswer answer, CancellationToken cancellationToken)
    {
        _currentAnswer = answer;
        _currentPresentation = OverlayPresentationFactory.Create(answer);
        await ShowPresentationAsync(_currentPresentation, TimeSpan.FromSeconds(Math.Clamp(_settings.Current.OverlaySeconds, 2, 60)), cancellationToken);
    }

    public async Task ShowMicroModelStatusAsync(MicroModelStatus status, CancellationToken cancellationToken)
    {
        _currentAnswer = null;
        _currentPresentation = OverlayPresentationFactory.Create(status);
        var duration = status.State is MicroModelState.Starting or MicroModelState.Generating ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
        await ShowPresentationAsync(_currentPresentation, duration, cancellationToken);
    }

    public async Task ShowListeningAsync(CancellationToken cancellationToken)
    {
        _currentAnswer = null;
        _currentPresentation = OverlayPresentationFactory.CreateListening();
        await ShowPresentationAsync(_currentPresentation, TimeSpan.FromSeconds(20), cancellationToken);
    }

    private async Task ShowPresentationAsync(OverlayPresentation presentation, TimeSpan duration, CancellationToken cancellationToken)
    {
        var current = _settings.Current;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            _expanded.HideOverlay();
            await _compact.ShowAsync(presentation, duration, current.OverlayPosition, TargetWindowHandle, cancellationToken);
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
        await _compact.ShowAsync(_currentPresentation, duration, current.OverlayPosition, TargetWindowHandle);
    }
}
