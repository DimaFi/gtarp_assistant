namespace GtaRpAssistant.App;

public sealed class SettingsEditor : ObservableObject
{
    private string _server = "all";
    private string _endpoint = "http://127.0.0.1:1234/v1";
    private string _model = "local-model";
    private string _sttModel = "whisper-1";
    private string _cloudEndpoint = "";
    private string _cloudModel = "";
    private string _overlaySeconds = "8";
    private int _performanceProfile;
    private int _proactiveMode = 1;
    private bool _allowCloud;
    private bool _watchGta = true;
    private bool _enableGameAudio;
    private bool _preferProcessLoopback = true;
    private bool _startWithWindows;
    private string _language = "ru";
    private string _overlayPosition = "TopRight";
    private int _transcriptMinutes = 3;
    private string _wakeWord = "помощник";
    private bool _allowGameAudioCloud;
    private int _voiceMode;
    private string? _voiceName;
    private int _voiceOutputDevice = -1;
    private bool _visionEnabled;
    private string _visionModel = "";
    private int _sttProviderMode;
    private int _chatProviderMode;
    private int _visionProviderMode;
    private int _ttsProviderMode;
    private int _embeddingsProviderMode;
    private int _localAiPerformanceProfile = 1;
    private int _localAiEngine;
    private bool _localAiAdvancedMode;
    private bool _autoManageLocalAi = true;
    private string _lmStudioCliPath = "";
    private string _lmStudioApplicationPath = "";
    private bool _enableLongTermConversation;
    private bool _voiceAutoSubmit;
    private int _voiceHotkeyMode;
    private bool _embeddedSttEnabled = true;
    private string _embeddedSttPackPath = "";
    private bool _overlayEnabled = true;
    private bool _overlayPinned;
    private int _screenObservationMode;

