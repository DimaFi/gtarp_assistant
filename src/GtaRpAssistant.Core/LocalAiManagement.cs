namespace GtaRpAssistant.Core;

public enum LocalAiEngineKind { LmStudio, Ollama, LlamaCpp, OpenAiCompatible }
public enum LocalAiReadiness { NotInstalled, Installed, ServerStopped, ServerReady, ModelDownloaded, ModelLoaded, Ready, Faulted }
public enum LocalAiModelType { Unknown, Llm, Embedding }

public sealed record LocalAiModelDescriptor
{
    public LocalAiModelDescriptor(
        string key,
        string displayName,
        string format,
        string? quantization,
        long sizeBytes,
        string? parameters,
        int maxContextLength,
        bool isLoaded,
        string? instanceId,
        bool supportsVision,
        bool supportsFunctionCalling,
        string? description,
        LocalAiModelType type = LocalAiModelType.Unknown,
        IReadOnlyList<string>? variants = null,
        string? selectedVariant = null)
    {
        Key = key;
        DisplayName = displayName;
        Format = format;
        Quantization = quantization;
        SizeBytes = sizeBytes;
        Parameters = parameters;
        MaxContextLength = maxContextLength;
        IsLoaded = isLoaded;
        InstanceId = instanceId;
        SupportsVision = supportsVision;
        SupportsFunctionCalling = supportsFunctionCalling;
        Description = description;
        Type = type;
        Variants = variants ?? Array.Empty<string>();
        SelectedVariant = selectedVariant;
    }

    public string Key { get; init; }
    public string DisplayName { get; init; }
    public string Format { get; init; }
    public string? Quantization { get; init; }
    public long SizeBytes { get; init; }
    public string? Parameters { get; init; }
    public int MaxContextLength { get; init; }
    public bool IsLoaded { get; init; }
    public string? InstanceId { get; init; }
    public bool SupportsVision { get; init; }
    public bool SupportsFunctionCalling { get; init; }
    public string? Description { get; init; }
    public LocalAiModelType Type { get; init; }
    public IReadOnlyList<string> Variants { get; init; }
    public string? SelectedVariant { get; init; }

    // Kept for source compatibility with the first settings UI. New code should use Type.
    public string Engine => Type switch
    {
        LocalAiModelType.Llm => "llm",
        LocalAiModelType.Embedding => "embedding",
        _ => "unknown",
    };

    public bool IsChatModel => Type == LocalAiModelType.Llm;
}

public sealed record LocalAiEngineSnapshot(
    LocalAiEngineKind Engine,
    string DisplayName,
    bool IsInstalled,
    bool CliAvailable,
    bool ApiAvailable,
    Uri Endpoint,
    LocalAiReadiness Readiness,
    IReadOnlyList<LocalAiModelDescriptor> Models,
    string? ActiveModelKey,
    string Message,
    DateTimeOffset CheckedAt,
    string? CliPath = null,
    string? ApplicationPath = null);

public interface ILocalAiPathSettings
{
    string? LmStudioCliPath { get; }
    string? LmStudioApplicationPath { get; }
}

public sealed record LocalAiBootstrapInstallResult(string CliPath, string InstallHome, string InstallerSha256);

public interface ILocalAiBootstrapInstaller
{
    Task<LocalAiBootstrapInstallResult> InstallAsync(string installHome, CancellationToken cancellationToken);
}

public sealed record LocalAiRecommendedModel(
    string Id,
    string DisplayName,
    LocalAiEngineKind Engine,
    string ModelKey,
    string Quantization,
    int MinimumRamGb,
    int RecommendedRamGb,
    int RecommendedVramGb,
    string Speed,
    string Quality,
    bool SupportsRussian,
    bool SupportsJson,
    bool SupportsVision,
    bool SupportsFunctionCalling,
    string Description,
    string RecommendedUse,
    LocalAiPerformanceProfile Profile);

public sealed record LocalAiLoadRequest(
    string ModelKey,
    int ContextLength,
    TimeSpan IdleTtl,
    string GpuOffload,
    bool FlashAttention = true);

public sealed record LocalAiDownloadProgress(
    string ModelKey,
    string Status,
    long DownloadedBytes,
    long TotalBytes,
    double BytesPerSecond,
    string? Error = null)
{
    public double Percent => TotalBytes <= 0 ? 0 : Math.Clamp((double)DownloadedBytes / TotalBytes * 100, 0, 100);
    public bool IsTerminal => Status is "completed" or "already_downloaded" or "failed";
}

