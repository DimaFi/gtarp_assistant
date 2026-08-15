using System.IO;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Services;

public interface IAppDialogService
{
    void ShowError(string title, string message);
    void ShowInformation(string title, string message);
    bool Confirm(string title, string message, bool dangerous = false);
    void ShowAnswerDetails(AssistantAnswer answer);
    string? PickExecutable(string title, string? currentPath);
    string? PickGgufFile(string title, string? initialDirectory);
    string? PickKnowledgeFile(string title);
    string? PickFolder(string title, string? initialDirectory);
}

public sealed class AppDialogService : IAppDialogService
{
    public void ShowError(string title, string message) => Show(title, message, System.Windows.MessageBoxImage.Error);
    public void ShowInformation(string title, string message) => Show(title, message, System.Windows.MessageBoxImage.Information);

    public bool Confirm(string title, string message, bool dangerous = false)
    {
        bool Display()
        {
            var owner = System.Windows.Application.Current.MainWindow;
            var image = dangerous ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Question;
            var result = owner?.IsVisible == true
                ? System.Windows.MessageBox.Show(owner, message, title, System.Windows.MessageBoxButton.YesNo, image, System.Windows.MessageBoxResult.No)
                : System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, image, System.Windows.MessageBoxResult.No);
            return result == System.Windows.MessageBoxResult.Yes;
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        return dispatcher.CheckAccess() ? Display() : dispatcher.Invoke(Display);
    }

    public string? PickExecutable(string title, string? currentPath)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "Приложения Windows (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(currentPath.Trim().Trim('"'));
            if (File.Exists(expanded)) dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(expanded));
        }
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickGgufFile(string title, string? initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "Модели GGUF (*.gguf)|*.gguf",
            CheckFileExists = true,
            Multiselect = false,
        };
        var directory = ResolveDirectory(initialDirectory);
        if (directory is not null) dialog.InitialDirectory = directory;
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickKnowledgeFile(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "Документы базы знаний (*.json;*.csv)|*.json;*.csv|JSON (*.json)|*.json|CSV (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialDirectory)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = ResolveDirectory(initialDirectory) ?? "",
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public void ShowAnswerDetails(AssistantAnswer answer)
    {
        var message = $"Источник: {answer.SourceTitle ?? "не найден"}\nОбновлено: {answer.SourceUpdatedAt:dd.MM.yyyy}\nFact IDs: {string.Join(", ", answer.UsedFactIds)}";
        Show("Подробнее", message, System.Windows.MessageBoxImage.Information);
    }

    private static void Show(string title, string message, System.Windows.MessageBoxImage image)
    {
        void Display()
        {
            var owner = System.Windows.Application.Current.MainWindow;
            if (owner?.IsVisible == true)
                System.Windows.MessageBox.Show(owner, message, title, System.Windows.MessageBoxButton.OK, image);
            else
                System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, image);
        }

        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) Display(); else dispatcher.Invoke(Display);
    }

    private static string? ResolveDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (File.Exists(expanded)) expanded = Path.GetDirectoryName(Path.GetFullPath(expanded)) ?? expanded;
            return Directory.Exists(expanded) ? Path.GetFullPath(expanded) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
