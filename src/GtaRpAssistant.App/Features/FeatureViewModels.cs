using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;
using GtaRpAssistant.Core;
using GtaRpAssistant.Providers;

namespace GtaRpAssistant.App.Features;

public abstract class FeatureViewModel : ObservableObject
{
    protected FeatureViewModel(ApplicationUiState ui, SettingsWorkspace workspace)
    {
        Ui = ui;
        Workspace = workspace;
        ui.PropertyChanged += RelayPropertyChanged;
        workspace.PropertyChanged += RelayPropertyChanged;
    }

    protected ApplicationUiState Ui { get; }
    protected SettingsWorkspace Workspace { get; }
    private void RelayPropertyChanged(object? sender, PropertyChangedEventArgs e) => Raise(e.PropertyName);
}

public sealed class ProvidersFeatureViewModel : FeatureViewModel
{
    private readonly ILocalAiCapabilityTester _capabilityTester;
    private readonly ILocalAiEngineManager _engineManager;
    private readonly SettingsSaveCoordinator _save;
    private readonly IAppDialogService _dialogs;
    private readonly ILocalModelFileDiscovery _modelFiles;
    private readonly IUiDispatcher _dispatcher;
    private LocalAiEngineSnapshot? _snapshot;
    private LocalAiRecommendedModel? _selectedRecommendedModel;
    private CancellationTokenSource? _operation;
    private string _capabilityStatus = "Тест совместимости ещё не запускался.";
    private string _engineStatus = "Проверка LM Studio…";
    private string _apiStatus = "API: проверяется";
    private string _modelStatus = "Модель: проверяется";
    private string _resourceForecast = "Прогноз ресурсов появится после выбора модели.";
    private string _setupProgress = "Можно работать без локальной модели: база знаний уже доступна.";
    private string _installationPaths = "Пути LM Studio определяются автоматически.";
    private string _pendingModelKey = "";

