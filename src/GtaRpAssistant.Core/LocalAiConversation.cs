namespace GtaRpAssistant.Core;

public sealed record LocalAiGenerationSettings(
    int ContextLength,
    int MaxOutputTokens,
    TimeSpan Timeout,
    int CpuThreads,
    int GpuOffloadLayers,
    int MaxTranscriptEntries,
    TimeSpan IdleUnload,
    int QueueLimit)
{
    public static LocalAiGenerationSettings For(LocalAiPerformanceProfile profile) => profile switch
    {
        LocalAiPerformanceProfile.Compact => new(2048, 220, TimeSpan.FromSeconds(30), 2, 0, 3, TimeSpan.FromMinutes(2), 1),
        LocalAiPerformanceProfile.Quality => new(8192, 700, TimeSpan.FromSeconds(90), 6, -1, 8, TimeSpan.FromMinutes(10), 1),
        _ => new(4096, 420, TimeSpan.FromSeconds(60), 4, -1, 6, TimeSpan.FromMinutes(5), 1),
    };
}

public sealed record AssistantConversationTurn(
    Guid Id,
    DateTimeOffset CreatedAt,
    ConversationRole Role,
    string Text,
    string? ProviderId,
    string? ModelId,
    IReadOnlyList<string> UsedFactIds,
    string? SituationId);

public sealed record AssistantConversationSnapshot(IReadOnlyList<AssistantConversationTurn> Turns, DateTimeOffset? LastActivity, string? SituationId);
public sealed record ConversationContextQuery(string? SituationId, int MaxTurns = 6, TimeSpan? MaxAge = null);
public sealed record AssistantConversationInfo(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int MessageCount);

public interface IAssistantConversationStore
{
    Guid CurrentConversationId { get; }
    void Add(AssistantConversationTurn turn);
    AssistantConversationSnapshot GetCurrent();
    AssistantConversationSnapshot GetRelevant(ConversationContextQuery query);
    IReadOnlyList<AssistantConversationInfo> ListConversations(int limit = 50);
    bool TryOpenConversation(Guid conversationId);
    void RenameConversation(Guid conversationId, string title);
    void DeleteConversation(Guid conversationId);
    void StartNewConversation();
    void Clear();
}

public sealed class InMemoryAssistantConversationStore(int capacity = 16, TimeSpan? idleTtl = null) : IAssistantConversationStore
{
    private readonly object _gate = new();
    private readonly int _capacity = Math.Clamp(capacity, 4, 50);
    private readonly TimeSpan _idleTtl = idleTtl ?? TimeSpan.FromMinutes(12);
    private readonly Dictionary<Guid, InMemoryConversation> _conversations = [];
    private Guid _currentConversationId = Guid.NewGuid();

    public Guid CurrentConversationId { get { lock (_gate) return _currentConversationId; } }

    public void Add(AssistantConversationTurn turn)
    {
        lock (_gate)
        {
            var conversation = CurrentUnsafe();
            if (conversation.Turns.Count > 0 && (turn.CreatedAt - conversation.Turns[^1].CreatedAt > _idleTtl
                || SituationChanged(conversation.Turns[^1].SituationId, turn.SituationId)))
            {
                _currentConversationId = Guid.NewGuid();
                conversation = CurrentUnsafe();
            }
            var stored = turn with { Text = Limit(turn.Text, 1200), UsedFactIds = turn.UsedFactIds.Take(12).ToArray() };
            conversation.Turns.Add(stored);
            conversation.UpdatedAt = stored.CreatedAt;
            if (conversation.Title == DefaultTitle && stored.Role == ConversationRole.User) conversation.Title = ConversationTitleGenerator.FromContext(stored.Text);
            if (conversation.Turns.Count > _capacity) conversation.Turns.RemoveRange(0, conversation.Turns.Count - _capacity);
        }
    }

    public AssistantConversationSnapshot GetCurrent()
    {
        lock (_gate) return Snapshot(CurrentUnsafe().Turns);
    }

    public AssistantConversationSnapshot GetRelevant(ConversationContextQuery query)
    {
        lock (_gate)
        {
            var cutoff = DateTimeOffset.UtcNow - (query.MaxAge ?? _idleTtl);
            var turns = CurrentUnsafe().Turns.Where(x => x.CreatedAt >= cutoff && (string.IsNullOrWhiteSpace(query.SituationId)
                || string.IsNullOrWhiteSpace(x.SituationId)
                || string.Equals(x.SituationId, query.SituationId, StringComparison.Ordinal))).TakeLast(Math.Clamp(query.MaxTurns, 1, 12)).ToArray();
            return Snapshot(turns);
        }
    }

