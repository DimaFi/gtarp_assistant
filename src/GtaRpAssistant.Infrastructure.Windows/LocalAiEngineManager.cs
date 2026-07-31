using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GtaRpAssistant.Core;
using Microsoft.Win32;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class LocalAiEngineManager(IEnumerable<ILocalAiEngineAdapter> adapters) : ILocalAiEngineManager
{
    private readonly IReadOnlyDictionary<LocalAiEngineKind, ILocalAiEngineAdapter> _adapters = adapters.ToDictionary(x => x.Kind);
    public IReadOnlyList<LocalAiEngineKind> SupportedEngines => _adapters.Keys.Order().ToArray();

    public Task<LocalAiEngineSnapshot> InspectAsync(LocalAiEngineKind engine, Uri endpoint, CancellationToken cancellationToken) => Get(engine).InspectAsync(endpoint, cancellationToken);
    public Task StartServerAsync(LocalAiEngineKind engine, Uri endpoint, CancellationToken cancellationToken) => Get(engine).StartServerAsync(endpoint, cancellationToken);
    public Task LoadModelAsync(LocalAiEngineKind engine, Uri endpoint, LocalAiLoadRequest request, CancellationToken cancellationToken) => Get(engine).LoadModelAsync(endpoint, request, cancellationToken);
    public Task UnloadModelAsync(LocalAiEngineKind engine, Uri endpoint, string instanceId, CancellationToken cancellationToken) => Get(engine).UnloadModelAsync(endpoint, instanceId, cancellationToken);
    public Task<LocalAiDownloadProgress> DownloadModelAsync(LocalAiEngineKind engine, Uri endpoint, string modelKey, string? quantization, IProgress<LocalAiDownloadProgress>? progress, CancellationToken cancellationToken) => Get(engine).DownloadModelAsync(endpoint, modelKey, quantization, progress, cancellationToken);
    public Task<LocalAiModelDescriptor> ImportModelAsync(LocalAiEngineKind engine, string filePath, CancellationToken cancellationToken) => Get(engine).ImportModelAsync(filePath, cancellationToken);
    public Task<LocalAiResourceEstimate> EstimateAsync(LocalAiEngineKind engine, string modelKey, LocalAiLoadRequest request, CancellationToken cancellationToken) => Get(engine).EstimateAsync(modelKey, request, cancellationToken);

    private ILocalAiEngineAdapter Get(LocalAiEngineKind engine) => _adapters.TryGetValue(engine, out var adapter)
        ? adapter
        : throw new NotSupportedException($"Local AI engine '{engine}' is not registered.");
}

