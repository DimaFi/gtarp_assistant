using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfControl = System.Windows.Controls.Control;

namespace GtaRpAssistant.App.DesignSystem.Controls;

/// <summary>Shared title, description and optional action for shell feature pages.</summary>
public sealed class FeaturePageHeader : WpfControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FeaturePageHeader), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(FeaturePageHeader), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(FeaturePageHeader), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(FeaturePageHeader));
    public static readonly DependencyProperty ActionAutomationIdProperty = DependencyProperty.Register(
        nameof(ActionAutomationId), typeof(string), typeof(FeaturePageHeader), new FrameworkPropertyMetadata(""));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public string ActionAutomationId { get => (string)GetValue(ActionAutomationIdProperty); set => SetValue(ActionAutomationIdProperty, value); }
}
