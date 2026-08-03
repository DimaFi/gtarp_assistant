using System.Windows;
using System.Windows.Controls;

namespace GtaRpAssistant.App.DesignSystem.Controls;

/// <summary>Reusable titled card section. Business content is supplied by the feature view.</summary>
public sealed class FeatureSection : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FeatureSection), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(FeatureSection), new FrameworkPropertyMetadata(""));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}
