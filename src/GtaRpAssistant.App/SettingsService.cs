using System.IO;
using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public sealed record AppSettings(
    string Server = "all",
    string Endpoint = "http://127.0.0.1:1234/v1",
    string Model = "local-model",
    int OverlaySeconds = 8,
    int PerformanceProfile = 0,
    int ProactiveMode = 1,
    bool AllowCloud = false,
    bool WatchGta = true,
    string? MicrophoneDeviceId = null,
    string SttModel = "whisper-1",
    string EmbeddingModel = "",
    string? RenderDeviceId = null,
    bool EnableGameAudio = false,
    bool PreferProcessLoopback = true,
    bool StartWithWindows = false,
    string Language = "ru",
    string OverlayPosition = "TopRight",
    int TranscriptMinutes = 3,
    string WakeWord = "Лаберти, слушай",
    bool AllowGameAudioCloud = false,
    string CloudEndpoint = "",
    string CloudModel = "",
    int VoiceMode = 0,
    string? VoiceName = null,
    int VoiceOutputDevice = -1,
    bool VisionEnabled = false,
    string VisionModel = "",
    int ProviderSettingsVersion = 0,
    IReadOnlyList<ProviderConnectionSettings>? ProviderConnections = null,
    ProviderRoutingSettings? ProviderRouting = null,
    int LocalAiPerformanceProfile = 1,
    LocalAiGenerationSettings? LocalAiCustomSettings = null,
    int LocalAiEngine = 0,
    bool LocalAiAdvancedMode = false,
    bool AutoManageLocalAi = true,
    string LmStudioCliPath = "",
    string LmStudioApplicationPath = "",
    bool EnableLongTermConversation = true,
    bool VoiceAutoSubmit = true,
    int VoiceHotkeyMode = 0,
    bool EmbeddedSttEnabled = true,
    bool StartMicrophoneOnLaunch = false,
    int AppearanceTheme = 0,
    string EmbeddedSttPackPath = "",
    bool OverlayEnabled = true,
    bool OverlayPinned = false,
    double? OverlayLeft = null,
    double? OverlayTop = null,
    bool FirstRunCompleted = false,
    int ScreenObservationMode = 0,
    int ScreenCaptureIntervalMs = 1000,
    double ScreenDiffThreshold = 0.015,
    int ScreenContextTtlSeconds = 20);

public sealed class SettingsService
{
    private readonly string _path;
    public SettingsService(string dataDirectory) => _path = Path.Combine(dataDirectory, "settings.json");
    public AppSettings Current { get; private set; } = new();
    public event EventHandler<AppSettings>? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return;
        AppSettings loaded;
        try
        {
            await using var stream = File.OpenRead(_path);
            loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var invalidPath = Path.Combine(
                Path.GetDirectoryName(_path)!,
                $"settings.invalid-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json");
            File.Move(_path, invalidPath, false);
            Current = ProviderSettingsMigration.Migrate(new AppSettings());
            await WriteAsync(Current, cancellationToken);
            return;
        }
        Current = ProviderSettingsMigration.Migrate(loaded);
        if (loaded.ProviderSettingsVersion < ProviderSettingsMigration.CurrentVersion)
            await WriteAsync(Current, cancellationToken);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var migrated = ProviderSettingsMigration.Migrate(settings);
        await WriteAsync(migrated, cancellationToken);
        Current = migrated;
        Changed?.Invoke(this, migrated);
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public static class SettingValues
{
    public static PerformanceProfile Performance(AppSettings settings) => Enum.IsDefined(typeof(PerformanceProfile), settings.PerformanceProfile) ? (PerformanceProfile)settings.PerformanceProfile : PerformanceProfile.CloudLite;
    public static ProactiveMode Proactive(AppSettings settings) => Enum.IsDefined(typeof(ProactiveMode), settings.ProactiveMode) ? (ProactiveMode)settings.ProactiveMode : ProactiveMode.Strict;
    public static TimeSpan TranscriptTtl(AppSettings settings) => TimeSpan.FromMinutes(settings.TranscriptMinutes is 1 or 3 or 5 ? settings.TranscriptMinutes : settings.TranscriptMinutes <= 0 ? .5 : 3);
    public static VoiceInteractionMode VoiceHotkey(AppSettings settings) =>
        Enum.IsDefined(typeof(VoiceInteractionMode), settings.VoiceHotkeyMode)
            ? (VoiceInteractionMode)settings.VoiceHotkeyMode
            : VoiceInteractionMode.Toggle;
    public static ScreenObservationMode ScreenObservation(AppSettings settings) =>
        Enum.IsDefined(typeof(ScreenObservationMode), settings.ScreenObservationMode)
            ? (ScreenObservationMode)settings.ScreenObservationMode
            : ScreenObservationMode.Off;
    public static LocalAiGenerationSettings LocalAi(AppSettings settings)
    {
        var profile = Enum.IsDefined(typeof(LocalAiPerformanceProfile), settings.LocalAiPerformanceProfile)
            ? (LocalAiPerformanceProfile)settings.LocalAiPerformanceProfile
            : LocalAiPerformanceProfile.Balanced;
        return profile == LocalAiPerformanceProfile.Custom && settings.LocalAiCustomSettings is not null
            ? settings.LocalAiCustomSettings
            : LocalAiGenerationSettings.For(profile);
    }
}
