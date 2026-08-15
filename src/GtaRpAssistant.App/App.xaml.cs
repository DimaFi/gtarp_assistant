using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using GtaRpAssistant.Knowledge;
using GtaRpAssistant.LocalData;
using GtaRpAssistant.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;
using Forms = System.Windows.Forms;

namespace GtaRpAssistant.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private MainWindow? _main;
    private ServiceProvider? _services;
    private bool _isExiting;
    private bool _servicesDisposed;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        WindowsAppIdentity.Apply();
        // Hidden WPF windows can expose only their most recently invalidated regions to
        // PrintWindow when hardware composition is enabled.  UI automation needs a
        // complete, deterministic backing surface; production rendering stays untouched.
        if (string.Equals(Environment.GetEnvironmentVariable("GTA_RP_AUTOMATION_MODE"), "1", StringComparison.Ordinal))
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        base.OnStartup(e);
        try
        {
            _services = ConfigureServices().BuildServiceProvider();
            _main = _services.GetRequiredService<MainWindow>();
            _main.Closing += (_, args) => { if (!_isExiting) { args.Cancel = true; _main.Hide(); } };
            _main.Show();
            ConfigureTray();
            if (e.Args.Contains("--capture-ui", StringComparer.Ordinal)) ScheduleSnapshotCapture(GetArgumentValue(e.Args, "--capture-feature"));
            else if (e.Args.Contains("--local-ai-e2e", StringComparer.Ordinal)) ScheduleLocalAiE2eShutdown(GetArgumentValue(e.Args, "--phase") ?? "configure");
            else if (e.Args.Contains("--smoke-test", StringComparer.Ordinal)) ScheduleSmokeShutdown();
        }
        catch (Exception ex)
        {
            HandleFatalError("startup-error.txt", "Приложение не смогло завершить запуск.", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleFatalError("fatal-error.txt", "В приложении произошла непредвиденная ошибка.", e.Exception);
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            TryWriteFatalReport("fatal-error.txt", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryWriteFatalReport("background-error.txt", e.Exception);
        e.SetObserved();
    }

    private void HandleFatalError(string reportName, string message, Exception exception)
    {
        var reportPath = TryWriteFatalReport(reportName, exception);
        if (!string.Equals(Environment.GetEnvironmentVariable("GTA_RP_AUTOMATION_MODE"), "1", StringComparison.Ordinal))
        {
            var details = reportPath is null
                ? "Не удалось сохранить диагностический отчёт."
                : $"Диагностический отчёт сохранён:\n{reportPath}";
            System.Windows.MessageBox.Show($"{message}\n\n{details}", "GTA RP Assistant", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _isExiting = true;
        _servicesDisposed = true;
        _tray?.Dispose();
        Environment.Exit(1);
    }

    private static string? TryWriteFatalReport(string reportName, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var path = Path.Combine(AppPaths.DataDirectory, reportName);
            File.WriteAllText(path, $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}");
            return path;
        }
        catch (Exception reportError) when (reportError is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(ApplicationExecutionMode.FromEnvironment());
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new RollingFileLoggerProvider(Path.Combine(AppPaths.DataDirectory, "logs")));
        });
        services.AddSingleton(new SettingsService(AppPaths.DataDirectory));
        services.AddSingleton<ISecretStore>(_ => new DpapiSecretStore(Path.Combine(AppPaths.DataDirectory, "secrets")));
        services.AddSingleton<ChatProviderCatalog>();
        services.AddSingleton<IChatProviderCatalog>(sp => sp.GetRequiredService<ChatProviderCatalog>());
        services.AddSingleton(new SqliteKnowledgeRepository($"Data Source={Path.Combine(AppPaths.DataDirectory, "knowledge.db")}"));
        services.AddSingleton<IKnowledgeRepository>(sp => sp.GetRequiredService<SqliteKnowledgeRepository>());
        services.AddSingleton(new TranscriptBuffer(TimeSpan.FromMinutes(3)));
        services.AddSingleton(new RuleBasedIntentDetector(LoadGameTerms()));
        services.AddSingleton<IIntentDetector>(sp => sp.GetRequiredService<RuleBasedIntentDetector>());
        services.AddSingleton<IContextSelector, ContextSelector>();
        services.AddSingleton<IAssistantContextBuilder, AssistantContextBuilder>();
        services.AddSingleton<IAssistantSessionContextStore, InMemoryAssistantSessionContextStore>();
        services.AddSingleton<IAiRouter, AiRouter>();
        services.AddSingleton<GroundedAnswerValidator>();
        services.AddSingleton<ILocalAiCapabilityTester, LocalAiCapabilityTester>();
        services.AddSingleton<InMemoryAssistantConversationStore>();
        services.AddSingleton<IUserMemoryStore>(_ => new SqliteUserMemoryStore(
            $"Data Source={Path.Combine(AppPaths.DataDirectory, "user-memory.db")}"));
        services.AddSingleton<IUserPersonalizationContextProvider, UserPersonalizationContextProvider>();
        services.AddSingleton<InMemoryAnswerCache>();
        services.AddSingleton<IAnswerCache>(sp => new ConfigurableAnswerCache(
            () => sp.GetRequiredService<SettingsService>().Current.EnableLongTermConversation,
            sp.GetRequiredService<InMemoryAnswerCache>(),
            () => new SqliteAnswerCache($"Data Source={Path.Combine(AppPaths.DataDirectory, "assistant-data.db")}")));
        services.AddSingleton<IAssistantConversationStore>(sp => new ConfigurableAssistantConversationStore(
            () => sp.GetRequiredService<SettingsService>().Current.EnableLongTermConversation,
            sp.GetRequiredService<InMemoryAssistantConversationStore>(),
            () => new SqliteAssistantConversationStore(
                $"Data Source={Path.Combine(AppPaths.DataDirectory, "assistant-data.db")}")));
        services.AddSingleton<ILocalAiPathSettings, WorkspaceLocalAiPathSettings>();
        services.AddSingleton<ILocalAiEngineAdapter, LmStudioEngineAdapter>();
        services.AddSingleton<ILocalAiEngineManager, LocalAiEngineManager>();
        services.AddSingleton<ILocalAiBootstrapInstaller, LmStudioBootstrapInstaller>();
        services.AddSingleton<ITranscriptDeduplicator, TranscriptDeduplicator>();
        services.AddSingleton<IProactivePolicy, ProactivePolicy>();
        services.AddSingleton<ISessionEventSink, SessionEventLogger>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<ExpandedOverlayWindow>();
        services.AddSingleton<OverlayService>();
        services.AddSingleton<IOverlayService>(sp => sp.GetRequiredService<OverlayService>());
        services.AddSingleton<MicroModelOverlayCoordinator>();
        services.AddSingleton<AssistantSessionCoordinator>();
        services.AddSingleton<IGameProcessDetector, GameProcessDetector>();
        services.AddSingleton(sp => new GameSessionMonitor(sp.GetRequiredService<IGameProcessDetector>(), new("gta5rp", "GTA 5 RP", ["GTA5", "ragemp_v", "ragemp"], ["Grand Theft Auto V", "RAGE Multiplayer"])));
        services.AddSingleton<PerformanceController>();
        services.AddSingleton<IMicroModelResourceGuard, MicroModelResourceGuard>();
        services.AddSingleton(MicroModelManagerOptions.CreateDefault(AppContext.BaseDirectory));
        services.AddSingleton<IMicroModelManager, MicroModelManager>();
        services.AddSingleton(sp => new ProcessPerformanceMonitor(sp.GetRequiredService<PerformanceController>(), () => SettingValues.Performance(sp.GetRequiredService<SettingsService>().Current)));
        services.AddSingleton<WindowsStartupService>();
        services.AddSingleton<WindowCaptureService>();
        services.AddSingleton<IScreenFrameDiffer, GridScreenFrameDiffer>();
        services.AddSingleton<ILocalScreenOcr, TesseractScreenOcr>();
        services.AddSingleton<IScreenContextStore, ScreenContextStore>();
        services.AddSingleton<ScreenContextController>();
        services.AddSingleton<ITextToSpeechService, WindowsTextToSpeechService>();
        services.AddSingleton<VisionWorkflowService>();
        services.AddSingleton<MicrophoneTestService>();
        services.AddSingleton<VoiceInteractionStateMachine>();
        services.AddSingleton<VoiceInteractionCoordinator>();
        services.AddSingleton(sp => new EmbeddedSttPackLocator(
            () => sp.GetRequiredService<SettingsService>().Current.EmbeddedSttPackPath,
            Path.Combine(AppPaths.DataDirectory, "model-packs", "stt"),
            Path.Combine(AppContext.BaseDirectory, "model-packs", "stt")));
        services.AddSingleton<WhisperCppSpeechToTextProvider>();
        services.AddSingleton<WindowsSpeechRecognitionProvider>();
        services.AddSingleton<ISpeechToTextProviderCatalog>(sp => new SpeechToTextProviderCatalog(
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<WhisperCppSpeechToTextProvider>(),
            sp.GetRequiredService<WindowsSpeechRecognitionProvider>()));
        services.AddSingleton<AudioSessionController>();
        services.AddSingleton<IAppDialogService, AppDialogService>();
        services.AddSingleton<ILocalModelFileDiscovery, LocalModelFileDiscovery>();
        services.AddSingleton(sp => new KnowledgeCatalogService(
            sp.GetRequiredService<SqliteKnowledgeRepository>(),
            sp.GetRequiredService<ILogger<KnowledgeCatalogService>>(),
            AppPaths.DataDirectory));
        services.AddSingleton<SettingsApplicationService>();
        services.AddSingleton<UiAutomationScenarioService>();
        services.AddFeatureModules();
        services.AddSingleton<ApplicationLifecycleCoordinator>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }

    private static IReadOnlyList<string> LoadGameTerms()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp", "dictionaries", "game-terms.json");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.GetProperty("terms").EnumerateArray().Select(x => x.GetString()).OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return ["gta", "сервер", "контракт", "семья"];
        }
    }

    private void ConfigureTray()
    {
        var executableIcon = Environment.ProcessPath is { } executablePath
            ? Icon.ExtractAssociatedIcon(executablePath)
            : null;
        _tray = new Forms.NotifyIcon { Icon = executableIcon ?? SystemIcons.Application, Text = "GTA RP Assistant", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        foreach (var definition in TrayCommandCatalog.Definitions)
        {
            var item = menu.Items.Add(definition.Label);
            item.Tag = definition.Command;
            item.Click += async (_, _) => await ExecuteTrayCommandAsync(definition.Command);
        }
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
    }

    private async Task ExecuteTrayCommandAsync(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Open:
                Dispatcher.Invoke(ShowMain);
                break;
            case TrayCommand.TogglePause:
                if (_main is not null) await _main.TogglePauseAsync();
                break;
            case TrayCommand.Exit:
                _isExiting = true;
                await DisposeServicesAsync();
                Dispatcher.Invoke(() => { _main?.Close(); Shutdown(); });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void ValidateTrayContract()
    {
        var items = _tray?.ContextMenuStrip?.Items.Cast<Forms.ToolStripItem>().ToArray()
            ?? throw new InvalidOperationException("Tray menu is unavailable.");
        if (items.Length != TrayCommandCatalog.Definitions.Count)
            throw new InvalidOperationException("Tray menu command count does not match the catalog.");
        for (var index = 0; index < items.Length; index++)
        {
            var expected = TrayCommandCatalog.Definitions[index];
            if (!Equals(items[index].Tag, expected.Command) || !string.Equals(items[index].Text, expected.Label, StringComparison.Ordinal))
                throw new InvalidOperationException($"Tray command '{expected.Command}' is configured incorrectly.");
        }
    }

    private void ShowMain()
    {
        _main?.Show();
        _main?.Activate();
    }

    private void ScheduleSmokeShutdown()
    {
        var progressPath = Path.Combine(AppPaths.DataDirectory, "smoke-progress.log");
        Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(progressPath, "scheduled" + Environment.NewLine);
        void Mark(string stage) => File.AppendAllText(progressPath, stage + Environment.NewLine);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try
            {
                Mark("ui-smoke:start");
                _main?.RunUiSmoke();
                Mark("ui-smoke:complete");
                ValidateTrayContract();
                Mark("tray:complete");
                if (_main is not null && _services is not null)
                    await _services.GetRequiredService<UiAutomationScenarioService>().RunAsync(_main);
                Mark("scenarios:complete");
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                await File.WriteAllTextAsync(Path.Combine(AppPaths.DataDirectory, "smoke-error.txt"), ex.ToString());
                Environment.ExitCode = 1;
            }
            _isExiting = true;
            Mark("dispose:start");
            await DisposeServicesAsync();
            Mark("dispose:complete");
            _main?.Close();
            Mark("window:closed");
            Shutdown();
        };
        timer.Start();
    }

    private void ScheduleLocalAiE2eShutdown(string phase)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("GTA_RP_LOCAL_AI_E2E_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("GTA_RP_LOCAL_AI_E2E_DIR is required for --local-ai-e2e.");
        var modelKey = Environment.GetEnvironmentVariable("GTA_RP_LOCAL_AI_MODEL") ?? "qwen/qwen3-vl-4b";
        Directory.CreateDirectory(outputDirectory);
        var errorReport = Path.Combine(outputDirectory, $"{phase}-error.txt");
        if (File.Exists(errorReport)) File.Delete(errorReport);
        var startedAt = DateTime.UtcNow;
        var running = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) =>
        {
            if (running) return;
            if (_main?.IsApplicationInitialized != true)
            {
                if (DateTime.UtcNow - startedAt <= TimeSpan.FromSeconds(30)) return;
                timer.Stop();
                await File.WriteAllTextAsync(errorReport, "Application initialization timed out.");
                Environment.ExitCode = 1;
            }
            else
            {
                running = true;
                timer.Stop();
                try
                {
                    if (_services is null) throw new InvalidOperationException("Application services are unavailable.");
                    await _services.GetRequiredService<UiAutomationScenarioService>().RunLocalAiE2eAsync(_main, phase, modelKey, outputDirectory);
                }
                catch (Exception ex)
                {
                    await File.WriteAllTextAsync(errorReport, ex.ToString());
                    Environment.ExitCode = 1;
                }
            }
            _isExiting = true;
            await DisposeServicesAsync();
            _main?.Close();
            Shutdown();
        };
        timer.Start();
    }

    private void ScheduleSnapshotCapture(string? featureId)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("GTA_RP_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("GTA_RP_UI_SNAPSHOT_DIR is required for --capture-ui.");

        var errorReport = Path.Combine(outputDirectory, "capture-error.txt");
        if (File.Exists(errorReport)) File.Delete(errorReport);

        var startedAt = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (_main?.IsApplicationInitialized != true)
                {
                    if (DateTime.UtcNow - startedAt <= TimeSpan.FromSeconds(20)) return;
                    throw new TimeoutException("UI snapshot capture timed out while waiting for initialization.");
                }

                timer.Stop();
                if (featureId is not null)
                {
                    _main.CaptureFeatureSnapshot(outputDirectory, featureId);
                }
                else
                {
                    var paths = _main.CaptureFeatureSnapshots(outputDirectory).ToList();
                    if (_services is null) throw new InvalidOperationException("Application services are unavailable.");
                    paths.AddRange(await _services.GetRequiredService<UiAutomationScenarioService>().RunAsync(_main, outputDirectory));
                    if (paths.Count < 10) throw new InvalidOperationException("UI snapshot capture produced an incomplete set.");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Directory.CreateDirectory(outputDirectory);
                await File.WriteAllTextAsync(errorReport, ex.ToString());
                Environment.ExitCode = 1;
            }
            _isExiting = true;
            await DisposeServicesAsync();
            _main?.Close();
            Shutdown();
        };
        timer.Start();
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
            if (string.Equals(args[index], name, StringComparison.Ordinal)) return args[index + 1];
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (!_servicesDisposed && _services is not null)
        {
            Task.Run(async () => await _services.DisposeAsync()).GetAwaiter().GetResult();
            _servicesDisposed = true;
        }
        base.OnExit(e);
    }

    private async Task DisposeServicesAsync()
    {
        if (_servicesDisposed || _services is null) return;
        _servicesDisposed = true;
        await _services.DisposeAsync();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _isExiting = true;
        base.OnSessionEnding(e);
    }
}
