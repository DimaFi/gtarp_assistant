namespace GtaRpAssistant.Core;

public enum AudioSourceKind { UserMicrophone, GameAudio }
public enum PerformanceProfile { CloudLite, Balanced, LocalHybrid, Custom }
public enum LocalAiPerformanceProfile { Compact, Balanced, Quality, Custom }
public enum ConversationRole { User, Assistant }
public enum AssistantRequestType { DirectKnowledgeQuestion, CurrentSituationQuestion, FollowUpQuestion, RuleRiskQuestion, ProblemSolving, VisionQuestion, GeneralConversation }
public enum ProactiveMode { Off, Strict, Balanced, Experimental }
public enum AnswerDecision { Show, AskForMoreInformation, Abstain }
public enum AnswerRoute { Deterministic, ConfiguredChat, LocalChat, CloudChat, Abstain }
public enum AssistantActivationKind { ManualText, ManualVoice, AutomaticVoice, Hotkey }
public enum AssistantSessionState { Dormant, WaitingForGame, Listening, SpeechDetected, Transcribing, EvaluatingIntent, SearchingKnowledge, GeneratingAnswer, ValidatingAnswer, ShowingOverlay, Cooldown, Paused, Faulted }
public enum VoiceInteractionMode { Toggle = 0, Hold = 1 }
public enum VoiceInteractionState { Idle, Arming, Listening, SpeechDetected, Transcribing, Preview, Submitting, AnswerReady, Speaking, Cancelled, Faulted }

public sealed record AudioSegment(Guid Id, AudioSourceKind Source, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int SampleRate, int Channels, ReadOnlyMemory<byte> PcmData);
public sealed record TranscriptEntry(Guid Id, AudioSourceKind Source, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Text, double RecognitionConfidence);
public sealed record TranscriptContext(IReadOnlyList<TranscriptEntry> Entries, TranscriptEntry? CurrentUserRequest);
public sealed record IntentDecision(bool ShouldConsiderHint, string? IntentId, double Confidence, bool ExplicitWakeWord, bool RequiresScreen, string Reason);
public sealed record KnowledgeFact(string Id, string ArticleId, string Text, bool Verified, DateTimeOffset UpdatedAt, string ServerScope = "all");
public sealed record KnowledgeMatch(string ArticleId, string Title, double Score, IReadOnlyList<KnowledgeFact> Facts, bool HasConflict, bool IsOutdated, string? PreparedAnswer = null, bool HasVerifiedPreparedAnswer = false);
public sealed record ProblemSolutionDetails(
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> PossibleCauses,
    bool NeedsMoreInformation,
    bool NeedsVisualContext,
    IReadOnlyList<string> FollowUpSuggestions);
public sealed record AssistantAnswer(AnswerDecision Decision, string Title, string Message, IReadOnlyList<string> UsedFactIds, string? SourceTitle, DateTimeOffset? SourceUpdatedAt, bool CanSpeak, string DiagnosticReason, ProblemSolutionDetails? ProblemSolution = null, string? ProviderId = null, string? ModelId = null);
public sealed record AudioSnapshot(AudioSourceKind Source, int SampleRate, short[] Samples);
public sealed record VoiceActivityResult(bool SpeechDetected, double Energy);
public sealed record ProviderHealth(bool IsAvailable, string Message, IReadOnlyList<string>? Models = null);
public sealed record TranscriptResult(string Text, double Confidence);
public sealed record GroundedAnswerRequest(
    string Question,
    IReadOnlyList<KnowledgeFact> VerifiedFacts,
    string Server,
    string TranscriptContext,
    AssistantRequestType RequestType = AssistantRequestType.DirectKnowledgeQuestion,
    IReadOnlyList<AssistantConversationTurn>? Conversation = null,
    UserPersonalizationContext? Personalization = null,
    bool IsRepair = false,
    string? InvalidResponse = null);
public sealed record GroundedAnswerResponse(string Json);
public sealed record KnowledgeQuery(string Text, string Server = "all", int Limit = 5);
public sealed record KnowledgeArticle(string Id, string Title, IReadOnlyList<KnowledgeFact> Facts, DateTimeOffset UpdatedAt, bool Verified, bool Demo);
public sealed record AiRoutingContext(bool HasVerifiedPreparedAnswer, bool HasSufficientGrounding, bool LocalAvailable, bool CloudAvailable, bool UserAllowsCloud, bool ConfiguredRouteAvailable = false);
public sealed record GroundedAnswerPayload(
    string Decision,
    string Title,
    string Message,
    IReadOnlyList<string> UsedFactIds,
    bool NeedsScreen,
    bool CanSpeak,
    string? PresentationType = null,
    string? Summary = null,
    IReadOnlyList<string>? Steps = null,
    IReadOnlyList<string>? PossibleCauses = null,
    bool NeedsMoreInformation = false,
    bool NeedsVisualContext = false,
    IReadOnlyList<string>? FollowUpSuggestions = null);
public sealed record ChatProviderAvailability(
    IChatProvider? Local,
    IChatProvider? Cloud,
    bool LocalAvailable,
    bool CloudAvailable,
    IReadOnlyList<IChatProvider>? ConfiguredRoute = null)
{
    public IReadOnlyList<IChatProvider> Route => ConfiguredRoute ?? [];
}
public sealed record AssistantProcessingRequest(TranscriptEntry Entry, AssistantActivationKind Activation, string Server, bool UserAllowsCloud, bool VoiceEnabled);
public sealed record SessionEvent(DateTimeOffset Timestamp, string Name, AssistantSessionState State, string? Detail = null);
public sealed record VisionAnalysisRequest(ReadOnlyMemory<byte> PngImage, string Prompt);
public sealed record VisionAnalysisResult(string Text);
public sealed record TextToSpeechRequest(string Text, string? Voice = null, int OutputDevice = -1);
public sealed record VoiceInteractionSnapshot(
    Guid RequestId,
    VoiceInteractionMode Mode,
    VoiceInteractionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? Deadline,
    string? Transcript,
    string? Detail,
    bool AutoSubmit)
{
    public bool IsActive => State is not (VoiceInteractionState.Idle or VoiceInteractionState.Cancelled or VoiceInteractionState.Faulted or VoiceInteractionState.AnswerReady);
}

public sealed class AudioFrameEventArgs(AudioSourceKind source, ReadOnlyMemory<short> samples, int sampleRate) : EventArgs
{
    public AudioSourceKind Source { get; } = source;
    public ReadOnlyMemory<short> Samples { get; } = samples;
    public int SampleRate { get; } = sampleRate;
}