    public IReadOnlyList<AssistantConversationInfo> ListConversations(int limit = 50)
    {
        lock (_gate) return _conversations.Select(x => new AssistantConversationInfo(x.Key, x.Value.Title, x.Value.CreatedAt, x.Value.UpdatedAt, x.Value.Turns.Count))
            .OrderByDescending(x => x.UpdatedAt).Take(Math.Clamp(limit, 1, 200)).ToArray();
    }

    public bool TryOpenConversation(Guid conversationId)
    {
        lock (_gate)
        {
            if (!_conversations.ContainsKey(conversationId)) return false;
            _currentConversationId = conversationId;
            return true;
        }
    }

    public void RenameConversation(Guid conversationId, string title)
    {
        lock (_gate)
        {
            if (_conversations.TryGetValue(conversationId, out var conversation)) conversation.Title = NormalizeTitle(title);
        }
    }

    public void DeleteConversation(Guid conversationId)
    {
        lock (_gate)
        {
            _conversations.Remove(conversationId);
            if (_currentConversationId == conversationId)
                _currentConversationId = _conversations.OrderByDescending(x => x.Value.UpdatedAt).Select(x => x.Key).FirstOrDefault();
            if (_currentConversationId == Guid.Empty) _currentConversationId = Guid.NewGuid();
            _ = CurrentUnsafe();
        }
    }

    public void StartNewConversation()
    {
        lock (_gate)
        {
            _currentConversationId = Guid.NewGuid();
            _ = CurrentUnsafe();
        }
    }

    public void Clear() { lock (_gate) CurrentUnsafe().Turns.Clear(); }
    private static AssistantConversationSnapshot Snapshot(IEnumerable<AssistantConversationTurn> turns)
    {
        var copy = turns.ToArray();
        return new(copy, copy.LastOrDefault()?.CreatedAt, copy.LastOrDefault()?.SituationId);
    }
    private static bool SituationChanged(string? previous, string? current) => !string.IsNullOrWhiteSpace(previous) && !string.IsNullOrWhiteSpace(current) && !string.Equals(previous, current, StringComparison.Ordinal);
    private static string Limit(string text, int max) => text.Length <= max ? text : text[..max];
    private InMemoryConversation CurrentUnsafe()
    {
        if (_conversations.TryGetValue(_currentConversationId, out var current)) return current;
        var now = DateTimeOffset.UtcNow;
        current = new(DefaultTitle, now, now, []);
        _conversations[_currentConversationId] = current;
        return current;
    }
    private const string DefaultTitle = "Новый диалог";
    private static string NormalizeTitle(string title)
    {
        var normalized = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (normalized.Length == 0) return DefaultTitle;
        return normalized.Length <= 80 ? normalized : normalized[..79] + "…";
    }
    private sealed class InMemoryConversation(string title, DateTimeOffset createdAt, DateTimeOffset updatedAt, List<AssistantConversationTurn> turns)
    {
        public string Title { get; set; } = title;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
        public List<AssistantConversationTurn> Turns { get; } = turns;
    }
}

public static class ConversationTitleGenerator
{
    private const int MaxLength = 56;
    private static readonly string[] FillerPrefixes =
    [
        "пожалуйста", "подскажи пожалуйста", "подскажи", "скажи пожалуйста", "скажи",
        "можешь рассказать", "расскажи пожалуйста", "расскажи", "я хочу узнать", "хочу узнать",
    ];