public sealed record LocalAiResourceEstimate(
    long AvailableRamBytes,
    long EstimatedRamBytes,
    long EstimatedVramBytes,
    double? ExpectedTokensPerSecond,
    string LoadLevel,
    bool FitsAvailableMemory,
    string Detail);

public interface ILocalAiEngineAdapter
{
    LocalAiEngineKind Kind { get; }
    string DisplayName { get; }
    Task<LocalAiEngineSnapshot> InspectAsync(Uri endpoint, CancellationToken cancellationToken);
    Task StartServerAsync(Uri endpoint, CancellationToken cancellationToken);
    Task<IReadOnlyList<LocalAiModelDescriptor>> GetModelsAsync(Uri endpoint, CancellationToken cancellationToken);
    Task LoadModelAsync(Uri endpoint, LocalAiLoadRequest request, CancellationToken cancellationToken);
    Task UnloadModelAsync(Uri endpoint, string instanceId, CancellationToken cancellationToken);
    Task<LocalAiDownloadProgress> DownloadModelAsync(Uri endpoint, string modelKey, string? quantization, IProgress<LocalAiDownloadProgress>? progress, CancellationToken cancellationToken);
    Task<LocalAiModelDescriptor> ImportModelAsync(string filePath, CancellationToken cancellationToken);
    Task<LocalAiResourceEstimate> EstimateAsync(string modelKey, LocalAiLoadRequest request, CancellationToken cancellationToken);
}

public interface ILocalAiEngineManager
{
    IReadOnlyList<LocalAiEngineKind> SupportedEngines { get; }
    Task<LocalAiEngineSnapshot> InspectAsync(LocalAiEngineKind engine, Uri endpoint, CancellationToken cancellationToken);
    Task StartServerAsync(LocalAiEngineKind engine, Uri endpoint, CancellationToken cancellationToken);
    Task LoadModelAsync(LocalAiEngineKind engine, Uri endpoint, LocalAiLoadRequest request, CancellationToken cancellationToken);
    Task UnloadModelAsync(LocalAiEngineKind engine, Uri endpoint, string instanceId, CancellationToken cancellationToken);
    Task<LocalAiDownloadProgress> DownloadModelAsync(LocalAiEngineKind engine, Uri endpoint, string modelKey, string? quantization, IProgress<LocalAiDownloadProgress>? progress, CancellationToken cancellationToken);
    Task<LocalAiModelDescriptor> ImportModelAsync(LocalAiEngineKind engine, string filePath, CancellationToken cancellationToken);
    Task<LocalAiResourceEstimate> EstimateAsync(LocalAiEngineKind engine, string modelKey, LocalAiLoadRequest request, CancellationToken cancellationToken);
}

public static class LocalAiRecommendedModelCatalog
{
    public static IReadOnlyList<LocalAiRecommendedModel> Models { get; } =
    [
        new("qwen3-4b-2507", "Qwen3 4B Instruct 2507", LocalAiEngineKind.LmStudio, "qwen/qwen3-4b-2507", "Q4_K_M",
            4, 8, 4, "Средняя", "Высокая для компактной модели", true, true, false, true,
            "Многоязычная instruct-модель без длинных reasoning-блоков.", "Основной локальный RAG и ответы во время игры.", LocalAiPerformanceProfile.Balanced),
        new("granite-4-micro", "Granite 4 Micro", LocalAiEngineKind.LmStudio, "ibm/granite-4-micro", "Q4_K_M",
            4, 8, 4, "Средняя", "Сбалансированная", true, true, false, true,
            "Компактная модель IBM для RAG, tool use и структурированного JSON.", "Слабые и средние ПК, короткие grounded-ответы.", LocalAiPerformanceProfile.Compact),
        new("qwen3-vl-4b", "Qwen3-VL 4B", LocalAiEngineKind.LmStudio, "qwen/qwen3-vl-4b", "Q4_K_M",
            6, 16, 6, "Средняя", "Высокая с изображениями", true, true, true, true,
            "Мультимодальная модель для текста и подтверждённых снимков экрана.", "Vision и расширенный помощник на ПК с запасом памяти.", LocalAiPerformanceProfile.Quality),
    ];

    public static LocalAiRecommendedModel Recommend(long availableRamBytes, bool needsVision = false)
    {
        var ramGb = availableRamBytes / 1024d / 1024d / 1024d;
        var candidates = Models.Where(x => !needsVision || x.SupportsVision).ToArray();
        return candidates.Where(x => x.RecommendedRamGb <= ramGb)
            .OrderByDescending(x => x.RecommendedRamGb)
            .FirstOrDefault() ?? candidates.OrderBy(x => x.MinimumRamGb).First();
    }
}
