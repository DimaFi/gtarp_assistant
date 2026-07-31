using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GtaRpAssistant.App.DesignSystem.Controls;
using GtaRpAssistant.App.Features;
using GtaRpAssistant.App.Shell;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Services;

public sealed class UiAutomationScenarioService
{
    private const string TestPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
    private readonly OverlayService _overlay;
    private readonly OverlayWindow _compact;
    private readonly ExpandedOverlayWindow _expanded;
    private readonly SettingsWorkspace _workspace;
    private readonly SettingsService _settings;
    private readonly SettingsSaveCoordinator _settingsSave;
    private readonly ISecretStore _secrets;
    private readonly ApplicationExecutionMode _executionMode;
    private readonly ApplicationUiState _ui;

    public UiAutomationScenarioService(
        OverlayService overlay,
        OverlayWindow compact,
        ExpandedOverlayWindow expanded,
        SettingsWorkspace workspace,
        SettingsService settings,
        SettingsSaveCoordinator settingsSave,
        ISecretStore secrets,
        ApplicationExecutionMode executionMode,
        ApplicationUiState ui)
    {
        _overlay = overlay;
        _compact = compact;
        _expanded = expanded;
        _workspace = workspace;
        _settings = settings;
        _settingsSave = settingsSave;
        _secrets = secrets;
        _executionMode = executionMode;
        _ui = ui;
    }

