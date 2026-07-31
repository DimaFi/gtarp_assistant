namespace GtaRpAssistant.Core;

public static class GroundingContextSelector
{
    public const int DefaultMaxFacts = 6;
    public const int DefaultMaxCharacters = 1600;

    public static IReadOnlyList<KnowledgeFact> Select(string question, IEnumerable<KnowledgeFact> facts, int maxFacts = DefaultMaxFacts, int maxCharacters = DefaultMaxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFacts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        var terms = Terms(question);
        var candidates = facts.Where(x => x.Verified)
            .Select((fact, index) => new { Fact = fact, Index = index, Score = Terms(fact.Text).Count(terms.Contains) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index);

        var result = new List<KnowledgeFact>(maxFacts);
        var characters = 0;
        foreach (var candidate in candidates)
        {
            if (result.Count >= maxFacts) break;
            if (characters + candidate.Fact.Text.Length > maxCharacters) continue;
            result.Add(candidate.Fact);
            characters += candidate.Fact.Text.Length;
        }
        return result;
    }

    private static HashSet<string> Terms(string text) => TranscriptDeduplicator.Normalize(text)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length >= 3)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
