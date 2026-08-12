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
        var selected = store.List()
            .Select(x => new { Item = x, Score = terms.Count(t => x.Content.Contains(t, StringComparison.OrdinalIgnoreCase)) })
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
