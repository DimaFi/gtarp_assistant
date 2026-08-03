using System.Windows;
using System.Windows.Input;

namespace GtaRpAssistant.App;

public partial class ExpandedOverlayWindow : Window
{
    public ExpandedOverlayWindow() => InitializeComponent();

    public event EventHandler? TechnicalDetailsRequested;
    public event EventHandler? IncorrectReported;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? CollapseRequested;
    public event EventHandler<string>? VoicePreviewConfirmed;
    public event EventHandler? VoicePreviewCancelled;
    private bool _showingVoicePreview;

    public void ShowPresentation(OverlayPresentation presentation, string position, nint targetWindow)
    {
        SetVoicePreviewMode(false);
        TitleText.Text = presentation.Title;
        SummaryText.Text = string.IsNullOrWhiteSpace(presentation.Summary) ? presentation.Message : presentation.Summary;
        SetItems(StepsSection, StepsList, presentation.Steps);
        SetItems(CausesSection, CausesList, presentation.PossibleCauses);
        SetItems(FollowUpsSection, FollowUpsList, presentation.FollowUpSuggestions);
        StatusPill.Text = presentation.Status;
        StatusPill.Tone = presentation.Tone;
        StatusPill.Activity = presentation.Activity;
        CardShell.Tone = presentation.Tone;
        SourceText.Text = presentation.Source;
        UpdatedText.Text = presentation.Updated;
        ProviderText.Text = string.IsNullOrWhiteSpace(presentation.Provider) ? string.Empty : $"Ответ: {presentation.Provider}";
        CommunityBadge.Visibility = presentation.IsCommunity ? Visibility.Visible : Visibility.Collapsed;
        Show();
        OverlayWindowPositioner.Position(this, position, targetWindow, 420);
        Activate();
    }

    public void ShowVoicePreview(string transcript, string position, nint targetWindow)
    {
        SetVoicePreviewMode(true);
        TitleText.Text = "Проверьте голосовой вопрос";
        StatusPill.Text = "Ожидает подтверждения";
        StatusPill.Tone = OverlayTone.Neutral;
        StatusPill.Activity = OverlayActivity.None;
        CardShell.Tone = OverlayTone.Neutral;
        CommunityBadge.Visibility = Visibility.Collapsed;
        VoicePreviewText.Text = transcript;
        Show();
        OverlayWindowPositioner.Position(this, position, targetWindow, 420);
        Activate();
        VoicePreviewText.Focus();
        VoicePreviewText.CaretIndex = VoicePreviewText.Text.Length;
    }

    private void SetVoicePreviewMode(bool enabled)
    {
        _showingVoicePreview = enabled;
        VoicePreviewPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        AnswerScroll.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        AnswerSourcePanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        AnswerQuickActions.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetItems(FrameworkElement section, System.Windows.Controls.ItemsControl list, IReadOnlyList<string>? items)
    {
        list.ItemsSource = items;
        section.Visibility = items is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    public void HideOverlay() => Hide();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (_showingVoicePreview) VoicePreviewCancelled?.Invoke(this, EventArgs.Empty);
        HideOverlay();
        e.Handled = true;
    }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_showingVoicePreview) VoicePreviewCancelled?.Invoke(this, EventArgs.Empty);
        HideOverlay();
    }
    private void VoicePreviewConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VoicePreviewText.Text)) return;
        VoicePreviewConfirmed?.Invoke(this, VoicePreviewText.Text.Trim());
        HideOverlay();
    }
    private void VoicePreviewCancel_Click(object sender, RoutedEventArgs e)
    {
        VoicePreviewCancelled?.Invoke(this, EventArgs.Empty);
        HideOverlay();
    }
    private void QuickActions_CollapseRequested(object sender, EventArgs e) { HideOverlay(); CollapseRequested?.Invoke(this, EventArgs.Empty); }
    private void QuickActions_TechnicalDetailsRequested(object sender, EventArgs e) => TechnicalDetailsRequested?.Invoke(this, EventArgs.Empty);
    private void QuickActions_IncorrectRequested(object sender, EventArgs e) { IncorrectReported?.Invoke(this, EventArgs.Empty); HideOverlay(); }
    private void QuickActions_SnoozeRequested(object sender, EventArgs e) { SnoozeRequested?.Invoke(this, EventArgs.Empty); HideOverlay(); }
}
