using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GtaRpAssistant.Core;

public sealed partial class TranscriptDeduplicator(TimeSpan? window = null, double threshold = 0.82) : ITranscriptDeduplicator
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(2);

    public bool IsDuplicate(TranscriptEntry candidate, IEnumerable<TranscriptEntry> existing)
    {
        foreach (var entry in existing)
        {
            if (entry.Source == candidate.Source || (entry.StartedAt - candidate.StartedAt).Duration() > _window) continue;
            if (Similarity(Normalize(entry.Text), Normalize(candidate.Text)) >= threshold) return true;
        }
        return false;
    }

    public static string Normalize(string text)
    {
        var normalized = NonWordRegex().Replace(text.ToLowerInvariant().Replace('ё', 'е'), " ");
        return WhitespaceRegex().Replace(normalized, " ").Trim();
    }

    private static double Similarity(string left, string right)
    {
        if (left == right) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1d - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex NonWordRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public sealed class RuleBasedIntentDetector(IEnumerable<string>? gameTerms = null, string wakeWord = "помощник") : IIntentDetector
{
    private static readonly string[] CandidatePhrases = ["как сделать", "что делать", "что дальше", "где взять", "почему не работает", "почему не запускается", "сколько нужно", "какие условия", "я не понимаю", "подскажи", "помощник"];
    private readonly HashSet<string> _terms = new(gameTerms ?? ["контракт", "семья", "gta", "сервер"], StringComparer.OrdinalIgnoreCase);
    public string WakeWord { get; set; } = wakeWord;
    public ProactiveMode Mode { get; set; } = ProactiveMode.Strict;

    public Task<IntentDecision> DetectAsync(TranscriptContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = context.CurrentUserRequest;
        if (current is null) return Task.FromResult(new IntentDecision(false, null, 0, false, false, "Нет текущего запроса"));
        if (current.Source != AudioSourceKind.UserMicrophone) return Task.FromResult(new IntentDecision(false, null, 0, false, false, "Game audio используется только как контекст"));
        var text = TranscriptDeduplicator.Normalize(current.Text);
        var wake = text.Contains(WakeWord, StringComparison.OrdinalIgnoreCase);
        var candidate = CandidatePhrases.Any(text.Contains)
            || (text.Contains("почему", StringComparison.Ordinal) && (text.Contains("не запуска", StringComparison.Ordinal) || text.Contains("не работа", StringComparison.Ordinal)))
            || current.Text.TrimEnd().EndsWith('?');
        if (Mode is ProactiveMode.Balanced or ProactiveMode.Experimental)
            candidate |= text.Contains("не получается", StringComparison.Ordinal) || text.Contains("что то не работает", StringComparison.Ordinal);
        if (Mode == ProactiveMode.Experimental)
            candidate |= text.Contains("застрял", StringComparison.Ordinal) || text.Contains("не могу", StringComparison.Ordinal);
        var gameRelated = _terms.Any(text.Contains) || context.Entries.Any(x => _terms.Any(t => x.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));
        var should = candidate && (gameRelated || wake);
        var intent = text.Contains("контракт", StringComparison.Ordinal) ? "family_contract_problem" : should ? "game_help" : null;
        return Task.FromResult(new IntentDecision(should, intent, should ? (wake ? 0.95 : 0.86) : 0.1, wake, false, should ? "Явный игровой вопрос пользователя" : "Нет явного игрового запроса"));
    }
}

public sealed class ContextSelector : IContextSelector
{
    public TranscriptContext Select(IEnumerable<TranscriptEntry> entries, TranscriptEntry current, int maxCharacters = 2000)
    {
        var all = entries.Where(x => x.StartedAt <= current.EndedAt)
            .OrderByDescending(x => x.StartedAt)
            .DistinctBy(x => $"{x.Source}:{TranscriptDeduplicator.Normalize(x.Text)}")
            .ToArray();
        var terms = Terms(current.Text);
        var primaryCutoff = current.StartedAt - TimeSpan.FromSeconds(90);
        var recentCutoff = current.StartedAt - TimeSpan.FromSeconds(30);
        var selected = all.Where(x => x.Id == current.Id || x.EndedAt >= recentCutoff || (x.EndedAt >= primaryCutoff && Terms(x.Text).Overlaps(terms))).ToList();
        if (selected.Sum(x => x.Text.Length) < Math.Min(500, maxCharacters / 2))
        {
            var extendedCutoff = current.StartedAt - TimeSpan.FromMinutes(3);
            selected.AddRange(all.Where(x => x.EndedAt >= extendedCutoff && Terms(x.Text).Overlaps(terms)));
        }
        var chars = 0;
        selected = selected.DistinctBy(x => x.Id).OrderByDescending(x => x.StartedAt)
            .Where(x => (chars += x.Text.Length) <= maxCharacters).OrderBy(x => x.StartedAt).ToList();
        return new TranscriptContext(selected, current);
    }

    private static HashSet<string> Terms(string text) => TranscriptDeduplicator.Normalize(text)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length >= 4)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed class AiRouter : IAiRouter
{
    public AnswerRoute Select(AiRoutingContext c) => c.HasVerifiedPreparedAnswer ? AnswerRoute.Deterministic
        : !c.HasSufficientGrounding ? AnswerRoute.Abstain
        : c.ConfiguredRouteAvailable ? AnswerRoute.ConfiguredChat
        : c.LocalAvailable ? AnswerRoute.LocalChat
        : c.CloudAvailable && c.UserAllowsCloud ? AnswerRoute.CloudChat
        : AnswerRoute.Deterministic;
}

public static partial class AssistantQuestionPolicy
{
    public static bool TryGetBlockReason(string question, out string reason)
    {
        var normalized = TranscriptDeduplicator.Normalize(question);
        if (ForbiddenRequestRegex().IsMatch(normalized))
        {
            reason = "Запрос предлагает запрещённую автоматизацию или вмешательство в игру";
            return true;
        }
        if (UnverifiablePredictionRegex().IsMatch(normalized))
        {
            reason = "Запрос требует непроверяемого предсказания";
            return true;
        }
        reason = "";
        return false;
    }

    [GeneratedRegex(@"\b(автокликер|инжект|dll\s*инжект|читать\s+память|автоматически\s+нажимать|бот\s+для\s+(?:фарма|стрельбы|вождения))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenRequestRegex();

    [GeneratedRegex(@"\b(?:кто\s+(?:выиграет|победит)|предскажи\s+(?:исход|победителя))\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnverifiablePredictionRegex();
}

public sealed partial class GroundedAnswerValidator
{
    public const string PassedReason = "Ответ прошёл проверку";
    public const string SafeAbstainTitle = "Недостаточно информации";
    public const string SafeAbstainMessage = "Недостаточно данных для точной подсказки.";
    private const string CommunityPrefix = "По данным игроков:";

    public AssistantAnswer Validate(string json, KnowledgeMatch knowledge, string server, bool voiceEnabled)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<GroundedAnswerPayload>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null) return Abstain("Пустой JSON");
            var decision = payload.Decision.Trim().ToLowerInvariant() switch
            {
                "show" => AnswerDecision.Show,
                "clarify" or "ask_for_more_information" or "escalate" => AnswerDecision.AskForMoreInformation,
                "abstain" => AnswerDecision.Abstain,
                _ => (AnswerDecision?)null,
            };
            if (decision is null) return Abstain("Недопустимое decision");
            if (string.IsNullOrWhiteSpace(payload.Title) || string.IsNullOrWhiteSpace(payload.Message)) return Abstain("Обязательные поля ответа не заполнены");
            if (decision == AnswerDecision.Abstain)
            {
                if (!IsCanonicalAbstain(payload)) return Abstain("Воздержание должно использовать безопасный локальный текст");
                return new(AnswerDecision.Abstain, SafeAbstainTitle, SafeAbstainMessage, [], null, null, false, PassedReason);
            }
            if (!knowledge.Facts.Any(x => x.Verified))
            {
                return Abstain("Без проверенных фактов разрешено только безопасное воздержание");
            }
            if (knowledge.HasConflict || knowledge.IsOutdated) return Abstain("Конфликт или устаревший источник");
            var facts = knowledge.Facts.ToDictionary(x => x.Id, StringComparer.Ordinal);
            if (payload.UsedFactIds is null || payload.UsedFactIds.Any(id => !facts.ContainsKey(id))) return Abstain("Неизвестный fact ID");
            var used = payload.UsedFactIds.Select(id => facts[id]).ToArray();
            if (decision == AnswerDecision.Show && (used.Length == 0 || used.Any(x => !x.Verified))) return Abstain("Нет проверенного grounding");
            if (used.Any(x => x.ServerScope != "all" && !x.ServerScope.Equals(server, StringComparison.OrdinalIgnoreCase))) return Abstain("Неверный сервер");

            var message = payload.Message.Trim();
            if (used.Any(x => x.Text.StartsWith(CommunityPrefix, StringComparison.OrdinalIgnoreCase))
                && !message.StartsWith(CommunityPrefix, StringComparison.OrdinalIgnoreCase)) message = $"{CommunityPrefix} {message}";
            if (message.Length > 350 || payload.Title.Length > 120) return Abstain("Некорректная длина ответа");

            var steps = NormalizeList(payload.Steps, 5, 240);
            var causes = NormalizeList(payload.PossibleCauses, 4, 240);
            var followUps = NormalizeList(payload.FollowUpSuggestions, 4, 180);
            if (steps is null || causes is null || followUps is null) return Abstain("Некорректная структура problem solving");
            var summary = payload.Summary?.Trim() ?? "";
            if (summary.Length > 300) return Abstain("Некорректная длина summary");

            var factText = string.Join(' ', used.Select(x => x.Text));
            var userStrings = new[] { payload.Title, message, summary }.Concat(steps).Concat(causes).Concat(followUps).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (userStrings.SelectMany(x => NumberRegex().Matches(x)).Select(x => x.Value).Any(n => !factText.Contains(n, StringComparison.Ordinal))) return Abstain("Неподтверждённое число");
            if (userStrings.SelectMany(x => UrlRegex().Matches(x)).Select(x => x.Value.TrimEnd('.', ',', ')')).Any(url => !factText.Contains(url, StringComparison.OrdinalIgnoreCase))) return Abstain("Неподтверждённый URL");
            if (userStrings.Any(x => ForbiddenAutomationRegex().IsMatch(x))) return Abstain("Ответ предлагает запрещённую автоматизацию");
            if (causes.Any(x => CategoricalBlameRegex().IsMatch(x))) return Abstain("Неподтверждённое категоричное обвинение");

            var problem = steps.Count > 0 || causes.Count > 0 || !string.IsNullOrWhiteSpace(summary)
                ? new ProblemSolutionDetails(summary, steps, causes, payload.NeedsMoreInformation || decision == AnswerDecision.AskForMoreInformation, payload.NeedsVisualContext || payload.NeedsScreen, followUps)
                : null;
            return new AssistantAnswer(decision.Value, payload.Title.Trim(), message, payload.UsedFactIds, knowledge.Title,
                used.Select(x => (DateTimeOffset?)x.UpdatedAt).Max(), voiceEnabled && payload.CanSpeak, PassedReason, problem);
        }
        catch (JsonException) { return Abstain("Невалидный JSON"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Abstain("Ошибка валидации"); }
    }

    private static IReadOnlyList<string>? NormalizeList(IReadOnlyList<string>? source, int maxCount, int maxLength)
    {
        if (source is null) return [];
        if (source.Count > maxCount || source.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > maxLength)) return null;
        return source.Select(x => x.Trim()).ToArray();
    }

    private static bool IsCanonicalAbstain(GroundedAnswerPayload payload) =>
        payload.UsedFactIds is { Count: 0 }
        && payload.Title.Trim().Equals(SafeAbstainTitle, StringComparison.Ordinal)
        && payload.Message.Trim().Equals(SafeAbstainMessage, StringComparison.Ordinal)
        && string.IsNullOrWhiteSpace(payload.Summary)
        && (payload.Steps is null || payload.Steps.Count == 0)
        && (payload.PossibleCauses is null || payload.PossibleCauses.Count == 0)
        && (payload.FollowUpSuggestions is null || payload.FollowUpSuggestions.Count == 0)
        && !payload.NeedsScreen
        && !payload.CanSpeak
        && !payload.NeedsMoreInformation
        && !payload.NeedsVisualContext;

    private static AssistantAnswer Abstain(string reason) => new(AnswerDecision.Abstain, SafeAbstainTitle, SafeAbstainMessage, [], null, null, false, reason);
    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
    [GeneratedRegex(@"\b(макрос|автокликер|инжект|внедр(?:ение|иться)|читать\s+память|автоматически\s+наж(?:ать|имать)|бот\s+для)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenAutomationRegex();
    [GeneratedRegex(@"\b(точно|однозначно|безусловно)\s+(виноват|обманул|нарушил|украл|читер)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CategoricalBlameRegex();
}
