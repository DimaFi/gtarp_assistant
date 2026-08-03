using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using WpfControl = System.Windows.Controls.Control;

namespace GtaRpAssistant.App.DesignSystem.Controls;

/// <summary>Compact metric card with an optional detail and semantic accent.</summary>
public sealed class MetricCard : WpfControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MetricCard), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(MetricCard), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(MetricCard), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(MediaBrush), typeof(MetricCard), new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Transparent));

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public MediaBrush AccentBrush { get => (MediaBrush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
}