    public static string FromContext(string text)
    {
        var normalized = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.', ',', '!', '?', ':', ';');
        if (normalized.Length == 0) return "Новый диалог";
        var lower = normalized.ToLowerInvariant();
        foreach (var prefix in FillerPrefixes)
            if (lower.StartsWith(prefix, StringComparison.Ordinal)
                && (lower.Length == prefix.Length || char.IsWhiteSpace(lower[prefix.Length]) || char.IsPunctuation(lower[prefix.Length])))
            {
                normalized = normalized[prefix.Length..].Trim(' ', '.', ',', '!', '?', ':', ';');
                lower = normalized.ToLowerInvariant();
                break;
            }

        var title = lower switch
        {
            _ when ContainsAny(lower, "заработ", "зарабат", "фарм", "деньг", "работ") => "Заработок и работа в GTA RP",
            _ when ContainsAny(lower, "питом", "дрессиров") => "Питомцы и дрессировка",
            _ when ContainsAny(lower, "машин", "авто", "транспорт", "штрафстоян") => "Транспорт в GTA RP",
            _ when ContainsAny(lower, "правил", "наказ", "можно ли", "наруш") => "Правила и ограничения",
            _ when ContainsAny(lower, "микрофон", "голос", "распозна") => "Голос и микрофон",
            _ => SentenceTitle(normalized),
        };
        return title.Length <= MaxLength ? title : title[..(MaxLength - 1)].TrimEnd() + "…";
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);
    private static string SentenceTitle(string value)
    {
        var end = value.IndexOfAny(['.', '!', '?', '\n', '\r']);
        if (end > 0) value = value[..end];
        value = value.Trim();
        return value.Length == 0 ? "Новый диалог" : char.ToUpperInvariant(value[0]) + value[1..];
    }
}

/// <summary>
/// Keeps the default question-answer mode transient and opens the durable store
/// only after the user explicitly enables long-term conversations.
/// </summary>
public sealed class ConfigurableAssistantConversationStore(
    Func<bool> persistenceEnabled,
    IAssistantConversationStore transientStore,
    Func<IAssistantConversationStore> persistentStoreFactory) : IAssistantConversationStore
{
    private readonly Lazy<IAssistantConversationStore> _persistentStore = new(
        persistentStoreFactory,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private IAssistantConversationStore Active => persistenceEnabled() ? _persistentStore.Value : transientStore;

    public Guid CurrentConversationId => Active.CurrentConversationId;
    public void Add(AssistantConversationTurn turn) => Active.Add(turn);
    public AssistantConversationSnapshot GetCurrent() => Active.GetCurrent();
    public AssistantConversationSnapshot GetRelevant(ConversationContextQuery query) => Active.GetRelevant(query);
    public IReadOnlyList<AssistantConversationInfo> ListConversations(int limit = 50) => Active.ListConversations(limit);
    public bool TryOpenConversation(Guid conversationId) => Active.TryOpenConversation(conversationId);
    public void RenameConversation(Guid conversationId, string title) => Active.RenameConversation(conversationId, title);
    public void DeleteConversation(Guid conversationId) => Active.DeleteConversation(conversationId);
    public void StartNewConversation() => Active.StartNewConversation();
    public void Clear() => Active.Clear();
}

public static class AssistantRequestClassifier
{
    public static AssistantRequestType Classify(string question, AssistantConversationSnapshot conversation, bool vision = false)
    {
        if (vision) return AssistantRequestType.VisionQuestion;
        var value = TranscriptDeduplicator.Normalize(question);
        if (conversation.Turns.Count > 0 && (value.StartsWith("а ") || value.StartsWith("почему") || value.StartsWith("и что") || value.Length < 45)) return AssistantRequestType.FollowUpQuestion;
        if (new[] { "не работает", "не запускается", "что проверить", "следующий шаг", "не получается", "как выполнить" }.Any(value.Contains)) return AssistantRequestType.ProblemSolving;
        if (new[] { "наруш", "можно ли", "правило", "накаж" }.Any(value.Contains)) return AssistantRequestType.RuleRiskQuestion;
        if (new[] { "сейчас", "они", "меня", "ситуац" }.Any(value.Contains)) return AssistantRequestType.CurrentSituationQuestion;
        if (question.TrimEnd().EndsWith('?')) return AssistantRequestType.DirectKnowledgeQuestion;
        return AssistantRequestType.GeneralConversation;
    }
}

public sealed record LocalAiCapabilityReport(
    bool EndpointAvailable,
    bool ModelAvailable,
    bool RussianLanguage,
    bool StructuredOutput,
    bool Grounding,
    bool Abstain,
    bool FollowUp,
    bool ContextSupported,
    TimeSpan AverageLatency,
    string Recommendation,
    IReadOnlyList<string> Warnings)
{
    public bool IsCompatible => EndpointAvailable && ModelAvailable && RussianLanguage && StructuredOutput && Grounding && Abstain && FollowUp && ContextSupported;
}