    public async Task<IReadOnlyList<string>> RunAsync(Window owner, string? outputDirectory = null)
    {
        if (!_executionMode.IsAutomation)
            throw new InvalidOperationException("UI automation scenarios require an isolated automation profile.");
        var paths = new List<string>();
        if (outputDirectory is not null) Directory.CreateDirectory(outputDirectory);

        await RunKeyboardAndMinimumLayoutAsync((MainWindow)owner);
        await RunSettingsRoundTripAsync((MainWindow)owner);

        var answer = new AssistantAnswer(
            AnswerDecision.Show,
            "Награда за достижение",
            "По данным игроков: награда составляет 25 BP.",
            ["ui-smoke-fact"],
            "Данные игроков",
            new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
            false,
            "UI automation smoke",
            new ProblemSolutionDetails(
                "Краткая проверенная подсказка по достижению.",
                ["Откройте список достижений", "Проверьте текущее условие", "Сверьте награду с источником"],
                ["Условие ещё не выполнено", "Прогресс ещё не обновился"],
                false,
                false,
                ["Показать связанные достижения"]),
            "ui-smoke-provider",
            "ui-smoke-model");

        var compactLifetime = _overlay.ShowAsync(answer, CancellationToken.None);
        await YieldRenderAsync();
        if (!_compact.IsVisible || _expanded.IsVisible || _compact.ShowActivated)
            throw new InvalidOperationException("Compact overlay visibility/focus contract failed.");
        UiVisualTestHelper.ValidateAutomationContract(
            _compact,
            "Overlay.Compact",
            "Overlay.Title",
            "Overlay.Message",
            "Overlay.Expand",
            "Overlay.Hide");
        CaptureIfRequested(_compact, outputDirectory, "overlay-compact.png", paths);

        UiVisualTestHelper.Click(_compact, "Overlay.Expand");
        await YieldRenderAsync();
        if (_compact.IsVisible || !_expanded.IsVisible || !_expanded.ShowActivated)
            throw new InvalidOperationException("Expanded overlay visibility/focus contract failed.");
        UiVisualTestHelper.ValidateAutomationContract(
            _expanded,
            "Overlay.Expanded",
            "Overlay.CommunityBadge",
            "Overlay.QuickActions",
            "Overlay.TechnicalDetails",
            "Overlay.Incorrect",
            "Overlay.Snooze",
            "Overlay.Collapse");
        var communityBadge = UiVisualTestHelper.Find<FrameworkElement>(_expanded, "Overlay.CommunityBadge");
        if (communityBadge.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Community overlay badge is not visible for player data.");
        CaptureIfRequested(_expanded, outputDirectory, "overlay-expanded.png", paths);

        UiVisualTestHelper.Click(_expanded, "Overlay.Collapse");
        await YieldRenderAsync();
        if (!_compact.IsVisible || _expanded.IsVisible)
            throw new InvalidOperationException("Overlay collapse contract failed.");
        UiVisualTestHelper.Click(_compact, "Overlay.Hide");
        await compactLifetime;
        await _overlay.HideAsync();
        if (_overlay.IsVisible) throw new InvalidOperationException("Overlay hide contract failed.");

        var warningAnswer = new AssistantAnswer(
            AnswerDecision.AskForMoreInformation,
            "Нужно уточнить ситуацию",
            "Проверенных данных пока недостаточно. Уточните сервер и условие.",
            [],
            null,
            null,
            false,
            "UI automation warning",
            new ProblemSolutionDetails(
                "Уточните исходные данные.",
                ["Укажите сервер", "Опишите точное условие"],
                [],
                true,
                false,
                []));
        var warningLifetime = _overlay.ShowAsync(warningAnswer, CancellationToken.None);
        await YieldRenderAsync();
        var warningShell = UiVisualTestHelper.Find<OverlayCardShell>(_compact, "Overlay.CardShell");
        if (warningShell.Tone != OverlayTone.Warning)
            throw new InvalidOperationException("Compact warning overlay did not apply the warning semantic tone.");
        if (warningShell.AccentBrush is not System.Windows.Media.SolidColorBrush actualWarning ||
            System.Windows.Application.Current.FindResource("Brush.Warning") is not System.Windows.Media.SolidColorBrush expectedWarning ||
            actualWarning.Color != expectedWarning.Color)
            throw new InvalidOperationException("Compact warning overlay did not resolve the warning accent brush.");
        await _overlay.HideAsync();
        await warningLifetime;

        var listeningLifetime = _overlay.ShowListeningAsync(CancellationToken.None);
        await YieldRenderAsync();
        var listeningPill = UiVisualTestHelper.Find<OverlayStatusPill>(_compact, "Overlay.Status");
        if (!_compact.IsVisible || listeningPill.Activity != OverlayActivity.Listening || Math.Abs(_compact.Width - 250) > 1)
            throw new InvalidOperationException(
                $"Listening pill compact layout contract failed: visible={_compact.IsVisible}, activity={listeningPill.Activity}, width={_compact.Width:0.##}.");
        if (UiVisualTestHelper.Find<FrameworkElement>(_compact, "Overlay.Title").Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Listening pill unexpectedly exposes the full answer title.");
        await _overlay.HideAsync();
        await listeningLifetime;

        RunVisionDialog(owner, confirm: false, outputDirectory, paths);
        RunVisionDialog(owner, confirm: true, outputDirectory: null, paths);
        owner.Activate();
        return paths;
    }

    public async Task<string> RunLocalAiE2eAsync(MainWindow owner, string phase, string modelKey, string outputDirectory)
    {
        if (!_executionMode.IsAutomation)
            throw new InvalidOperationException("Local AI E2E requires an isolated automation profile.");
        if (phase is not ("configure" or "verify"))
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Supported phases: configure, verify.");
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var providers = owner.SelectFeatureForAutomation("providers");
        var viewModel = providers.DataContext as ProvidersFeatureViewModel
            ?? throw new InvalidOperationException("Providers page has no ProvidersFeatureViewModel data context.");
        await ExecuteAndWaitAsync(viewModel.RefreshLocalAiCommand, TimeSpan.FromSeconds(30));

        var selected = viewModel.InstalledModels.FirstOrDefault(x => string.Equals(x.Key, modelKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Expected LLM '{modelKey}' was not shown in the installed chat-model list.");
        if (viewModel.InstalledModels.Any(x => !x.IsChatModel || x.Type == LocalAiModelType.Embedding))
            throw new InvalidOperationException("An embedding/non-chat model leaked into the installed chat-model list.");

        if (phase == "configure")
        {
            viewModel.PendingModelKey = selected.Key;
            await ExecuteAndWaitAsync(viewModel.LoadModelCommand, TimeSpan.FromMinutes(3));
            if (!viewModel.SetupProgress.StartsWith("✓", StringComparison.Ordinal))
                throw new InvalidOperationException($"Selected model was not activated. Status: {viewModel.SetupProgress}");
        }

        if (!string.Equals(viewModel.PendingModelKey, modelKey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(viewModel.Settings.Model, modelKey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_settings.Current.Model, modelKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Model selection was not restored in phase '{phase}'. Pending='{viewModel.PendingModelKey}', workspace='{viewModel.Settings.Model}', persisted='{_settings.Current.Model}'.");

        var reportPath = Path.Combine(outputDirectory, $"{phase}.json");
        var report = new
        {
            phase,
            modelKey,
            installedChatModels = viewModel.InstalledModels.Select(x => new { x.Key, x.DisplayName, x.Type, x.Format, x.IsLoaded }).ToArray(),
            viewModel.PendingModelKey,
            persistedModel = _settings.Current.Model,
            viewModel.EngineStatus,
            viewModel.ApiStatus,
            viewModel.ModelStatus,
            viewModel.CapabilityStatus,
            viewModel.SetupProgress,
            checkedAt = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return reportPath;
    }

    private static async Task RunKeyboardAndMinimumLayoutAsync(MainWindow owner)
    {
        var originalWidth = owner.Width;
        var originalHeight = owner.Height;
        owner.Width = owner.MinWidth;
        owner.Height = owner.MinHeight;
        await YieldRenderAsync();
        owner.RunUiSmoke();
        if (owner.ActualWidth < owner.MinWidth - 1 || owner.ActualHeight < owner.MinHeight - 1)
            throw new InvalidOperationException("Main window did not honor its minimum layout dimensions.");

        var assistant = owner.SelectFeatureForAutomation("assistant");
        UiVisualTestHelper.MoveFocusAndRequire(owner, "Navigation.assistant", "Navigation.audio");
        UiVisualTestHelper.MoveFocusAndRequire(assistant, "Assistant.Source", "Assistant.Question");
        UiVisualTestHelper.MoveFocusAndRequire(assistant, "Assistant.Question", "Assistant.AddContext");
        UiVisualTestHelper.MoveFocusAndRequire(assistant, "Assistant.AddContext", "Assistant.Ask");

        owner.Width = originalWidth;
        owner.Height = originalHeight;
        await YieldRenderAsync();
    }

    private async Task RunSettingsRoundTripAsync(MainWindow owner)
    {
        const string endpoint = "http://127.0.0.1:54321/v1";
        const string secret = "ui-smoke-secret-not-for-json";
        const string cliPath = @"D:\AI Tools\LM Studio CLI\lms.exe";
        const string applicationPath = @"E:\Portable Apps\LM Studio\LM Studio.exe";
        var providers = owner.SelectFeatureForAutomation("providers");
        _workspace.Settings.LocalAiAdvancedMode = true;
        await YieldRenderAsync();
        var endpointBox = UiVisualTestHelper.Find<System.Windows.Controls.TextBox>(providers, "Providers.LocalEndpoint");
        endpointBox.Text = "not-a-valid-absolute-uri";
        endpointBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        UiVisualTestHelper.Click(providers, "Providers.CheckEndpoint");
        await YieldRenderAsync();
        if (!string.Equals(_ui.PipelineStatus, "Provider: некорректный URI", StringComparison.Ordinal))
            throw new InvalidOperationException("Provider validation error state was not produced.");

        endpointBox.Text = endpoint;
        endpointBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        var cliPathBox = UiVisualTestHelper.Find<System.Windows.Controls.TextBox>(providers, "Providers.LmStudioCliPath");
        cliPathBox.Text = cliPath;
        cliPathBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        var applicationPathBox = UiVisualTestHelper.Find<System.Windows.Controls.TextBox>(providers, "Providers.LmStudioApplicationPath");
        applicationPathBox.Text = applicationPath;
        applicationPathBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        _workspace.ApiKey = secret;
        _workspace.Settings.StartWithWindows = false;
        _workspace.Settings.WatchGta = false;

        var saved = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSaved(object? sender, AppSettings value) => saved.TrySetResult(value);
        _settingsSave.SettingsSaved += OnSaved;
        try
        {
            UiVisualTestHelper.Click(providers, "Providers.Save");
            AppSettings value;
            try
            {
                value = await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Settings save did not complete. Status: {_ui.PipelineStatus}", ex);
            }
            if (!string.Equals(value.Endpoint, endpoint, StringComparison.Ordinal) ||
                !string.Equals(_settings.Current.Endpoint, endpoint, StringComparison.Ordinal) ||
                !string.Equals(value.LmStudioCliPath, cliPath, StringComparison.Ordinal) ||
                !string.Equals(value.LmStudioApplicationPath, applicationPath, StringComparison.Ordinal))
                throw new InvalidOperationException("Settings UI round-trip did not persist the endpoint and executable paths.");
        }
        finally
        {
            _settingsSave.SettingsSaved -= OnSaved;
        }

        var settingsJson = await File.ReadAllTextAsync(Path.Combine(AppPaths.DataDirectory, "settings.json"));
        if (!settingsJson.Contains(endpoint, StringComparison.Ordinal) ||
            !settingsJson.Contains("LM Studio CLI", StringComparison.Ordinal) ||
            !settingsJson.Contains("Portable Apps", StringComparison.Ordinal) ||
            settingsJson.Contains(secret, StringComparison.Ordinal))
            throw new InvalidOperationException("Settings JSON persistence or secret isolation contract failed.");
        if (!string.Equals(await _secrets.GetAsync("chat-provider-api-key", CancellationToken.None), secret, StringComparison.Ordinal))
            throw new InvalidOperationException("DPAPI secret round-trip failed.");

        _workspace.Settings.LocalAiAdvancedMode = false;
        _workspace.Settings.LmStudioCliPath = "";
        _workspace.Settings.LmStudioApplicationPath = "";
        await _settingsSave.SaveAsync();
        await YieldRenderAsync();
        owner.SelectFeatureForAutomation("assistant");
    }

    private static void RunVisionDialog(Window owner, bool confirm, string? outputDirectory, ICollection<string> paths)
    {
        var preview = new VisionPreviewWindow(Convert.FromBase64String(TestPngBase64), "локальный тестовый endpoint")
        {
            Owner = owner,
        };

        preview.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            UiVisualTestHelper.ValidateAutomationContract(
                preview,
                "Dialog.VisionPreview",
                "Vision.ConsentCard",
                "Vision.Destination",
                "Vision.PreviewImage",
                "Vision.Cancel",
                "Vision.Confirm");
            if (UiVisualTestHelper.Find<System.Windows.Controls.Image>(preview, "Vision.PreviewImage").Source is null)
                throw new InvalidOperationException("Vision preview image was not decoded.");
            var destination = UiVisualTestHelper.Find<TextBlock>(preview, "Vision.Destination");
            var cancelButton = UiVisualTestHelper.Find<System.Windows.Controls.Button>(preview, "Vision.Cancel");
            var confirmButton = UiVisualTestHelper.Find<System.Windows.Controls.Button>(preview, "Vision.Confirm");
            if (string.IsNullOrWhiteSpace(destination.Text))
                throw new InvalidOperationException("Vision consent card does not show the destination.");
            if (!cancelButton.IsCancel || !confirmButton.IsDefault)
                throw new InvalidOperationException("Vision consent keyboard contract requires Esc=Cancel and Enter=Confirm.");
            if (!confirm) CaptureIfRequested(preview, outputDirectory, "vision-preview.png", paths);
            UiVisualTestHelper.Click(preview, confirm ? "Vision.Confirm" : "Vision.Cancel");
        }));

        var result = preview.ShowDialog();
        if (result != confirm)
            throw new InvalidOperationException($"Vision {(confirm ? "confirm" : "cancel")} result contract failed.");
    }

    private static void CaptureIfRequested(FrameworkElement visual, string? outputDirectory, string fileName, ICollection<string> paths)
    {
        if (outputDirectory is null) return;
        var path = Path.Combine(outputDirectory, fileName);
        UiVisualTestHelper.Capture(visual, path);
        paths.Add(path);
    }

    private static async Task YieldRenderAsync() =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

    private static async Task ExecuteAndWaitAsync(System.Windows.Input.ICommand command, TimeSpan timeout)
    {
        if (!command.CanExecute(null)) throw new InvalidOperationException("The requested UI command is already busy or unavailable.");
        command.Execute(null);
        var timer = Stopwatch.StartNew();
        do
        {
            await Task.Delay(50);
            if (command.CanExecute(null)) return;
        }
        while (timer.Elapsed < timeout);
        throw new TimeoutException($"UI command did not complete in {timeout.TotalSeconds:0} seconds.");
    }
}
