using System.Net.Http;
using System.IO;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using GtaRpAssistant.Providers;
using Microsoft.Extensions.Logging;
using System.Windows.Media.Imaging;

namespace GtaRpAssistant.App;

public sealed class VisionWorkflowService(
    WindowCaptureService capture,
    GameSessionMonitor gameMonitor,
    SettingsService settings,
    ISecretStore secrets,
    OverlayService overlay,
    ILocalScreenOcr screenOcr,
    IResourceBudgetCoordinator resourceBudget,
    ILogger<VisionWorkflowService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(System.Windows.Window owner, CancellationToken cancellationToken)
    {
        var game = gameMonitor.Current ?? throw new InvalidOperationException("Окно GTA не найдено.");
        await ExecuteAsync(owner, () => Task.Run(() => capture.CapturePng(game.MainWindowHandle), cancellationToken), localOnly: false, cancellationToken);
    }

    public async Task RunLocalImageAsync(System.Windows.Window owner, string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Файл изображения не найден.", path);
        if (info.Length > 12 * 1024 * 1024) throw new InvalidDataException("Изображение больше 12 МБ.");
        await ExecuteAsync(owner, async () =>
        {
            var source = await File.ReadAllBytesAsync(path, cancellationToken);
            try { return VisionImageNormalizer.NormalizeToPng(source); }
            finally { Array.Clear(source); }
        }, localOnly: true, cancellationToken);
    }

    private async Task ExecuteAsync(System.Windows.Window owner, Func<Task<byte[]>> getPng, bool localOnly, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        byte[]? png = null;
        var clients = new List<HttpClient>();
        try
        {
            var value = ProviderSettingsMigration.Migrate(settings.Current);
            if (!value.VisionEnabled) throw new InvalidOperationException("Ручной vision выключен в настройках.");
            var providers = await BuildRouteAsync(value, clients, localOnly, cancellationToken);
            if (providers.Count == 0 && !screenOcr.IsAvailable) throw new InvalidOperationException(localOnly
                ? "Для фото нужен настроенный локальный Vision provider. Облачная отправка запрещена."
                : "Vision route выключен или не настроен.");
            png = await getPng();
            var destination = providers.FirstOrDefault() is { } first
                ? first.Capabilities.IsLocal ? "локальный OCR / локальный vision provider" : "локальный OCR / облачный HTTPS vision provider"
                : "локальный OCR";
            var preview = new VisionPreviewWindow(png, destination) { Owner = owner };
            if (preview.ShowDialog() != true) return;
            if (screenOcr.IsAvailable)
            {
                var ocr = await screenOcr.RecognizeAsync(png, cancellationToken);
                var confident = ocr.Fields.Where(x => x.Confidence >= .55 && !string.IsNullOrWhiteSpace(x.Text)).ToArray();
                var recognized = KnownScreenRecognizer.Recognize(confident);
                if (confident.Length >= 2 || recognized.Confidence >= .75)
                {
                    var fields = ScreenFieldProfiler.Apply(recognized.Kind, confident);
                    var snapshot = new ScreenContextSnapshot(DateTimeOffset.UtcNow, recognized.Kind, recognized.Confidence,
                        [ScreenRegion.Full], fields, [], DateTimeOffset.UtcNow.AddSeconds(30));
                    await overlay.ShowAsync(ScreenContextAnswerFactory.Create(snapshot), cancellationToken);
                    logger.LogInformation("Vision request completed by local OCR; fields={FieldCount}; screen={ScreenKind}", fields.Count, recognized.Kind);
                    return;
                }
            }
            if (providers.Count == 0) throw new InvalidOperationException("Локальный OCR не прочитал изображение, а Vision provider не настроен.");
            VisionAnalysisResult? result = null;
            foreach (var provider in providers)
            {
                if (localOnly && !provider.Capabilities.IsLocal) continue;
                if (!provider.Capabilities.IsLocal && !value.AllowCloud) continue;
                try
                {
                    var profile = Enum.IsDefined(typeof(LocalAiPerformanceProfile), value.LocalAiPerformanceProfile)
                        ? (LocalAiPerformanceProfile)value.LocalAiPerformanceProfile
                        : LocalAiPerformanceProfile.Balanced;
                    var leaseResult = await resourceBudget.TryAcquireAsync(new(
                        AssistantWorkloadKind.Vision,
                        profile,
                        provider.Capabilities.IsLocal), cancellationToken);
                    if (!leaseResult.Granted)
                    {
                        logger.LogInformation("Vision deferred by resource budget; provider={Provider}; reason={Reason}", provider.Id, leaseResult.Reason);
                        continue;
                    }
                    await using var workloadLease = leaseResult.Lease!;
                    logger.LogInformation("Manual vision request confirmed; provider={Provider}; local={Local}; image_bytes={ImageBytes}", provider.Id, provider.Capabilities.IsLocal, png.Length);
                    result = await provider.AnalyzeAsync(new(png, "Опиши видимый интерфейс и сообщения. Не делай выводов о правилах игры."), cancellationToken);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Vision provider failed; provider={Provider}; type={ErrorType}", provider.Id, ex.GetType().Name);
                }
            }
            if (result is null) throw new InvalidOperationException("Ни один Vision provider не обработал снимок.");
            if (ContainsUnsafeOutput(result.Text)) throw new InvalidDataException("Vision provider вернул потенциально небезопасную инструкцию.");
            var answer = new AssistantAnswer(AnswerDecision.AskForMoreInformation, "Ручной анализ экрана", result.Text, [], "Снимок окна пользователя — не источник игровых правил", DateTimeOffset.UtcNow, false, "Manual vision");
            await overlay.ShowAsync(answer, cancellationToken);
        }
        finally
        {
            if (png is not null) Array.Clear(png);
            foreach (var client in clients) client.Dispose();
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<IVisionProvider>> BuildRouteAsync(AppSettings value, List<HttpClient> clients, bool localOnly, CancellationToken cancellationToken)
    {
        var registry = new ProviderRegistry();
        var route = value.ProviderRouting!.Vision;
        var ids = ConfiguredIds(route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in value.ProviderConnections!.Where(connection => connection.Enabled && ids.Contains(connection.Id)))
        {
            if (localOnly && !connection.IsLocal) continue;
            if (connection.Kind is not (ProviderKind.OpenAiCompatible or ProviderKind.OpenAi or ProviderKind.OpenRouter or ProviderKind.Groq or ProviderKind.LmStudio or ProviderKind.Ollama or ProviderKind.CustomHttp)) continue;
            if (string.IsNullOrWhiteSpace(connection.ModelId) || (!connection.IsLocal && !value.AllowCloud)) continue;
            var secret = string.IsNullOrWhiteSpace(connection.SecretReference) ? null : await secrets.GetAsync(connection.SecretReference, cancellationToken);
            var client = new HttpClient();
            try
            {
                registry.Register(new OpenAiCompatibleVisionProvider(
                    client,
                    connection.BaseUri,
                    connection.ModelId,
                    secret,
                    connection.IsLocal,
                    connection.Id,
                    connection.Kind,
                    connection.IsLocal ? SettingValues.LocalAi(value).IdleUnload : null));
                clients.Add(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        return new ProviderRouteResolver(registry).Resolve(ProviderTask.Vision, route).Providers.OfType<IVisionProvider>().ToArray();
    }

    private static IEnumerable<string> ConfiguredIds(ProviderRouteSettings route)
    {
        if (!string.IsNullOrWhiteSpace(route.PrimaryProviderId)) yield return route.PrimaryProviderId;
        foreach (var id in route.FallbackProviderIds)
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
    }

    private static bool ContainsUnsafeOutput(string text)
    {
        var normalized = text.ToLowerInvariant();
        return normalized.Contains("http://", StringComparison.Ordinal) || normalized.Contains("https://", StringComparison.Ordinal)
            || new[] { "автокликер", "макрос", "инжект", "читать память", "автоматически наж" }.Any(normalized.Contains);
    }

    public void Dispose() => _gate.Dispose();
}

public static class VisionImageNormalizer
{
    public static byte[] NormalizeToPng(byte[] source)
    {
        using var input = new MemoryStream(source, writable: false);
        BitmapDecoder decoder;
        try
        {
            decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
        {
            throw new InvalidDataException("Поддерживаются только корректные PNG и JPEG.", ex);
        }
        var frame = decoder.Frames.FirstOrDefault() ?? throw new InvalidDataException("В изображении нет кадра.");
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 33_177_600)
            throw new InvalidDataException("Разрешение изображения превышает безопасный предел 33 Мп.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var output = new MemoryStream();
        encoder.Save(output);
        if (output.Length > 20 * 1024 * 1024) throw new InvalidDataException("PNG после декодирования слишком велик.");
        return output.ToArray();
    }
}
