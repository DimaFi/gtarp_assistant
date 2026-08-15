using System.Text;

namespace GtaRpAssistant.Core;

public sealed record AssistantContextBudget(
    int TargetInputTokens = 1600,
    int MaximumFacts = 6,
    int FactsCharacters = 1200,
    int TranscriptCharacters = 450,
    int ConversationCharacters = 480,
    int SummaryCharacters = 360,
    int MemoryCharacters = 240,
    int DefaultOutputTokens = 300,
    int ProblemSolvingOutputTokens = 450)
{
    public AssistantContextBudget Normalize() => new(
        Math.Clamp(TargetInputTokens, 800, 4096),
        Math.Clamp(MaximumFacts, 1, 12),
        Math.Clamp(FactsCharacters, 240, 2400),
        Math.Clamp(TranscriptCharacters, 0, 1200),
        Math.Clamp(ConversationCharacters, 0, 1800),
        Math.Clamp(SummaryCharacters, 0, 900),
        Math.Clamp(MemoryCharacters, 0, 600),
        Math.Clamp(DefaultOutputTokens, 80, 500),
        Math.Clamp(ProblemSolvingOutputTokens, 120, 700));
}

public sealed record AssistantContextBuildRequest(
    string Question,
    string Server,
    KnowledgeMatch Match,
    TranscriptContext Transcript,
    AssistantRequestType RequestType,
    IReadOnlyList<AssistantConversationTurn> Conversation,
    UserPersonalizationContext? Personalization,
    AssistantSessionContextSnapshot? SessionContext = null,
    AssistantResponseMode ResponseMode = AssistantResponseMode.GroundedKnowledge);

public sealed record AssistantContextBuildResult(
    GroundedAnswerRequest Request,
    AssistantContextBudget Budget,
    bool WasTrimmed,
    int EstimatedInputTokens);

public interface IAssistantContextBuilder
{
    AssistantContextBuildResult Build(AssistantContextBuildRequest request);
}

public sealed class AssistantContextBuilder(AssistantContextBudget? budget = null) : IAssistantContextBuilder
{
    private readonly AssistantContextBudget _budget = (budget ?? new()).Normalize();

    public AssistantContextBuildResult Build(AssistantContextBuildRequest input)
    {
        var facts = GroundingContextSelector.Select(input.Question, input.Match.Facts, _budget.MaximumFacts, _budget.FactsCharacters);
        var transcript = BuildTranscript(input.Transcript, _budget.TranscriptCharacters, out var transcriptTrimmed);
        var conversation = TakeRecent(input.Conversation, _budget.ConversationCharacters, out var conversationTrimmed);
        var personalization = LimitPersonalization(input.Personalization, _budget.MemoryCharacters, out var memoryTrimmed);
        var summary = LimitText(input.SessionContext?.RollingSummary, _budget.SummaryCharacters, out var summaryTrimmed);
        var outputTokens = input.RequestType == AssistantRequestType.ProblemSolving
            ? _budget.ProblemSolvingOutputTokens
            : _budget.DefaultOutputTokens;

        var request = new GroundedAnswerRequest(
            input.Question,
            facts,
            input.Server,
            transcript,
            input.RequestType,
            conversation,
            personalization,
            MaxOutputTokens: outputTokens,
            ConversationSummary: summary,
            SessionState: input.SessionContext?.State,
            ResponseMode: input.ResponseMode);
        var estimated = AssistantTokenEstimator.EstimateInput(request);
        return new(request, _budget, transcriptTrimmed || conversationTrimmed || memoryTrimmed || summaryTrimmed
            || facts.Count < input.Match.Facts.Count(x => x.Verified), estimated);
    }

    private static string BuildTranscript(TranscriptContext context, int maxCharacters, out bool trimmed)
    {
        var lines = context.Entries.Where(x => context.CurrentUserRequest is null || x.Id != context.CurrentUserRequest.Id)
            .Select(x => $"[{x.Source}] {x.Text.ReplaceLineEndings(" ").Trim()}").Where(x => x.Length > 0).ToArray();
        return TakeRecentText(lines, maxCharacters, out trimmed);
    }

    private static IReadOnlyList<AssistantConversationTurn> TakeRecent(
        IReadOnlyList<AssistantConversationTurn> turns, int maxCharacters, out bool trimmed)
    {
        if (maxCharacters <= 0) { trimmed = turns.Count > 0; return []; }
        var selected = new List<AssistantConversationTurn>();
        var used = 0;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            var turn = turns[i];
            if (turn.Text.Length + used > maxCharacters) break;
            selected.Add(turn);
            used += turn.Text.Length;
            if (selected.Count >= 6) break;
        }
        selected.Reverse();
        trimmed = selected.Count < turns.Count;
        return selected;
    }

    private static UserPersonalizationContext? LimitPersonalization(
        UserPersonalizationContext? context, int maxCharacters, out bool trimmed)
    {
        if (context is null) { trimmed = false; return null; }
        var selected = new List<UserMemoryItem>();
        var used = 0;
        foreach (var memory in context.Memories)
        {
            if (selected.Count >= 3 || memory.Content.Length + used > maxCharacters) break;
            selected.Add(memory);
            used += memory.Content.Length;
        }
        trimmed = selected.Count < context.Memories.Count;
        return new(selected, context.Personality.Normalize());
    }

    private static string TakeRecentText(IReadOnlyList<string> lines, int maxCharacters, out bool trimmed)
    {
        if (maxCharacters <= 0) { trimmed = lines.Count > 0; return ""; }
        var selected = new List<string>();
        var used = 0;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.Length + used > maxCharacters) break;
            selected.Add(line);
            used += line.Length + Environment.NewLine.Length;
        }
        selected.Reverse();
        trimmed = selected.Count < lines.Count;
        var builder = new StringBuilder(Math.Min(maxCharacters, used));
        foreach (var line in selected) builder.AppendLine(line);
        return builder.ToString().TrimEnd();
    }

    private static string? LimitText(string? value, int maxCharacters, out bool trimmed)
    {
        if (string.IsNullOrWhiteSpace(value)) { trimmed = false; return null; }
        var clean = value.ReplaceLineEndings(" ").Trim();
        if (maxCharacters <= 0) { trimmed = true; return null; }
        trimmed = clean.Length > maxCharacters;
        return trimmed ? clean[^maxCharacters..].TrimStart() : clean;
    }
}
