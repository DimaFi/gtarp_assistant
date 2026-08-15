namespace GtaRpAssistant.Core;

public static class AssistantTokenEstimator
{
    // Russian text is commonly denser in tokens than English. Three UTF-16 characters
    // per token plus a fixed policy/schema allowance is deliberately conservative.
    private const int FixedPolicyAndSchemaTokens = 700;

    public static int EstimateInput(GroundedAnswerRequest request)
    {
        var characters = request.Question.Length
            + request.Server.Length
            + request.TranscriptContext.Length
            + request.VerifiedFacts.Sum(x => x.Id.Length + x.Text.Length + x.ServerScope.Length)
            + (request.Conversation?.Sum(x => x.Text.Length + x.UsedFactIds.Sum(id => id.Length)) ?? 0)
            + (request.ConversationSummary?.Length ?? 0)
            + (request.SessionState is null ? 0 : (request.SessionState.Goal?.Length ?? 0) + (request.SessionState.SituationId?.Length ?? 0)
                + (request.SessionState.OpenQuestion?.Length ?? 0) + request.SessionState.RecentArticleIds.Sum(x => x.Length)
                + request.SessionState.RecentFactIds.Sum(x => x.Length))
            + (request.Personalization?.Memories.Sum(x => x.Content.Length + x.Category.ToString().Length) ?? 0)
            + (request.InvalidResponse?.Length ?? 0);

        return FixedPolicyAndSchemaTokens + (int)Math.Ceiling(characters / 3d);
    }

    public static int EstimateOutputBudget(GroundedAnswerRequest request) => request.MaxOutputTokens
        ?? (request.RequestType == AssistantRequestType.ProblemSolving ? 700 : 420);
}
