using System.Windows;
using MediaBrush = System.Windows.Media.Brush;

namespace GtaRpAssistant.App.DesignSystem.Controls;

public sealed class OverlayStatusPill : System.Windows.Controls.Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(OverlayStatusPill), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(OverlayTone), typeof(OverlayStatusPill), new FrameworkPropertyMetadata(OverlayTone.Neutral));

    public static readonly DependencyProperty ActivityProperty = DependencyProperty.Register(
        nameof(Activity), typeof(OverlayActivity), typeof(OverlayStatusPill), new FrameworkPropertyMetadata(OverlayActivity.None));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(MediaBrush), typeof(OverlayStatusPill),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Transparent));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public OverlayTone Tone
    {
        get => (OverlayTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public OverlayActivity Activity
    {
        get => (OverlayActivity)GetValue(ActivityProperty);
        set => SetValue(ActivityProperty, value);
    }

    public MediaBrush AccentBrush
    {
        get => (MediaBrush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }
}
