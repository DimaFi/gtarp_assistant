namespace GtaRpAssistant.App.Features;

public partial class ProvidersView : System.Windows.Controls.UserControl
{
    public ProvidersView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ProvidersFeatureViewModel viewModel)
            await viewModel.EnsureInitializedAsync();
    }
}
