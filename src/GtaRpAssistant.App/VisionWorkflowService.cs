using System.Net.Http;
using System.IO;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using GtaRpAssistant.Providers;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App;

public sealed class VisionWorkflowService(
    WindowCaptureService capture,
    GameSessionMonitor gameMonitor,
    SettingsService settings,
    ISecretStore secrets,
    OverlayService overlay,
    IResourceBudgetCoordinator resourceBudget,
    ILogger<VisionWorkflowService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RunAsync(System.Windows.Window owner, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return;
        byte[]? png = null;
        var clients = new List<HttpClient>();
        try
        {
            var value = ProviderSettingsMigration.Migrate(settings.Current);
            if (!value.VisionEnabled) throw new InvalidOperationException("Ручной vision выключен в настройках.");
            var game = gameMonitor.Current ?? throw new InvalidOperationException("Окно GTA не найдено.");
            var providers = await BuildRouteAsync(value, clients, cancellationToken);
            if (providers.Count == 0) throw new InvalidOperationException("Vision route выключен или не настроен.");
            png = await Task.Run(() => capture.CapturePng(game.MainWindowHandle), cancellationToken);
            var preview = new VisionPreviewWindow(png, providers[0].Capabilities.IsLocal ? "локальный vision provider" : "облачный HTTPS vision provider") { Owner = owner };
            if (preview.ShowDialog() != true) return;
            VisionAnalysisResult? result = null;
            foreach (var provider in providers)
            {
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

    private async Task<IReadOnlyList<IVisionProvider>> BuildRouteAsync(AppSettings value, List<HttpClient> clients, CancellationToken cancellationToken)
    {
        var registry = new ProviderRegistry();
        var route = value.ProviderRouting!.Vision;
        var ids = ConfiguredIds(route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in value.ProviderConnections!.Where(connection => connection.Enabled && ids.Contains(connection.Id)))
        {
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
                    connection.Kind));
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
