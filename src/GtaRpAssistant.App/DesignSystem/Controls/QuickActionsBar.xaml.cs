using System.Windows;

namespace GtaRpAssistant.App.DesignSystem.Controls;

public partial class QuickActionsBar : System.Windows.Controls.UserControl
{
    public QuickActionsBar() => InitializeComponent();

    public event EventHandler? TechnicalDetailsRequested;
    public event EventHandler? IncorrectRequested;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? CollapseRequested;

    private void TechnicalDetails_Click(object sender, RoutedEventArgs e) => TechnicalDetailsRequested?.Invoke(this, EventArgs.Empty);
    private void Incorrect_Click(object sender, RoutedEventArgs e) => IncorrectRequested?.Invoke(this, EventArgs.Empty);
    private void Snooze_Click(object sender, RoutedEventArgs e) => SnoozeRequested?.Invoke(this, EventArgs.Empty);
    private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);
}