    public ProvidersFeatureViewModel(
        ApplicationUiState ui,
        SettingsWorkspace workspace,
        SettingsSaveCoordinator save,
        ILocalAiCapabilityTester capabilityTester,
        ILocalAiEngineManager engineManager,
        IAppDialogService dialogs,
        ILocalModelFileDiscovery modelFiles,
        IUiDispatcher dispatcher,
        ApplicationExecutionMode executionMode) : base(ui, workspace)
    {
        _capabilityTester = capabilityTester;
        _engineManager = engineManager;
        _save = save;
        _dialogs = dialogs;
        _modelFiles = modelFiles;
        _dispatcher = dispatcher;
        SaveSettingsCommand = save.SaveCommand;
        CheckEndpointCommand = new AsyncRelayCommand(CheckEndpointAsync);
        DiscoverModelsCommand = new AsyncRelayCommand(DiscoverModelsAsync);
        TestCapabilityCommand = new AsyncRelayCommand(TestCapabilityAsync);
        RefreshLocalAiCommand = new AsyncRelayCommand(RefreshLocalAiAsync);
        AutoConfigureCommand = new AsyncRelayCommand(AutoConfigureAsync);
        StartServerCommand = new AsyncRelayCommand(StartServerAsync);
        DownloadModelCommand = new AsyncRelayCommand(DownloadSelectedAsync);
        LoadModelCommand = new AsyncRelayCommand(LoadSelectedAsync);
        UnloadModelCommand = new AsyncRelayCommand(UnloadAsync);
        EstimateResourcesCommand = new AsyncRelayCommand(EstimateSelectedAsync);
        CancelLocalAiCommand = new RelayCommand(CancelOperation);
        InstallLmStudioCommand = new RelayCommand(OpenLmStudioDownload);
        ShowLocalAiHelpCommand = new RelayCommand(ShowHelp);
        BrowseLmStudioCliCommand = new RelayCommand(BrowseLmStudioCli);
        BrowseLmStudioApplicationCommand = new RelayCommand(BrowseLmStudioApplication);
        ClearLocalAiPathsCommand = new RelayCommand(ClearLocalAiPaths);
        ImportGgufCommand = new AsyncRelayCommand(ImportGgufAsync);
        FindGgufInFolderCommand = new AsyncRelayCommand(FindGgufInFolderAsync);
        RecommendedModels = new(LocalAiRecommendedModelCatalog.Models);
        _selectedRecommendedModel = RecommendedModels.FirstOrDefault(x => string.Equals(x.ModelKey, Settings.Model, StringComparison.OrdinalIgnoreCase))
            ?? RecommendedModels.FirstOrDefault(x => x.Profile == LocalAiPerformanceProfile.Balanced)
            ?? RecommendedModels.First();
        _pendingModelKey = string.IsNullOrWhiteSpace(Settings.Model) ? _selectedRecommendedModel.ModelKey : Settings.Model.Trim();
        workspace.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(SettingsWorkspace.Settings), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(Settings.Model))
                PendingModelKey = Settings.Model;
        };
        if (!executionMode.IsAutomation) _ = RefreshLocalAiAsync();
    }

    public SettingsEditor Settings => Workspace.Settings;
    public string ApiKey { get => Workspace.ApiKey; set => Workspace.ApiKey = value; }
    public string CloudApiKey { get => Workspace.CloudApiKey; set => Workspace.CloudApiKey = value; }
    public string PipelineStatus => Ui.PipelineStatus;
    public ObservableCollection<string> AvailableModels { get; } = [];
    public ObservableCollection<LocalAiModelDescriptor> InstalledModels { get; } = [];
    public ObservableCollection<LocalAiRecommendedModel> RecommendedModels { get; }
    public LocalAiRecommendedModel? SelectedRecommendedModel { get => _selectedRecommendedModel; set { if (Set(ref _selectedRecommendedModel, value) && value is not null) PendingModelKey = value.ModelKey; } }
    public string PendingModelKey { get => _pendingModelKey; set => Set(ref _pendingModelKey, value?.Trim() ?? ""); }
    public string CapabilityStatus { get => _capabilityStatus; private set => Set(ref _capabilityStatus, value); }
    public string EngineStatus { get => _engineStatus; private set => Set(ref _engineStatus, value); }
    public string ApiStatus { get => _apiStatus; private set => Set(ref _apiStatus, value); }
    public string ModelStatus { get => _modelStatus; private set => Set(ref _modelStatus, value); }
    public string ResourceForecast { get => _resourceForecast; private set => Set(ref _resourceForecast, value); }
    public string SetupProgress { get => _setupProgress; private set => Set(ref _setupProgress, value); }
    public string InstallationPaths { get => _installationPaths; private set => Set(ref _installationPaths, value); }
    public string RagStatus => Ui.TotalArticleCount > 0 ? $"RAG: готово · {Ui.TotalArticleCount} статей" : "RAG: инициализация";
    public string WhisperStatus => Settings.SttProviderMode == (int)ProviderSelectionMode.Disabled ? "Whisper/STT: выключен" : "Whisper/STT: настроен маршрутом";
    public string VisionStatus => Settings.VisionEnabled ? "Vision: разрешён после preview" : "Vision: выключен";
    public ICommand CheckEndpointCommand { get; }
    public ICommand DiscoverModelsCommand { get; }
    public ICommand TestCapabilityCommand { get; }
    public ICommand RefreshLocalAiCommand { get; }
    public ICommand AutoConfigureCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand DownloadModelCommand { get; }
    public ICommand LoadModelCommand { get; }
    public ICommand UnloadModelCommand { get; }
    public ICommand EstimateResourcesCommand { get; }
    public ICommand CancelLocalAiCommand { get; }
    public ICommand InstallLmStudioCommand { get; }
    public ICommand ShowLocalAiHelpCommand { get; }
    public ICommand BrowseLmStudioCliCommand { get; }
    public ICommand BrowseLmStudioApplicationCommand { get; }
    public ICommand ClearLocalAiPathsCommand { get; }
    public ICommand ImportGgufCommand { get; }
    public ICommand FindGgufInFolderCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    private LocalAiEngineKind Engine => Enum.IsDefined(typeof(LocalAiEngineKind), Settings.LocalAiEngine) ? (LocalAiEngineKind)Settings.LocalAiEngine : LocalAiEngineKind.LmStudio;
    private Uri Endpoint()
    {
        if (!Uri.TryCreate(Settings.Endpoint, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback)
            throw new InvalidOperationException("Локальный endpoint должен быть loopback URI, например http://127.0.0.1:1234/v1.");
        return endpoint;
    }

    private OpenAiCompatibleChatProvider CreateProvider(HttpClient client, TimeSpan timeout, string? modelId = null)
    {
        if (!Uri.TryCreate(Settings.Endpoint, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException("Укажите корректный URI локального endpoint.");
        if (!endpoint.IsLoopback)
            throw new InvalidOperationException("Локальная модель должна быть доступна только через loopback.");
        var selectedModel = string.IsNullOrWhiteSpace(modelId) ? Settings.Model : modelId;
        if (string.IsNullOrWhiteSpace(selectedModel)) throw new InvalidOperationException("Выберите chat-модель.");
        return new(client, new(endpoint, selectedModel, string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            timeout, true, "lm-studio", ProviderKind.LmStudio));
    }

    private async Task CheckEndpointAsync()
    {
        try
        {
            if (!Uri.TryCreate(Settings.Endpoint, UriKind.Absolute, out _))
            {
                Ui.PipelineStatus = "Provider: некорректный URI";
                return;
            }
            using var client = new HttpClient();
            var provider = CreateProvider(client, TimeSpan.FromSeconds(5), PendingModelKey);
            Ui.PipelineStatus = $"Provider: {(await provider.CheckHealthAsync(CancellationToken.None)).Message}";
        }
        catch (Exception ex)
        {
            Ui.PipelineStatus = $"Provider: {ex.Message}";
        }
    }

    private async Task DiscoverModelsAsync()
    {
        try
        {
            using var client = new HttpClient();
            var provider = CreateProvider(client, TimeSpan.FromSeconds(8), PendingModelKey);
            var models = await provider.GetModelsAsync(CancellationToken.None);
            AvailableModels.Clear();
            foreach (var model in models.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                AvailableModels.Add(model.Id);
            if (models.Count == 1) PendingModelKey = models[0].Id;
            Ui.PipelineStatus = models.Count == 0
                ? "Модели не найдены. Загрузите модель в LM Studio или llama-server."
                : $"Найдено моделей: {models.Count}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Ui.PipelineStatus = $"Не удалось получить модели: {ex.Message}";
        }
    }

    private async Task TestCapabilityAsync()
    {
        var operation = BeginOperation();
        await TestCapabilityInternalAsync(operation.Token, PendingModelKey);
    }

    private async Task<bool> TestCapabilityInternalAsync(CancellationToken cancellationToken, string? modelId = null)
    {
        CapabilityStatus = "Проверяю русский язык, JSON, grounding, abstain и follow-up…";
        try
        {
            using var client = new HttpClient();
            var profile = Enum.IsDefined(typeof(LocalAiPerformanceProfile), Settings.LocalAiPerformanceProfile)
                ? (LocalAiPerformanceProfile)Settings.LocalAiPerformanceProfile
                : LocalAiPerformanceProfile.Balanced;
            var provider = CreateProvider(client, LocalAiGenerationSettings.For(profile).Timeout, modelId);
            var report = await _capabilityTester.TestAsync(provider, cancellationToken);
            var warnings = report.Warnings.Count == 0 ? "" : $" Предупреждения: {string.Join("; ", report.Warnings)}";
            CapabilityStatus = $"{report.Recommendation}. Средняя задержка: {report.AverageLatency.TotalSeconds:F1} с.{warnings}";
            Ui.PipelineStatus = report.IsCompatible
                ? "Модель прошла capability-test."
                : "Модель не прошла все проверки; используйте её только осознанно.";
            return report.IsCompatible;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { CapabilityStatus = "Capability-test отменён."; return false; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CapabilityStatus = $"Тест не выполнен: {ex.Message}";
            return false;
        }
    }

    private async Task RefreshLocalAiAsync()
    {
        try
        {
            var snapshot = await _engineManager.InspectAsync(Engine, Endpoint(), CancellationToken.None);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EngineStatus = "✕ Не удалось проверить Local AI";
            ApiStatus = "API: недоступен";
            SetupProgress = ex.Message;
        }
    }

    private void ApplySnapshot(LocalAiEngineSnapshot snapshot)
    {
        _snapshot = snapshot;
        EngineStatus = snapshot.IsInstalled ? $"✓ {snapshot.DisplayName} установлен" : $"✕ {snapshot.DisplayName} не обнаружен";
        ApiStatus = snapshot.ApiAvailable ? "✓ Локальный API запущен" : "✕ Локальный API остановлен";
        var chatModels = snapshot.Models.Where(x => x.IsChatModel).OrderBy(x => x.DisplayName).ToArray();
        ModelStatus = snapshot.ActiveModelKey is not null ? $"✓ В памяти: {snapshot.ActiveModelKey}"
            : chatModels.Length > 0 ? $"Доступно chat-моделей: {chatModels.Length}" : "Chat-модель не найдена";
        SetupProgress = snapshot.Message;
        InstallationPaths = $"CLI: {snapshot.CliPath ?? "не найден"}\nLM Studio: {snapshot.ApplicationPath ?? "не найден"}";
        AvailableModels.Clear();
        InstalledModels.Clear();
        foreach (var model in chatModels)
        {
            InstalledModels.Add(model);
            AvailableModels.Add(model.Key);
        }
        if (string.IsNullOrWhiteSpace(PendingModelKey))
            PendingModelKey = chatModels.FirstOrDefault(x => string.Equals(x.Key, Settings.Model, StringComparison.OrdinalIgnoreCase))?.Key
                ?? chatModels.FirstOrDefault(x => x.IsLoaded)?.Key
                ?? chatModels.FirstOrDefault()?.Key
                ?? "";
        Raise(nameof(RagStatus)); Raise(nameof(WhisperStatus)); Raise(nameof(VisionStatus));
    }

    private void BrowseLmStudioCli()
    {
        var selected = _dialogs.PickExecutable("Выберите lms.exe", Settings.LmStudioCliPath);
        if (selected is null) return;
        Settings.LmStudioCliPath = selected;
        SetupProgress = "Путь к lms.exe выбран. Сохраните настройки или обновите статус.";
    }

    private void BrowseLmStudioApplication()
    {
        var selected = _dialogs.PickExecutable("Выберите LM Studio.exe", Settings.LmStudioApplicationPath);
        if (selected is null) return;
        Settings.LmStudioApplicationPath = selected;
        SetupProgress = "Путь к LM Studio.exe выбран. Сохраните настройки или обновите статус.";
    }

    private void ClearLocalAiPaths()
    {
        Settings.LmStudioCliPath = "";
        Settings.LmStudioApplicationPath = "";
        InstallationPaths = "Пути LM Studio определяются автоматически.";
        SetupProgress = "Ручные пути очищены. Будет использован автоматический поиск.";
    }

    private async Task AutoConfigureAsync()
    {
        var operation = BeginOperation();
        var ct = operation.Token;
        try
        {
            SetupProgress = "Шаг 1/8 · поиск LM Studio";
            var snapshot = await _engineManager.InspectAsync(Engine, Endpoint(), ct);
            ApplySnapshot(snapshot);
            if (!snapshot.IsInstalled)
            {
                SetupProgress = "LM Studio не найден. Установите его с официального сайта и запустите один раз.";
                return;
            }
            if (!snapshot.ApiAvailable)
            {
                SetupProgress = "Шаг 2/8 · запуск локального API";
                await _engineManager.StartServerAsync(Engine, Endpoint(), ct);
                snapshot = await _engineManager.InspectAsync(Engine, Endpoint(), ct);
                ApplySnapshot(snapshot);
            }

            SetupProgress = "Шаг 3/8 · выбор chat-модели и профиля";
            var installed = snapshot.Models.Where(x => x.IsChatModel).ToArray();
            var existing = installed.FirstOrDefault(x => string.Equals(x.Key, PendingModelKey, StringComparison.OrdinalIgnoreCase))
                ?? installed.FirstOrDefault(x => string.Equals(x.Key, Settings.Model, StringComparison.OrdinalIgnoreCase))
                ?? installed.FirstOrDefault(x => x.IsLoaded)
                ?? (installed.Length == 1 ? installed[0] : null);
            string modelKey;
            LocalAiPerformanceProfile profile;
            var alreadyLoaded = existing?.IsLoaded == true;
            if (existing is not null)
            {
                modelKey = existing.Key;
                PendingModelKey = modelKey;
                profile = Enum.IsDefined(typeof(LocalAiPerformanceProfile), Settings.LocalAiPerformanceProfile)
                    ? (LocalAiPerformanceProfile)Settings.LocalAiPerformanceProfile
                    : LocalAiPerformanceProfile.Balanced;
                SetupProgress = $"Шаг 4/8 · использую установленную модель {existing.DisplayName}";
            }
            else
            {
                var recommendation = LocalAiRecommendedModelCatalog.Recommend(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Settings.VisionEnabled);
                SelectedRecommendedModel = recommendation;
                profile = recommendation.Profile;
                Settings.LocalAiPerformanceProfile = (int)profile;
                modelKey = recommendation.ModelKey;
                SetupProgress = $"Шаг 4/8 · скачивание {recommendation.DisplayName}";
                await DownloadAsync(recommendation, ct);
            }
            var load = CreateLoadRequest(modelKey, profile);
            await EstimateAsync(modelKey, load, ct);

            SetupProgress = alreadyLoaded ? "Шаг 5/8 · модель уже загружена в память" : "Шаг 5/8 · загрузка модели в память";
            if (!alreadyLoaded) await _engineManager.LoadModelAsync(Engine, Endpoint(), load, ct);

            SetupProgress = "Шаг 6/8 · проверка памяти";
            await EstimateAsync(modelKey, load, ct);
            SetupProgress = "Шаг 7/8 · capability-test";
            if (!await TestCapabilityInternalAsync(ct, modelKey))
            {
                SetupProgress = "Модель загружена, но не прошла capability-test. Действующий маршрут не изменён.";
                await RefreshLocalAiAsync();
                return;
            }
            SetupProgress = "Шаг 8/8 · сохранение профиля";
            Settings.Model = modelKey;
            Settings.ChatProviderMode = (int)ProviderSelectionMode.Local;
            await _save.SaveAsync();
            await RefreshLocalAiAsync();
            SetupProgress = "✓ Local AI готов к работе.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { SetupProgress = "Автоматическая настройка отменена."; }
        catch (Exception ex) { SetupProgress = $"Автоматическая настройка остановлена: {ex.Message}"; }
    }

    private async Task StartServerAsync()
    {
        var operation = BeginOperation();
        try { SetupProgress = "Запускаю локальный API…"; await _engineManager.StartServerAsync(Engine, Endpoint(), operation.Token); await RefreshLocalAiAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { SetupProgress = $"Не удалось запустить API: {ex.Message}"; }
    }

    private async Task DownloadSelectedAsync()
    {
        if (SelectedRecommendedModel is null) return;
        var operation = BeginOperation();
        try { await DownloadAsync(SelectedRecommendedModel, operation.Token); await RefreshLocalAiAsync(); }
        catch (OperationCanceledException) { SetupProgress = "Скачивание отменено."; }
        catch (Exception ex) { SetupProgress = $"Скачивание не выполнено: {ex.Message}"; }
    }

    private async Task DownloadAsync(LocalAiRecommendedModel model, CancellationToken cancellationToken)
    {
        var progress = new Progress<LocalAiDownloadProgress>(value => SetupProgress = value.TotalBytes > 0
            ? $"Скачивание {model.DisplayName}: {value.Percent:F1}% · {FormatBytes(value.BytesPerSecond)}/с"
            : $"Скачивание {model.DisplayName}: {value.Status}");
        var result = await _engineManager.DownloadModelAsync(Engine, Endpoint(), model.ModelKey, model.Quantization, progress, cancellationToken);
        if (result.Status == "failed") throw new InvalidOperationException(result.Error ?? "LM Studio сообщил об ошибке скачивания.");
    }

    private async Task LoadSelectedAsync()
    {
        var modelKey = PendingModelKey;
        if (string.IsNullOrWhiteSpace(modelKey)) { SetupProgress = "Сначала выберите установленную chat-модель."; return; }
        var operation = BeginOperation();
        try
        {
            await ActivateModelAsync(modelKey, operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { SetupProgress = "Загрузка модели отменена."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { SetupProgress = $"Модель не загружена: {ex.Message}"; }
    }

    private async Task ImportGgufAsync()
    {
        var path = _dialogs.PickGgufFile("Выберите локальную GGUF-модель", null);
        if (path is null) return;
        await ImportAndActivateAsync(path);
    }

    private async Task FindGgufInFolderAsync()
    {
        var folder = _dialogs.PickFolder("Выберите папку, в которой находится GGUF-модель", null);
        if (folder is null) return;
        var operation = BeginOperation();
        try
        {
            SetupProgress = "Ищу поддерживаемые GGUF-файлы в выбранной папке…";
            var candidates = await _modelFiles.ScanAsync(folder, operation.Token);
            var supported = candidates.Where(x => x.IsSupported).ToArray();
            if (supported.Length == 0)
            {
                var reason = candidates.FirstOrDefault()?.UnsupportedReason;
                SetupProgress = reason is null ? "В выбранной папке GGUF-файлы не найдены." : $"Подходящая модель не найдена: {reason}";
                return;
            }

            string? selected;
            if (supported.Length == 1) selected = supported[0].Path;
            else
            {
                _dialogs.ShowInformation("Найдено несколько моделей", $"Найдено поддерживаемых GGUF: {supported.Length}. Выберите один файл; автоматический импорт всех моделей не выполняется.");
                selected = _dialogs.PickGgufFile("Выберите одну GGUF-модель", folder);
            }
            if (selected is null) return;
            await ImportAndActivateInternalAsync(selected, operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { SetupProgress = "Поиск модели отменён."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { SetupProgress = $"Не удалось подключить модель: {ex.Message}"; }
    }

    private async Task ImportAndActivateAsync(string path)
    {
        var operation = BeginOperation();
        try { await ImportAndActivateInternalAsync(path, operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { SetupProgress = "Импорт модели отменён."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { SetupProgress = $"Импорт не выполнен: {ex.Message}"; }
    }

    private async Task ImportAndActivateInternalAsync(string path, CancellationToken cancellationToken)
    {
        SetupProgress = $"Проверяю и импортирую {Path.GetFileName(path)}. Исходный файл будет сохранён…";
        var imported = await _engineManager.ImportModelAsync(Engine, path, cancellationToken);
        PendingModelKey = imported.Key;
        await RefreshLocalAiAsync();
        SetupProgress = $"Импортирована {imported.DisplayName}. Загружаю и проверяю совместимость…";
        await ActivateModelAsync(imported.Key, cancellationToken);
    }

    private async Task<bool> ActivateModelAsync(string modelKey, CancellationToken cancellationToken)
    {
        var profile = Enum.IsDefined(typeof(LocalAiPerformanceProfile), Settings.LocalAiPerformanceProfile)
            ? (LocalAiPerformanceProfile)Settings.LocalAiPerformanceProfile
            : LocalAiPerformanceProfile.Balanced;
        var request = CreateLoadRequest(modelKey, profile);
        await EstimateAsync(modelKey, request, cancellationToken);
        if (_snapshot?.Models.Any(x => x.IsLoaded && string.Equals(x.Key, modelKey, StringComparison.OrdinalIgnoreCase)) != true)
            await _engineManager.LoadModelAsync(Engine, Endpoint(), request, cancellationToken);
        SetupProgress = "Модель загружена. Проверяю совместимость…";
        if (!await TestCapabilityInternalAsync(cancellationToken, modelKey))
        {
            SetupProgress = "Модель осталась в памяти, но не прошла capability-test. Действующий маршрут не изменён.";
            await RefreshLocalAiAsync();
            return false;
        }
        Settings.Model = modelKey;
        Settings.ChatProviderMode = (int)ProviderSelectionMode.Local;
        await _save.SaveAsync();
        await RefreshLocalAiAsync();
        SetupProgress = $"✓ Модель {modelKey} используется ассистентом.";
        return true;
    }

    private async Task UnloadAsync()
    {
        var instance = _snapshot?.Models.FirstOrDefault(x => x.IsLoaded && string.Equals(x.Key, PendingModelKey, StringComparison.OrdinalIgnoreCase))?.InstanceId
            ?? _snapshot?.Models.FirstOrDefault(x => x.IsLoaded)?.InstanceId;
        if (string.IsNullOrWhiteSpace(instance)) { SetupProgress = "В памяти нет загруженной модели."; return; }
        var operation = BeginOperation();
        try { await _engineManager.UnloadModelAsync(Engine, Endpoint(), instance, operation.Token); await RefreshLocalAiAsync(); SetupProgress = "Память модели освобождена."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { SetupProgress = $"Не удалось выгрузить модель: {ex.Message}"; }
    }

    private async Task EstimateSelectedAsync()
    {
        var modelKey = PendingModelKey;
        if (string.IsNullOrWhiteSpace(modelKey)) { SetupProgress = "Сначала выберите модель."; return; }
        var operation = BeginOperation();
        var profile = Enum.IsDefined(typeof(LocalAiPerformanceProfile), Settings.LocalAiPerformanceProfile)
            ? (LocalAiPerformanceProfile)Settings.LocalAiPerformanceProfile
            : LocalAiPerformanceProfile.Balanced;
        await EstimateAsync(modelKey, CreateLoadRequest(modelKey, profile), operation.Token);
    }

    private async Task EstimateAsync(string modelKey, LocalAiLoadRequest request, CancellationToken cancellationToken)
    {
        var estimate = await _engineManager.EstimateAsync(Engine, modelKey, request, cancellationToken);
        ResourceForecast = $"ОЗУ ≈ {FormatBytes(estimate.EstimatedRamBytes)} · VRAM ≈ {FormatBytes(estimate.EstimatedVramBytes)} · нагрузка: {estimate.LoadLevel} · {(estimate.FitsAvailableMemory ? "помещается в доступную память" : "недостаточно свободной памяти")}";
    }

    private static LocalAiLoadRequest CreateLoadRequest(string modelKey, LocalAiPerformanceProfile profile)
    {
        var generation = LocalAiGenerationSettings.For(profile);
        return new(modelKey, generation.ContextLength, generation.IdleUnload, generation.GpuOffloadLayers == 0 ? "off" : "auto");
    }

    private CancellationTokenSource BeginOperation()
    {
        CancelOperation();
        _operation = new CancellationTokenSource();
        return _operation;
    }
    private void CancelOperation() { _operation?.Cancel(); _operation?.Dispose(); _operation = null; }
    private static string FormatBytes(double value) => value <= 0 ? "неизвестно" : value >= 1024 * 1024 * 1024 ? $"{value / 1024 / 1024 / 1024:F1} ГБ" : $"{value / 1024 / 1024:F0} МБ";
    private static void OpenLmStudioDownload() => Process.Start(new ProcessStartInfo("https://lmstudio.ai/download") { UseShellExecute = true });
    private void ShowHelp() => _dialogs.ShowInformation("Как работает Local AI", "LM Studio — локальный backend. GTA RP Assistant проверяет API, управляет загрузкой модели и применяет безопасный профиль. Модель занимает RAM/VRAM только пока загружена. Для снижения нагрузки используйте компактный профиль или кнопку «Освободить память». Первая загрузка долгая из-за скачивания нескольких гигабайт. Удаление файла модели пока выполняется в LM Studio; приложение безопасно умеет выгружать её из памяти.");
}

public sealed class BehaviorFeatureViewModel : FeatureViewModel
{
    private readonly IProactivePolicy _proactive;

    public BehaviorFeatureViewModel(
        ApplicationUiState ui,
        SettingsWorkspace workspace,
        SettingsSaveCoordinator save,
        IProactivePolicy proactive) : base(ui, workspace)
    {
        _proactive = proactive;
        SaveSettingsCommand = save.SaveCommand;
        SnoozeFiveMinutesCommand = new RelayCommand(() =>
        {
            _proactive.Snooze(TimeSpan.FromMinutes(5));
            Ui.PipelineStatus = "Автоматические подсказки отключены на 5 минут.";
        });
        SnoozeSessionCommand = new RelayCommand(() =>
        {
            _proactive.SnoozeForSession();
            Ui.PipelineStatus = "Автоматические подсказки отключены до конца сессии.";
        });
        ResumeHintsCommand = new RelayCommand(() =>
        {
            _proactive.Resume();
            Ui.PipelineStatus = "Автоматические подсказки снова разрешены.";
        });
    }

    public SettingsEditor Settings => Workspace.Settings;
    public string PipelineStatus => Ui.PipelineStatus;
    public ICommand SnoozeFiveMinutesCommand { get; }
    public ICommand SnoozeSessionCommand { get; }
    public ICommand ResumeHintsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
}

public sealed class KnowledgeFeatureViewModel(ApplicationUiState ui, SettingsWorkspace workspace) : FeatureViewModel(ui, workspace)
{
    public int OfficialArticleCount => Ui.OfficialArticleCount;
    public int CommunityArticleCount => Ui.CommunityArticleCount;
    public int TotalArticleCount => Ui.TotalArticleCount;
    public string PipelineStatus => Ui.PipelineStatus;
}
