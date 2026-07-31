using System.Windows.Input;

namespace GtaRpAssistant.App.Shell;

public sealed class ShellNavigationItem : ObservableObject
{
    private bool _isSelected;

    public ShellNavigationItem(string id, string title, string symbol, object content, Action<ShellNavigationItem> select)
    {
        Id = id;
        Title = title;
        Symbol = symbol;
        Content = content;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Id { get; }
    public string Title { get; }
    public string Symbol { get; }
    public object Content { get; }
    public ICommand SelectCommand { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
