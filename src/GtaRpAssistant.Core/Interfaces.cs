namespace GtaRpAssistant.Core;

public interface IAudioCaptureService : IAsyncDisposable
{
    AudioSourceKind SourceKind { get; }
    event EventHandler<AudioFrameEventArgs>? FrameCaptured;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IAiProvider
{
    string Id { get; }
    ProviderKind Kind { get; }
    ProviderCapabilities Capabilities { get; }
    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken);
}

public interface IVoiceActivityDetector { VoiceActivityResult Process(ReadOnlySpan<short> samples, int sampleRate); }
public interface IAudioRingBuffer { TimeSpan Capacity { get; } void Write(AudioSourceKind source, ReadOnlySpan<short> samples); AudioSnapshot ReadLast(AudioSourceKind source, TimeSpan duration); void Clear(); }
public interface ISpeechToTextProvider : IAiProvider { Task<TranscriptResult> TranscribeAsync(AudioSegment segment, CancellationToken cancellationToken); }
public interface IChatProvider : IAiProvider { Task<GroundedAnswerResponse> CreateGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken); }
public interface IModelIdentifiedProvider { string ModelId { get; } }
public interface ILocalAiCapabilityTester { Task<LocalAiCapabilityReport> TestAsync(IChatProvider provider, CancellationToken cancellationToken); }
public interface IKnowledgeRepository { Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken); Task<KnowledgeArticle?> GetArticleAsync(string articleId, CancellationToken cancellationToken); }
public interface IIntentDetector { Task<IntentDecision> DetectAsync(TranscriptContext context, CancellationToken cancellationToken); }
public interface IOverlayService { bool IsVisible { get; } Task ShowAsync(AssistantAnswer answer, CancellationToken cancellationToken); Task HideAsync(); }
public interface ISecretStore { Task SaveAsync(string key, string value, CancellationToken cancellationToken); Task<string?> GetAsync(string key, CancellationToken cancellationToken); Task DeleteAsync(string key, CancellationToken cancellationToken); }
public interface ITranscriptDeduplicator { bool IsDuplicate(TranscriptEntry candidate, IEnumerable<TranscriptEntry> existing); }
public interface IContextSelector { TranscriptContext Select(IEnumerable<TranscriptEntry> entries, TranscriptEntry current, int maxCharacters = 2000); }
public interface IAiRouter { AnswerRoute Select(AiRoutingContext context); }
public interface IEmbeddingProvider : IAiProvider { Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken); }
public interface IGameProcessDetector { Task<GameProcessInfo?> FindAsync(GameProfile profile, CancellationToken cancellationToken); }
public interface IChatProviderCatalog { Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken); }
public interface IVisionProvider : IAiProvider { Task<VisionAnalysisResult> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken); }
public interface ITextToSpeechProvider : IAiProvider { Task SpeakAsync(TextToSpeechRequest request, CancellationToken cancellationToken); }
public interface IAiProviderRegistry
{
    IReadOnlyList<IAiProvider> Providers { get; }
    void Register(IAiProvider provider);
    bool TryGet(string id, out IAiProvider? provider);
}
public interface IProviderRouteResolver
{
    ProviderRoutePlan Resolve(ProviderTask task, ProviderRouteSettings route);
}
public interface ITextToSpeechService
{
    IReadOnlyList<string> GetVoices();
    IReadOnlyList<AudioOutputDevice> GetOutputDevices();
    Task SpeakAsync(string text, string? voice, int outputDevice, CancellationToken cancellationToken);
    void Stop();
}
public interface ISessionEventSink { void Write(SessionEvent sessionEvent); }
public interface IProactivePolicy
{
    bool CanProcess(AssistantActivationKind activation, string topic, DateTimeOffset now, out string reason);
    void RecordShown(AssistantActivationKind activation, string topic, DateTimeOffset now);
    void Snooze(TimeSpan duration);
    void SnoozeForSession();
    void Resume();
}

public sealed record GameProfile(string Id, string DisplayName, IReadOnlyList<string> ProcessNames, IReadOnlyList<string> WindowTitlePatterns);
public sealed record GameProcessInfo(int ProcessId, nint MainWindowHandle, string ProcessName);
public sealed record AudioOutputDevice(int Id, string Name);
