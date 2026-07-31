using System.Windows;
using System.Windows.Media;

namespace GtaRpAssistant.App.DesignSystem.Controls;

public partial class VisionConsentCard : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty DestinationProperty = DependencyProperty.Register(
        nameof(Destination), typeof(string), typeof(VisionConsentCard), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty PreviewSourceProperty = DependencyProperty.Register(
        nameof(PreviewSource), typeof(ImageSource), typeof(VisionConsentCard), new FrameworkPropertyMetadata(null));

    public VisionConsentCard() => InitializeComponent();

    public string Destination
    {
        get => (string)GetValue(DestinationProperty);
        set => SetValue(DestinationProperty, value);
    }

    public ImageSource? PreviewSource
    {
        get => (ImageSource?)GetValue(PreviewSourceProperty);
        set => SetValue(PreviewSourceProperty, value);
    }

    public event EventHandler? ConfirmRequested;
    public event EventHandler? CancelRequested;

    private void Confirm_Click(object sender, RoutedEventArgs e) => ConfirmRequested?.Invoke(this, EventArgs.Empty);
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);
}
