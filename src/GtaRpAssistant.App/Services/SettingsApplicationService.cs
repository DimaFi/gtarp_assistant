using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.App.Services;

public sealed record LoadedSettings(SettingsEditor Editor, string ApiKey, string CloudApiKey);

public sealed class SettingsApplicationService(
    SettingsService settings,
    ISecretStore secrets,
    WindowsStartupService startup,
    ChatProviderCatalog chatProviders,
    RuleBasedIntentDetector intent,
    TranscriptBuffer transcripts,
    AudioSessionController audioSession,
    GameSessionMonitor gameMonitor,
    AssistantSessionCoordinator coordinator)
{
    public async Task<LoadedSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await settings.LoadAsync(cancellationToken);
        ApplyRuntime(settings.Current);
        var apiKey = await secrets.GetAsync("chat-provider-api-key", cancellationToken) ?? string.Empty;
        var cloudApiKey = await secrets.GetAsync("cloud-provider-api-key", cancellationToken) ?? string.Empty;
        return new(SettingsEditor.From(settings.Current), apiKey, cloudApiKey);
    }

    public async Task<AppSettings> SaveAsync(
        SettingsEditor editor,
        string apiKey,
        string cloudApiKey,
        string? microphoneId,
        string? renderDeviceId,
        CancellationToken cancellationToken)
    {
        var value = editor.ToSettings(microphoneId, renderDeviceId, settings.Current);
        await settings.SaveAsync(value, cancellationToken);
        await SaveSecretAsync("chat-provider-api-key", apiKey, cancellationToken);
        await SaveSecretAsync("cloud-provider-api-key", cloudApiKey, cancellationToken);
        startup.Apply(value.StartWithWindows);
        chatProviders.Invalidate();
        ApplyRuntime(value);
        if (value.WatchGta)
        {
            await gameMonitor.StartAsync(cancellationToken);
        }
        else
        {
            await gameMonitor.StopAsync();
            coordinator.SetGameAvailable(true);
        }
        return value;
    }

    private async Task SaveSecretAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)) await secrets.DeleteAsync(key, cancellationToken);
        else await secrets.SaveAsync(key, value, cancellationToken);
    }

    private void ApplyRuntime(AppSettings value)
    {
        intent.WakeWord = value.WakeWord;
        intent.Mode = SettingValues.Proactive(value);
        var ttl = SettingValues.TranscriptTtl(value);
        transcripts.SetTtl(ttl);
        audioSession.SetBufferDuration(ttl);
    }
}