    public string Server { get => _server; set => Set(ref _server, value); }
    public string Endpoint { get => _endpoint; set => Set(ref _endpoint, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string SttModel { get => _sttModel; set => Set(ref _sttModel, value); }
    public string CloudEndpoint { get => _cloudEndpoint; set => Set(ref _cloudEndpoint, value); }
    public string CloudModel { get => _cloudModel; set => Set(ref _cloudModel, value); }
    public string OverlaySeconds { get => _overlaySeconds; set => Set(ref _overlaySeconds, value); }
    public int PerformanceProfile { get => _performanceProfile; set => Set(ref _performanceProfile, value); }
    public int ProactiveMode { get => _proactiveMode; set => Set(ref _proactiveMode, value); }
    public bool AllowCloud { get => _allowCloud; set => Set(ref _allowCloud, value); }
    public bool WatchGta { get => _watchGta; set => Set(ref _watchGta, value); }
    public bool EnableGameAudio { get => _enableGameAudio; set => Set(ref _enableGameAudio, value); }
    public bool PreferProcessLoopback { get => _preferProcessLoopback; set => Set(ref _preferProcessLoopback, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public string Language { get => _language; set => Set(ref _language, value); }
    public string OverlayPosition { get => _overlayPosition; set => Set(ref _overlayPosition, value); }
    public int TranscriptMinutes { get => _transcriptMinutes; set => Set(ref _transcriptMinutes, value); }
    public string WakeWord { get => _wakeWord; set => Set(ref _wakeWord, value); }
    public bool AllowGameAudioCloud { get => _allowGameAudioCloud; set => Set(ref _allowGameAudioCloud, value); }
    public int VoiceMode { get => _voiceMode; set => Set(ref _voiceMode, value); }
    public string? VoiceName { get => _voiceName; set => Set(ref _voiceName, value); }
    public int VoiceOutputDevice { get => _voiceOutputDevice; set => Set(ref _voiceOutputDevice, value); }
    public bool VisionEnabled { get => _visionEnabled; set => Set(ref _visionEnabled, value); }
    public string VisionModel { get => _visionModel; set => Set(ref _visionModel, value); }
    public int SttProviderMode { get => _sttProviderMode; set => Set(ref _sttProviderMode, value); }
    public int ChatProviderMode { get => _chatProviderMode; set => Set(ref _chatProviderMode, value); }
    public int VisionProviderMode { get => _visionProviderMode; set => Set(ref _visionProviderMode, value); }
    public int TtsProviderMode { get => _ttsProviderMode; set => Set(ref _ttsProviderMode, value); }
    public int EmbeddingsProviderMode { get => _embeddingsProviderMode; set => Set(ref _embeddingsProviderMode, value); }
    public int LocalAiPerformanceProfile { get => _localAiPerformanceProfile; set => Set(ref _localAiPerformanceProfile, value); }
    public int LocalAiEngine { get => _localAiEngine; set => Set(ref _localAiEngine, value); }
    public bool LocalAiAdvancedMode { get => _localAiAdvancedMode; set => Set(ref _localAiAdvancedMode, value); }
    public bool AutoManageLocalAi { get => _autoManageLocalAi; set => Set(ref _autoManageLocalAi, value); }
    public string LmStudioCliPath { get => _lmStudioCliPath; set => Set(ref _lmStudioCliPath, value); }
    public string LmStudioApplicationPath { get => _lmStudioApplicationPath; set => Set(ref _lmStudioApplicationPath, value); }
    public bool EnableLongTermConversation { get => _enableLongTermConversation; set => Set(ref _enableLongTermConversation, value); }
    public bool VoiceAutoSubmit { get => _voiceAutoSubmit; set => Set(ref _voiceAutoSubmit, value); }
    public int VoiceHotkeyMode { get => _voiceHotkeyMode; set => Set(ref _voiceHotkeyMode, value); }
    public bool EmbeddedSttEnabled { get => _embeddedSttEnabled; set => Set(ref _embeddedSttEnabled, value); }
    public string EmbeddedSttPackPath { get => _embeddedSttPackPath; set => Set(ref _embeddedSttPackPath, value); }
    public bool OverlayEnabled { get => _overlayEnabled; set => Set(ref _overlayEnabled, value); }
    public bool OverlayPinned { get => _overlayPinned; set => Set(ref _overlayPinned, value); }
    public int ScreenObservationMode { get => _screenObservationMode; set => Set(ref _screenObservationMode, value); }

    public static SettingsEditor From(AppSettings value)
    {
        value = ProviderSettingsMigration.Migrate(value);
        var routes = value.ProviderRouting!;
        return new()
        {
        Server = value.Server, Endpoint = value.Endpoint, Model = value.Model, SttModel = value.SttModel,
        CloudEndpoint = value.CloudEndpoint, CloudModel = value.CloudModel, OverlaySeconds = value.OverlaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PerformanceProfile = value.PerformanceProfile, ProactiveMode = value.ProactiveMode, AllowCloud = value.AllowCloud, WatchGta = value.WatchGta,
        EnableGameAudio = value.EnableGameAudio, PreferProcessLoopback = value.PreferProcessLoopback, StartWithWindows = value.StartWithWindows,
        Language = value.Language, OverlayPosition = value.OverlayPosition, TranscriptMinutes = value.TranscriptMinutes, WakeWord = value.WakeWord,
        AllowGameAudioCloud = value.AllowGameAudioCloud, VoiceMode = value.VoiceMode, VoiceName = value.VoiceName, VoiceOutputDevice = value.VoiceOutputDevice, VisionEnabled = value.VisionEnabled, VisionModel = value.VisionModel,
        SttProviderMode = (int)routes.SpeechToText.Mode, ChatProviderMode = (int)routes.Chat.Mode, VisionProviderMode = (int)routes.Vision.Mode,
        TtsProviderMode = (int)routes.TextToSpeech.Mode, EmbeddingsProviderMode = (int)routes.Embeddings.Mode,
        LocalAiPerformanceProfile = value.LocalAiPerformanceProfile,
        LocalAiEngine = value.LocalAiEngine, LocalAiAdvancedMode = value.LocalAiAdvancedMode, AutoManageLocalAi = value.AutoManageLocalAi,
        LmStudioCliPath = value.LmStudioCliPath, LmStudioApplicationPath = value.LmStudioApplicationPath,
        EnableLongTermConversation = value.EnableLongTermConversation,
        VoiceAutoSubmit = value.VoiceAutoSubmit,
        VoiceHotkeyMode = value.VoiceHotkeyMode,
        EmbeddedSttEnabled = value.EmbeddedSttEnabled,
        EmbeddedSttPackPath = value.EmbeddedSttPackPath,
        OverlayEnabled = value.OverlayEnabled,
        OverlayPinned = value.OverlayPinned,
        ScreenObservationMode = value.ScreenObservationMode,
        };
    }

    public AppSettings ToSettings(string? microphoneId, string? renderDeviceId, AppSettings previous)
    {
        previous = ProviderSettingsMigration.Migrate(previous);
        var routes = previous.ProviderRouting!;
        var updated = previous with
        {
        Server = string.IsNullOrWhiteSpace(Server) ? "all" : Server.Trim(), Endpoint = Endpoint.Trim(), Model = Model.Trim(), SttModel = SttModel.Trim(),
        CloudEndpoint = CloudEndpoint.Trim(), CloudModel = CloudModel.Trim(), OverlaySeconds = int.TryParse(OverlaySeconds, out var seconds) ? Math.Clamp(seconds, 2, 60) : 8,
        PerformanceProfile = PerformanceProfile, ProactiveMode = ProactiveMode, AllowCloud = AllowCloud, WatchGta = WatchGta,
        EnableGameAudio = EnableGameAudio, PreferProcessLoopback = PreferProcessLoopback, StartWithWindows = StartWithWindows,
        Language = Language, OverlayPosition = OverlayPosition, TranscriptMinutes = TranscriptMinutes, WakeWord = string.IsNullOrWhiteSpace(WakeWord) ? "помощник" : WakeWord.Trim(),
        AllowGameAudioCloud = AllowGameAudioCloud, VoiceMode = VoiceMode, VoiceName = VoiceName, VoiceOutputDevice = VoiceOutputDevice, VisionEnabled = VisionEnabled, VisionModel = VisionModel.Trim(),
        MicrophoneDeviceId = microphoneId, RenderDeviceId = renderDeviceId,
        LocalAiPerformanceProfile = LocalAiPerformanceProfile,
        LocalAiEngine = LocalAiEngine, LocalAiAdvancedMode = LocalAiAdvancedMode, AutoManageLocalAi = AutoManageLocalAi,
        LmStudioCliPath = LmStudioCliPath.Trim(), LmStudioApplicationPath = LmStudioApplicationPath.Trim(),
        EnableLongTermConversation = EnableLongTermConversation,
        VoiceAutoSubmit = VoiceAutoSubmit,
        VoiceHotkeyMode = Enum.IsDefined(typeof(GtaRpAssistant.Core.VoiceInteractionMode), VoiceHotkeyMode) ? VoiceHotkeyMode : 0,
        EmbeddedSttEnabled = EmbeddedSttEnabled,
        EmbeddedSttPackPath = EmbeddedSttPackPath.Trim(),
        OverlayEnabled = OverlayEnabled,
        OverlayPinned = OverlayPinned,
        ScreenObservationMode = Enum.IsDefined(typeof(GtaRpAssistant.Core.ScreenObservationMode), ScreenObservationMode) ? ScreenObservationMode : 0,
        ProviderRouting = routes with
        {
            SpeechToText = routes.SpeechToText with { Mode = Mode(SttProviderMode) },
            Chat = routes.Chat with { Mode = Mode(ChatProviderMode) },
            Vision = routes.Vision with { Mode = Mode(VisionProviderMode) },
            TextToSpeech = routes.TextToSpeech with { Mode = Mode(TtsProviderMode) },
            Embeddings = routes.Embeddings with { Mode = Mode(EmbeddingsProviderMode) },
        },
        };
        return ProviderSettingsMigration.ApplyLegacyConnectionEdits(updated);
    }

    private static GtaRpAssistant.Core.ProviderSelectionMode Mode(int value) => Enum.IsDefined(typeof(GtaRpAssistant.Core.ProviderSelectionMode), value)
        ? (GtaRpAssistant.Core.ProviderSelectionMode)value
        : GtaRpAssistant.Core.ProviderSelectionMode.Disabled;
}
