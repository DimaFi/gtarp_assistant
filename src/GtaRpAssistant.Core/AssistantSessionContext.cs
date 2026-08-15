namespace GtaRpAssistant.Core;

public sealed record AssistantSessionSituationState(
    string? Goal,
    string? SituationId,
    string? OpenQuestion,
    IReadOnlyList<string> RecentArticleIds,
    IReadOnlyList<string> RecentFactIds,
    DateTimeOffset UpdatedAt);

public sealed record AssistantSessionContextSnapshot(string RollingSummary, AssistantSessionSituationState State);

public interface IAssistantSessionContextStore
{
    AssistantSessionContextSnapshot Get();
    void ObserveUser(string text, AssistantRequestType requestType, string? situationId, DateTimeOffset at);
    void ObserveAssistant(AssistantAnswer answer, string? situationId, DateTimeOffset at);
    void Clear();
}

public sealed class InMemoryAssistantSessionContextStore(int retainedExchanges = 3, int maximumSummaryCharacters = 600)
    : IAssistantSessionContextStore
{
    private readonly object _gate = new();
    private readonly int _retainedExchanges = Math.Clamp(retainedExchanges, 1, 6);
    private readonly int _maximumSummaryCharacters = Math.Clamp(maximumSummaryCharacters, 240, 1200);
    private readonly Queue<Exchange> _recent = new();
    private readonly Queue<string> _summarySegments = new();
    private string? _pendingQuestion;
    private AssistantSessionSituationState _state = EmptyState();

    public AssistantSessionContextSnapshot Get()
    {
        lock (_gate) return new(string.Join(Environment.NewLine, _summarySegments), _state);
    }

    public void ObserveUser(string text, AssistantRequestType requestType, string? situationId, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_pendingQuestion)) CompletePending("Ответ не был сохранён.", situationId, at);
            _pendingQuestion = Limit(text, 180);
            var goal = requestType is AssistantRequestType.FollowUpQuestion or AssistantRequestType.GeneralConversation
                ? _state.Goal ?? _pendingQuestion
                : _pendingQuestion;
            _state = _state with
            {
                Goal = goal,
                SituationId = situationId ?? _state.SituationId,
                OpenQuestion = _pendingQuestion,
                UpdatedAt = at,
            };
        }
    }

    public void ObserveAssistant(AssistantAnswer answer, string? situationId, DateTimeOffset at)
    {
        lock (_gate)
        {
            CompletePending(answer.Message, situationId, at);
            _state = _state with
            {
                SituationId = situationId ?? _state.SituationId,
                OpenQuestion = null,
                RecentArticleIds = Push(_state.RecentArticleIds, situationId, 3),
                RecentFactIds = Push(_state.RecentFactIds, answer.UsedFactIds, 8),
                UpdatedAt = at,
            };
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _recent.Clear();
            _summarySegments.Clear();
            _pendingQuestion = null;
            _state = EmptyState();
        }
    }

    private void CompletePending(string answer, string? situationId, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(_pendingQuestion)) return;
        _recent.Enqueue(new(_pendingQuestion, Limit(answer, 220), situationId, at));
        _pendingQuestion = null;
        while (_recent.Count > _retainedExchanges)
        {
            var old = _recent.Dequeue();
            _summarySegments.Enqueue($"Вопрос: {old.Question} Ответ: {old.Answer}");
        }
        while (_summarySegments.Sum(x => x.Length + Environment.NewLine.Length) > _maximumSummaryCharacters && _summarySegments.Count > 1)
            _summarySegments.Dequeue();
    }

    private static AssistantSessionSituationState EmptyState() => new(null, null, null, [], [], DateTimeOffset.MinValue);
    private static string Limit(string value, int max)
    {
        var clean = value.ReplaceLineEndings(" ").Trim();
        return clean.Length > max ? clean[..max].TrimEnd() + "…" : clean;
    }
    private static IReadOnlyList<string> Push(IReadOnlyList<string> current, string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? current : Push(current, [value], maximum);
    private static IReadOnlyList<string> Push(IReadOnlyList<string> current, IEnumerable<string> values, int maximum) =>
        current.Concat(values.Where(x => !string.IsNullOrWhiteSpace(x))).Distinct(StringComparer.Ordinal).TakeLast(maximum).ToArray();

    private sealed record Exchange(string Question, string Answer, string? SituationId, DateTimeOffset At);
}