public sealed partial class LmStudioEngineAdapter : ILocalAiEngineAdapter, IDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ImportValidationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromMinutes(30);
    private readonly ILocalAiPathSettings _pathSettings;
    private readonly ILocalAiCommandRunner _commandRunner;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public LmStudioEngineAdapter(ILocalAiPathSettings pathSettings)
        : this(pathSettings, new LocalAiCommandRunner()) { }

    internal LmStudioEngineAdapter(ILocalAiPathSettings pathSettings, ILocalAiCommandRunner commandRunner)
    {
        _pathSettings = pathSettings ?? throw new ArgumentNullException(nameof(pathSettings));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public LocalAiEngineKind Kind => LocalAiEngineKind.LmStudio;
    public string DisplayName => "LM Studio";

    public async Task<LocalAiEngineSnapshot> InspectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var cli = FindCli();
        var application = FindDesktopApp();
        var installed = cli is not null || application is not null;
        IReadOnlyList<LocalAiModelDescriptor> models = [];
        var apiAvailable = false;
        string message;
        try
        {
            models = await GetModelsAsync(endpoint, cancellationToken);
            apiAvailable = true;
            message = models.Count == 0 ? "API запущен, модели ещё не скачаны." : "Локальный API отвечает.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            message = installed ? "LM Studio найден, локальный API остановлен." : "LM Studio не обнаружен.";
        }

        if (!string.IsNullOrWhiteSpace(_pathSettings.LmStudioCliPath) && cli is null)
            message = $"Указанный путь к lms.exe не найден: {_pathSettings.LmStudioCliPath}";
        else if (!string.IsNullOrWhiteSpace(_pathSettings.LmStudioApplicationPath) && application is null)
            message = $"Указанный путь к LM Studio.exe не найден: {_pathSettings.LmStudioApplicationPath}";

        var active = models.FirstOrDefault(x => x.IsLoaded);
        var readiness = !installed ? LocalAiReadiness.NotInstalled
            : !apiAvailable ? LocalAiReadiness.ServerStopped
            : active is not null ? LocalAiReadiness.Ready
            : models.Count > 0 ? LocalAiReadiness.ModelDownloaded
            : LocalAiReadiness.ServerReady;
        return new(Kind, DisplayName, installed, cli is not null, apiAvailable, endpoint, readiness, models, active?.Key, message, DateTimeOffset.UtcNow, cli, application);
    }

    public async Task StartServerAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var cli = FindCli();
        var application = FindDesktopApp();
        if (cli is null)
        {
            if (application is null)
                throw new FileNotFoundException("LM Studio и lms.exe не найдены. Укажите их пути в расширенных настройках или установите LM Studio.");
            if (await TryDesktopFallbackAsync(application, null, endpoint, cancellationToken)) return;
            throw new FileNotFoundException("LM Studio запущен, но lms.exe не найден и API не включился. Укажите путь к .lmstudio\\bin\\lms.exe.");
        }
        var port = Origin(endpoint).Port;
        LocalAiCommandResult daemonStatus;
        try
        {
            daemonStatus = await _commandRunner.RunAsync(cli, ["daemon", "status", "--json"], CommandTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            if (await TryDesktopFallbackAsync(application, cli, endpoint, cancellationToken)) return;
            throw new TimeoutException("LM Studio CLI не ответил при проверке фоновой службы. Откройте LM Studio один раз или проверьте путь к lms.exe.", ex);
        }
        if (daemonStatus.ExitCode != 0 || daemonStatus.Output.Contains("not-running", StringComparison.OrdinalIgnoreCase))
        {
            LocalAiCommandResult daemon;
            try
            {
                daemon = await _commandRunner.RunAsync(cli, ["daemon", "up", "--json"], TimeSpan.FromSeconds(75), cancellationToken);
            }
            catch (TimeoutException ex)
            {
                if (await TryDesktopFallbackAsync(application, cli, endpoint, cancellationToken)) return;
                throw new TimeoutException("Фоновая служба LM Studio не запустилась вовремя. Откройте LM Studio один раз или переустановите headless daemon.", ex);
            }
            if (daemon.ExitCode != 0)
            {
                if (await TryDesktopFallbackAsync(application, cli, endpoint, cancellationToken)) return;
                throw new InvalidOperationException($"Не удалось запустить фоновую службу LM Studio (llmster). {ShortError(daemon)} Проверьте указанные пути, откройте LM Studio один раз или переустановите headless daemon.");
            }
        }
        LocalAiCommandResult result;
        try
        {
            result = await _commandRunner.RunAsync(cli, ["server", "start", "--port", port.ToString(CultureInfo.InvariantCulture), "--bind", "127.0.0.1"], TimeSpan.FromSeconds(45), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            if (await TryDesktopFallbackAsync(application, cli, endpoint, cancellationToken)) return;
            throw new TimeoutException("LM Studio API не запустился вовремя. Проверьте порт и настройки Local Server.", ex);
        }
        if (result.ExitCode != 0 && !result.Output.Contains("already", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(ShortError(result));
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await GetModelsAsync(endpoint, cancellationToken); return; }
            catch (HttpRequestException) { await Task.Delay(500, cancellationToken); }
        }
        throw new TimeoutException("LM Studio сообщил о запуске, но API не стал доступен.");
    }

    public async Task<IReadOnlyList<LocalAiModelDescriptor>> GetModelsAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Origin(endpoint), "/api/v1/models"));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("models", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray().Select(ParseModel).Where(x => x is not null).Select(x => x!).ToArray();
    }

    public async Task LoadModelAsync(Uri endpoint, LocalAiLoadRequest request, CancellationToken cancellationToken)
    {
        var cli = FindCli();
        if (cli is not null)
        {
            var result = await _commandRunner.RunAsync(cli, BuildLoadArguments(request, estimateOnly: false), TimeSpan.FromMinutes(5), cancellationToken);
            if (result.ExitCode != 0 && !result.Output.Contains("already", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"LM Studio не загрузил модель. {ShortError(result)}");
            return;
        }

        var payload = new
        {
            model = request.ModelKey,
            context_length = request.ContextLength,
            flash_attention = request.FlashAttention,
            echo_load_config = true,
        };
        using var response = await _http.PostAsJsonAsync(new Uri(Origin(endpoint), "/api/v1/models/load"), payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnloadModelAsync(Uri endpoint, string instanceId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(Origin(endpoint), "/api/v1/models/unload"), new { instance_id = instanceId }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<LocalAiDownloadProgress> DownloadModelAsync(Uri endpoint, string modelKey, string? quantization, IProgress<LocalAiDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(Origin(endpoint), "/api/v1/models/download"), new { model = modelKey, quantization }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var initialJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var initial = ParseDownload(initialJson, modelKey);
        progress?.Report(initial);
        if (initial.IsTerminal || string.IsNullOrWhiteSpace(initial.Error) == false) return initial;

        var jobId = ReadJobId(initialJson);
        if (string.IsNullOrWhiteSpace(jobId)) return initial with { Status = "failed", Error = "LM Studio не вернул job_id загрузки." };
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            using var statusResponse = await _http.GetAsync(new Uri(Origin(endpoint), $"/api/v1/models/download/status/{Uri.EscapeDataString(jobId)}"), cancellationToken);
            statusResponse.EnsureSuccessStatusCode();
            var status = ParseDownload(await statusResponse.Content.ReadAsStringAsync(cancellationToken), modelKey);
            progress?.Report(status);
            if (status.IsTerminal) return status;
        }
    }

    public async Task<LocalAiModelDescriptor> ImportModelAsync(string filePath, CancellationToken cancellationToken)
    {
        var fullPath = await ValidateGgufModelPathAsync(filePath, cancellationToken);
        var cli = FindCli() ?? throw new FileNotFoundException(
            "lms.exe не найден. Укажите путь к LM Studio CLI в расширенных настройках.");
        var userRepository = BuildLocalImportRepository(fullPath);

        var before = await ListLocalLlmModelsAsync(cli, cancellationToken);
        var dryRun = await _commandRunner.RunAsync(cli,
            ["import", fullPath, "--copy", "--yes", "--user-repo", userRepository, "--dry-run"], ImportValidationTimeout, cancellationToken);
        if (dryRun.ExitCode != 0)
            throw new InvalidOperationException($"LM Studio отклонил модель при безопасной проверке. {ShortError(dryRun)}");

        var import = await _commandRunner.RunAsync(cli,
            ["import", fullPath, "--copy", "--yes", "--user-repo", userRepository], ImportTimeout, cancellationToken);
        if (import.ExitCode != 0)
            throw new InvalidOperationException($"LM Studio не импортировал модель. {ShortError(import)}");

        IReadOnlyList<LocalAiModelDescriptor> after = [];
        for (var attempt = 0; attempt < 10; attempt++)
        {
            after = await ListLocalLlmModelsAsync(cli, cancellationToken);
            var resolved = ResolveImportedModel(before, after, fullPath, import.Output + Environment.NewLine + import.Error);
            if (resolved is not null) return resolved;
            if (attempt < 9) await Task.Delay(500, cancellationToken);
        }

        throw new InvalidOperationException(
            "LM Studio завершил импорт, но не сообщил новый ключ LLM-модели. Возможно, эта модель уже была импортирована. Обновите список и выберите её вручную.");
    }

    private async Task<IReadOnlyList<LocalAiModelDescriptor>> ListLocalLlmModelsAsync(string cli, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(cli, ["ls", "--llm", "--json"], TimeSpan.FromSeconds(45), cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Не удалось получить список LLM-моделей LM Studio. {ShortError(result)}");

        try
        {
            var json = ExtractJsonArray(result.Output);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateArray()
                .Select(ParseModel)
                .Where(x => x?.IsChatModel == true)
                .Select(x => x!)
                .ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("LM Studio вернул некорректный JSON списка моделей.", ex);
        }
    }

    private static string ExtractJsonArray(string output)
    {
        var start = output.IndexOf('[');
        var end = output.LastIndexOf(']');
        if (start < 0 || end < start) throw new JsonException("JSON array was not found in CLI output.");
        return output[start..(end + 1)];
    }

    private static LocalAiModelDescriptor? ResolveImportedModel(
        IReadOnlyList<LocalAiModelDescriptor> before,
        IReadOnlyList<LocalAiModelDescriptor> after,
        string filePath,
        string commandOutput)
    {
        var old = before.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var changed = after.Where(candidate =>
            !old.TryGetValue(candidate.Key, out var previous) ||
            !string.Equals(previous.SelectedVariant, candidate.SelectedVariant, StringComparison.OrdinalIgnoreCase) ||
            !previous.Variants.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(candidate.Variants.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (changed.Length == 1) return changed[0];

        var mentioned = after.Where(x => commandOutput.Contains(x.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (mentioned.Length == 1) return mentioned[0];

        var fileStem = Path.GetFileNameWithoutExtension(filePath);
        var normalizedStem = NormalizeModelName(fileStem);
        var matching = (changed.Length > 0 ? changed : after)
            .Where(x => NormalizeModelName(x.Key.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? x.Key) == normalizedStem ||
                        NormalizeModelName(x.DisplayName) == normalizedStem)
            .ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private static string NormalizeModelName(string value) =>
        Regex.Replace(value, "[^a-zA-Z0-9а-яА-Я]+", "", RegexOptions.CultureInvariant).ToLowerInvariant();

    internal static string BuildLocalImportRepository(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var slug = Regex.Replace(fileName.ToLowerInvariant(), "[^a-z0-9]+", "-", RegexOptions.CultureInvariant).Trim('-');
        if (slug.Length > 48) slug = slug[..48].TrimEnd('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = "local-model";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFileName(filePath)))).ToLowerInvariant()[..8];
        return $"local-imports/{slug}-{fingerprint}";
    }

    private static async Task<string> ValidateGgufModelPathAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Выберите файл модели GGUF.", nameof(filePath));

        string fullPath;
        try { fullPath = Path.GetFullPath(filePath.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Путь к модели некорректен.", nameof(filePath), ex);
        }
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Файл модели не найден.", fullPath);

        var name = Path.GetFileName(fullPath);
        if (!string.Equals(Path.GetExtension(name), ".gguf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Поддерживается импорт только одного файла модели в формате .gguf.");
        if (name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Файл mmproj содержит vision-проектор, а не самостоятельную языковую модель.");
        if (ShardedGgufNameRegex().IsMatch(name))
            throw new InvalidDataException("Модель состоит из нескольких GGUF-частей. Импортируйте её средствами LM Studio, выбрав полный набор шардов.");

        var magic = new byte[4];
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, magic.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = 0;
        while (read < magic.Length)
        {
            var count = await stream.ReadAsync(magic.AsMemory(read), cancellationToken);
            if (count == 0) break;
            read += count;
        }
        if (read != magic.Length || magic[0] != (byte)'G' || magic[1] != (byte)'G' || magic[2] != (byte)'U' || magic[3] != (byte)'F')
            throw new InvalidDataException("Файл не содержит сигнатуру GGUF и не может быть безопасно импортирован как модель.");
        return fullPath;
    }

    [GeneratedRegex(@"(?:^|[-_.])\d{1,5}-of-\d{1,5}\.gguf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShardedGgufNameRegex();

    public async Task<LocalAiResourceEstimate> EstimateAsync(string modelKey, LocalAiLoadRequest request, CancellationToken cancellationToken)
    {
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var cli = FindCli();
        long estimatedTotal = 0;
        long estimatedGpu = 0;
        string detail = "Оценка построена по каталогу приложения.";
        if (cli is not null)
        {
            var result = await _commandRunner.RunAsync(cli, BuildLoadArguments(request with { ModelKey = modelKey }, estimateOnly: true), TimeSpan.FromSeconds(45), cancellationToken);
            if (result.ExitCode == 0)
            {
                estimatedTotal = ParseMemory(result.Output, "Estimated Total Memory");
                estimatedGpu = ParseMemory(result.Output, "Estimated GPU Memory");
                detail = result.Output.Trim();
            }
        }
        if (estimatedTotal <= 0)
        {
            var catalog = LocalAiRecommendedModelCatalog.Models.FirstOrDefault(x => string.Equals(x.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase));
            estimatedTotal = (catalog?.MinimumRamGb ?? 4) * 1024L * 1024 * 1024;
        }
        var ratio = available <= 0 ? 1 : (double)estimatedTotal / available;
        return new(available, estimatedTotal, estimatedGpu, null, ratio < .35 ? "Низкая" : ratio < .7 ? "Средняя" : "Высокая", estimatedTotal < available * .85, detail);
    }

    internal static IReadOnlyList<string> BuildLoadArguments(LocalAiLoadRequest request, bool estimateOnly)
    {
        if (string.IsNullOrWhiteSpace(request.ModelKey)) throw new ArgumentException("Model key is required.", nameof(request));
        if (request.ContextLength <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Context length must be positive.");
        var arguments = new List<string>
        {
            "load",
            request.ModelKey,
            "--context-length",
            request.ContextLength.ToString(CultureInfo.InvariantCulture),
            "--parallel",
            "1",
            "--yes",
        };
        var gpu = request.GpuOffload?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(gpu) && gpu != "auto")
        {
            var validRatio = double.TryParse(gpu, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) && ratio is >= 0 and <= 1;
            if (gpu is not ("off" or "max") && !validRatio)
                throw new ArgumentException("GPU offload must be auto, off, max or a ratio from 0 to 1.", nameof(request));
            arguments.Add("--gpu");
            arguments.Add(gpu);
        }
        if (!estimateOnly)
        {
            arguments.Add("--ttl");
            arguments.Add(Math.Max(1, (int)Math.Ceiling(request.IdleTtl.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
        }
        else arguments.Add("--estimate-only");
        return arguments;
    }

    internal static LocalAiModelDescriptor? ParseModel(JsonElement item)
    {
        var key = ReadString(item, "key", "modelKey");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var loaded = TryGetProperty(item, out var instances, "loaded_instances", "loadedInstances") && instances.ValueKind == JsonValueKind.Array && instances.GetArrayLength() > 0;
        var instance = loaded && instances[0].TryGetProperty("id", out var id) ? id.GetString() : null;
        var quantization = item.TryGetProperty("quantization", out var quant) && quant.ValueKind == JsonValueKind.Object && quant.TryGetProperty("name", out var name) ? name.GetString() : null;
        var capabilities = item.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object ? caps : default;
        var type = ReadString(item, "type")?.ToLowerInvariant() switch
        {
            "llm" => LocalAiModelType.Llm,
            "embedding" => LocalAiModelType.Embedding,
            _ => LocalAiModelType.Unknown,
        };
        var variants = ReadStringArray(item, "variants");
        return new(
            key,
            ReadString(item, "display_name", "displayName") ?? key,
            ReadString(item, "format") ?? "unknown",
            quantization,
            ReadInt64(item, "size_bytes", "sizeBytes"),
            ReadString(item, "params_string", "paramsString"),
            checked((int)Math.Min(int.MaxValue, ReadInt64(item, "max_context_length", "maxContextLength"))),
            loaded,
            instance,
            ReadBoolean(capabilities, "vision") || ReadBoolean(item, "vision"),
            ReadBoolean(capabilities, "trained_for_tool_use", "trainedForToolUse") || ReadBoolean(item, "trained_for_tool_use", "trainedForToolUse"),
            ReadString(item, "description"),
            type,
            variants,
            ReadString(item, "selected_variant", "selectedVariant"));
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
                if (element.TryGetProperty(name, out value)) return true;
        }
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, params string[] names) =>
        TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long ReadInt64(JsonElement element, params string[] names) =>
        TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : 0;

    private static bool ReadBoolean(JsonElement element, params string[] names) =>
        TryGetProperty(element, out var value, names) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : ReadString(x, "key", "modelKey", "name"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LocalAiDownloadProgress ParseDownload(string json, string modelKey)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(modelKey,
            root.TryGetProperty("status", out var status) ? status.GetString() ?? "unknown" : "unknown",
            root.TryGetProperty("downloaded_bytes", out var downloaded) ? downloaded.GetInt64() : 0,
            root.TryGetProperty("total_size_bytes", out var total) ? total.GetInt64() : 0,
            root.TryGetProperty("bytes_per_second", out var speed) ? speed.GetDouble() : 0,
            root.TryGetProperty("error", out var error) ? error.GetString() : null);
    }

    private static string? ReadJobId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("job_id", out var id) ? id.GetString() : null;
    }

    private static Uri Origin(Uri endpoint) => new UriBuilder(endpoint) { Path = "/", Query = "", Fragment = "" }.Uri;
    private string? FindCli()
    {
        var configured = ResolveConfiguredFile(_pathSettings.LmStudioCliPath);
        if (!string.IsNullOrWhiteSpace(_pathSettings.LmStudioCliPath)) return configured;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[] { Path.Combine(home, ".lmstudio", "bin", "lms.exe") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(x => Path.Combine(x.Trim('"'), "lms.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }
    private string? FindDesktopApp()
    {
        var configured = ResolveConfiguredFile(_pathSettings.LmStudioApplicationPath);
        if (!string.IsNullOrWhiteSpace(_pathSettings.LmStudioApplicationPath)) return configured;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var metadata = FindDesktopAppFromInstallMetadata(home);
        if (metadata is not null) return metadata;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var standard = new[] { Path.Combine(local, "Programs", "LM Studio", "LM Studio.exe"), Path.Combine(local, "LM Studio", "LM Studio.exe") }.FirstOrDefault(File.Exists);
        return standard ?? FindDesktopAppInRegistry();
    }

    internal static string? FindDesktopAppFromInstallMetadata(string userProfile)
    {
        if (string.IsNullOrWhiteSpace(userProfile)) return null;
        try
        {
            var metadataPath = Path.Combine(userProfile, ".lmstudio", ".internal", "app-install-location.json");
            if (!File.Exists(metadataPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (!document.RootElement.TryGetProperty("path", out var pathNode) || pathNode.ValueKind != JsonValueKind.String) return null;
            return ResolveConfiguredFile(pathNode.GetString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? ResolveConfiguredFile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static void StartDesktopApplication(string executable)
    {
        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Path.GetDirectoryName(executable),
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Не удалось запустить LM Studio по пути '{executable}'.", ex);
        }
    }

    private async Task<bool> WaitForApiAsync(Uri endpoint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await GetModelsAsync(endpoint, cancellationToken); return true; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }

    private async Task<bool> TryDesktopFallbackAsync(string? application, string? cli, Uri endpoint, CancellationToken cancellationToken)
    {
        if (application is null) return false;
        StartDesktopApplication(application);
        if (cli is not null)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            var port = Origin(endpoint).Port;
            try
            {
                var result = await _commandRunner.RunAsync(cli,
                    ["server", "start", "--port", port.ToString(CultureInfo.InvariantCulture), "--bind", "127.0.0.1"],
                    TimeSpan.FromSeconds(25), cancellationToken);
                if (result.ExitCode != 0 && !result.Output.Contains("already", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch (TimeoutException)
            {
                // Some desktop builds keep the command attached while the server starts.
                // The bounded HTTP probe below is the source of truth.
            }
        }
        return await WaitForApiAsync(endpoint, TimeSpan.FromSeconds(30), cancellationToken);
    }

    private static string? FindDesktopAppInRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var (hive, view) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var item = uninstall.OpenSubKey(name);
                    if (item?.GetValue("DisplayName") is not string display || !display.StartsWith("LM Studio", StringComparison.OrdinalIgnoreCase)) continue;
                    var icon = (item.GetValue("DisplayIcon") as string)?.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(icon)) continue;
                    var candidate = Regex.Replace(icon, @",\d+$", "");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        return null;
    }

    private static string ShortError(LocalAiCommandResult result)
    {
        var value = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        return string.IsNullOrWhiteSpace(value) ? $"LM Studio CLI завершился с кодом {result.ExitCode}." : value.Trim()[..Math.Min(500, value.Trim().Length)];
    }

    private static long ParseMemory(string output, string label)
    {
        var match = Regex.Match(output, $@"{Regex.Escape(label)}:\s*([0-9.,]+)\s*(KB|MB|GB|TB)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1024d, "MB" => 1024d * 1024, "GB" => 1024d * 1024 * 1024, "TB" => 1024d * 1024 * 1024 * 1024, _ => 1 };
        return (long)(value * multiplier);
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record LocalAiCommandResult(int ExitCode, string Output, string Error);

internal interface ILocalAiCommandRunner
{
    Task<LocalAiCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class LocalAiCommandRunner : ILocalAiCommandRunner
{
    public async Task<LocalAiCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException($"Не удалось запустить {Path.GetFileName(executable)}.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process may have exited between the state check and Kill.
            }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"Команда {Path.GetFileName(executable)} не завершилась за {timeout.TotalSeconds:F0} секунд.");
        }

        return new(process.ExitCode, await outputTask, await errorTask);
    }
}
