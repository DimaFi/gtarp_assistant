using System.Windows;
using System.Windows.Controls;
using MediaBrush = System.Windows.Media.Brush;

namespace GtaRpAssistant.App.DesignSystem.Controls;

/// <summary>
/// Shared visual shell for compact and expanded overlay cards.
/// It owns semantic tone presentation only; answer content remains supplied by the window.
/// </summary>
public sealed class OverlayCardShell : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone),
        typeof(OverlayTone),
        typeof(OverlayCardShell),
        new FrameworkPropertyMetadata(OverlayTone.Neutral));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(MediaBrush),
        typeof(OverlayCardShell),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Transparent));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(CornerRadius),
        typeof(OverlayCardShell),
        new FrameworkPropertyMetadata(new CornerRadius(16)));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded),
        typeof(bool),
        typeof(OverlayCardShell),
        new FrameworkPropertyMetadata(false));

    public OverlayTone Tone
    {
        get => (OverlayTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public MediaBrush AccentBrush
    {
        get => (MediaBrush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }
}
