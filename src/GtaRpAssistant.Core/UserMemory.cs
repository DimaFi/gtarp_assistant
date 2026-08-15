using System.Text.RegularExpressions;

namespace GtaRpAssistant.Core;

public enum UserMemoryCategory { PlayStyle, ExplainedTopic, FavoriteActivity, CommunicationPreference, ConfirmedFact }

public sealed record UserMemoryItem(
    Guid Id,
    UserMemoryCategory Category,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PersonalityProfile(
    int DetailLevel = 1,
    int HumorLevel = 0,
    int InitiativeLevel = 1,
    int Tone = 0,
    bool AdaptiveEnabled = false)
{
    public PersonalityProfile Normalize() => new(
        Math.Clamp(DetailLevel, 0, 2),
        Math.Clamp(HumorLevel, 0, 2),
        Math.Clamp(InitiativeLevel, 0, 2),
        Math.Clamp(Tone, 0, 2),
        AdaptiveEnabled);
}

public sealed record PersonalityChange(Guid Id, DateTimeOffset CreatedAt, string Trait, int OldValue, int NewValue, string Reason);

public sealed record UserPersonalizationContext(
    IReadOnlyList<UserMemoryItem> Memories,
    PersonalityProfile Personality);

public sealed record UserMemoryCandidate(
    Guid Id,
    UserMemoryCategory Category,
    string Content,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public interface IUserMemoryCandidateService
{
    UserMemoryCandidate? Observe(string userText, DateTimeOffset at);
    IReadOnlyList<UserMemoryCandidate> List(DateTimeOffset at);
    UserMemoryItem? Approve(Guid id, DateTimeOffset at);
    bool Reject(Guid id);
    void Clear();
}

public interface IUserMemoryStore
{
    IReadOnlyList<UserMemoryItem> List();
    UserMemoryItem Upsert(Guid? id, UserMemoryCategory category, string content);
    void Delete(Guid id);
    void Clear();
    PersonalityProfile GetPersonality();
    void SavePersonality(PersonalityProfile profile);
    IReadOnlyList<PersonalityChange> ListPersonalityChanges(int limit = 50);
    void AddPersonalityChange(PersonalityChange change);
    void ClearPersonalityChanges();
    void ResetPersonality();
}

public interface IUserPersonalizationContextProvider
{
    bool ApplyExplicitFeedback(string userText);
    UserPersonalizationContext Build(string question, int maxMemories = 8);
}

public sealed class UserPersonalizationContextProvider(IUserMemoryStore store) : IUserPersonalizationContextProvider
{
    public bool ApplyExplicitFeedback(string userText)
    {
        var profile = store.GetPersonality().Normalize();
        if (!profile.AdaptiveEnabled || string.IsNullOrWhiteSpace(userText)) return false;
        var text = userText.Trim().ToLowerInvariant();
        var updated = profile;
        var changes = new List<(string Trait, int OldValue, int NewValue, string Reason)>();

        if (ContainsAny(text, "отвечай короче", "пиши короче", "будь кратче", "покороче"))
            updated = Change(profile, profile with { DetailLevel = Math.Max(0, profile.DetailLevel - 1) }, "detail", profile.DetailLevel, Math.Max(0, profile.DetailLevel - 1), "Явная просьба отвечать короче", changes);
        else if (ContainsAny(text, "отвечай подробнее", "пиши подробнее", "объясняй подробнее", "больше деталей"))
            updated = Change(profile, profile with { DetailLevel = Math.Min(2, profile.DetailLevel + 1) }, "detail", profile.DetailLevel, Math.Min(2, profile.DetailLevel + 1), "Явная просьба отвечать подробнее", changes);
        else if (ContainsAny(text, "без шуток", "не шути", "говори серьезно", "говори серьёзно"))
        {
            updated = profile with { HumorLevel = 0, Tone = 2 };
            AddChange("humor", profile.HumorLevel, 0, "Явная просьба говорить без шуток", changes);
            AddChange("tone", profile.Tone, 2, "Явная просьба говорить серьёзно", changes);
        }
        else if (ContainsAny(text, "можешь шутить", "добавь юмора", "больше юмора"))
            updated = Change(profile, profile with { HumorLevel = Math.Min(2, profile.HumorLevel + 1) }, "humor", profile.HumorLevel, Math.Min(2, profile.HumorLevel + 1), "Явная просьба добавить юмора", changes);
        else if (ContainsAny(text, "не предлагай лишнего", "только отвечай", "без лишних советов"))
            updated = Change(profile, profile with { InitiativeLevel = 0 }, "initiative", profile.InitiativeLevel, 0, "Явная просьба не добавлять инициативные советы", changes);
        else if (ContainsAny(text, "предлагай варианты", "предлагай следующие шаги", "будь инициативнее"))
            updated = Change(profile, profile with { InitiativeLevel = Math.Min(2, profile.InitiativeLevel + 1) }, "initiative", profile.InitiativeLevel, Math.Min(2, profile.InitiativeLevel + 1), "Явная просьба предлагать следующие шаги", changes);

        if (changes.Count == 0) return false;
        store.SavePersonality(updated.Normalize());
        foreach (var change in changes)
            store.AddPersonalityChange(new(Guid.NewGuid(), DateTimeOffset.UtcNow, change.Trait, change.OldValue, change.NewValue, change.Reason));
        return true;
    }

    public UserPersonalizationContext Build(string question, int maxMemories = 8)
    {
        var terms = question.Split(new[] { ' ', '\t', '\r', '\n', ',', '.', '?', '!', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gameContext = ContainsAny(question.ToLowerInvariant(), "gta", "гта", "rp", "рп", "игр", "фарм", "заработ", "работ", "рыбал", "дальноб", "контракт", "сервер");
        var selected = store.List()
            .Select(x => new
            {
                Item = x,
                Score = terms.Count(t => x.Content.Contains(t, StringComparison.OrdinalIgnoreCase)) * 10
                    + (x.Category == UserMemoryCategory.CommunicationPreference ? 2 : 0)
                    + (gameContext && x.Category is UserMemoryCategory.PlayStyle or UserMemoryCategory.FavoriteActivity ? 1 : 0),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.UpdatedAt)
            .Take(Math.Clamp(maxMemories, 1, 12))
            .Select(x => x.Item).ToArray();
        return new(selected, store.GetPersonality().Normalize());
    }

    private static bool ContainsAny(string text, params string[] phrases) => phrases.Any(text.Contains);
    private static PersonalityProfile Change(PersonalityProfile current, PersonalityProfile updated, string trait, int oldValue, int newValue, string reason, List<(string, int, int, string)> changes) { AddChange(trait, oldValue, newValue, reason, changes); return updated; }
    private static void AddChange(string trait, int oldValue, int newValue, string reason, List<(string Trait, int OldValue, int NewValue, string Reason)> changes) { if (oldValue != newValue) changes.Add((trait, oldValue, newValue, reason)); }
}

public sealed partial class UserMemoryCandidateService(IUserMemoryStore store, int maximumCandidates = 12) : IUserMemoryCandidateService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private readonly object _gate = new();
    private readonly List<UserMemoryCandidate> _candidates = [];
    private readonly int _maximumCandidates = Math.Clamp(maximumCandidates, 1, 30);

    public UserMemoryCandidate? Observe(string userText, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(userText) || SensitiveRegex().IsMatch(userText)) return null;
        var extracted = Extract(userText);
        if (extracted is null) return null;
        var (category, content, reason) = extracted.Value;
        if (content.Length is < 3 or > 180) return null;

        lock (_gate)
        {
            RemoveExpired(at);
            if (store.List().Any(x => string.Equals(x.Content, content, StringComparison.OrdinalIgnoreCase))) return null;
            var existing = _candidates.FirstOrDefault(x => string.Equals(x.Content, content, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            var candidate = new UserMemoryCandidate(Guid.NewGuid(), category, content, reason, at, at.Add(Lifetime));
            _candidates.Add(candidate);
            if (_candidates.Count > _maximumCandidates) _candidates.RemoveRange(0, _candidates.Count - _maximumCandidates);
            return candidate;
        }
    }

    public IReadOnlyList<UserMemoryCandidate> List(DateTimeOffset at)
    {
        lock (_gate)
        {
            RemoveExpired(at);
            return _candidates.OrderByDescending(x => x.CreatedAt).ToArray();
        }
    }

    public UserMemoryItem? Approve(Guid id, DateTimeOffset at)
    {
        lock (_gate)
        {
            RemoveExpired(at);
            var candidate = _candidates.FirstOrDefault(x => x.Id == id);
            if (candidate is null) return null;
            _candidates.Remove(candidate);
            return store.Upsert(null, candidate.Category, candidate.Content);
        }
    }

    public bool Reject(Guid id)
    {
        lock (_gate)
        {
            var candidate = _candidates.FirstOrDefault(x => x.Id == id);
            return candidate is not null && _candidates.Remove(candidate);
        }
    }

    public void Clear() { lock (_gate) _candidates.Clear(); }

    private void RemoveExpired(DateTimeOffset at) => _candidates.RemoveAll(x => x.ExpiresAt <= at);

    private static (UserMemoryCategory Category, string Content, string Reason)? Extract(string text)
    {
        var compact = Regex.Replace(text.ReplaceLineEndings(" ").Trim(), @"\s+", " ");
        var direct = DirectPreferenceRegex().Match(compact);
        if (direct.Success)
        {
            var value = Clean(direct.Groups["value"].Value);
            var verb = direct.Groups["verb"].Value.ToLowerInvariant();
            return verb switch
            {
                "люблю" or "обожаю" => (UserMemoryCategory.FavoriteActivity, $"Любит {value}", "Явно названо любимое занятие"),
                "предпочитаю" => (UserMemoryCategory.PlayStyle, $"Предпочитает {value}", "Явно названо предпочтение"),
                _ => (UserMemoryCategory.PlayStyle, $"Не любит {value}", "Явно названо нежелательное занятие"),
            };
        }
        var personal = PersonalPreferenceRegex().Match(compact);
        if (personal.Success)
        {
            var value = Clean(personal.Groups["value"].Value);
            var verb = personal.Groups["verb"].Value.ToLowerInvariant();
            return verb == "нравится"
                ? (UserMemoryCategory.FavoriteActivity, $"Нравится {value}", "Явно названо любимое занятие")
                : (UserMemoryCategory.PlayStyle, $"Надоел {value}", "Явно названо нежелательное занятие");
        }
        var reverse = ReversePersonalPreferenceRegex().Match(compact);
        if (reverse.Success)
        {
            var value = Clean(reverse.Groups["value"].Value);
            var verb = reverse.Groups["verb"].Value.ToLowerInvariant();
            return verb == "нравится"
                ? (UserMemoryCategory.FavoriteActivity, $"Нравится {value}", "Явно названо любимое занятие")
                : (UserMemoryCategory.PlayStyle, $"Надоел {value}", "Явно названо нежелательное занятие");
        }
        var remember = RememberRegex().Match(compact);
        if (!remember.Success) return null;
        var remembered = Clean(remember.Groups["value"].Value);
        if (!FirstPersonRegex().IsMatch(remembered)) return null;
        return (UserMemoryCategory.ConfirmedFact, remembered, "Пользователь явно попросил запомнить это о себе");
    }

    private static string Clean(string value) => value.Trim(' ', '.', ',', '!', '?', ';', ':', '-', '—');

    [GeneratedRegex(@"\bя\s+(?<verb>люблю|обожаю|предпочитаю|не\s+люблю)\s+(?<value>[^,.!?;]{2,180})", RegexOptions.IgnoreCase)]
    private static partial Regex DirectPreferenceRegex();
    [GeneratedRegex(@"\bмне\s+(?<verb>нравится|надоел|надоела|надоело)\s+(?<value>[^,.!?;]{2,180})", RegexOptions.IgnoreCase)]
    private static partial Regex PersonalPreferenceRegex();
    [GeneratedRegex(@"\b(?<value>[\p{L}\d_-]+)\s+мне\s+(?<verb>нравится|надоел|надоела|надоело)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReversePersonalPreferenceRegex();
    [GeneratedRegex(@"\bзапомни(?:те)?(?:,?\s+пожалуйста)?(?:,?\s+что)?\s+(?<value>[^.!?]{3,180})", RegexOptions.IgnoreCase)]
    private static partial Regex RememberRegex();
    [GeneratedRegex(@"\b(?:я|мне|мой|моя|моё|мои)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstPersonRegex();
    [GeneratedRegex(@"\b(?:парол[ьяеи]*|api[\s_-]*key|токен\p{L}*|секрет\p{L}*|телефон\p{L}*|адрес\p{L}*|почт\p{L}*|email|e-mail|паспорт\p{L}*|банковск\p{L}*|карт[аы]\p{L}*|cvv)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveRegex();
}
