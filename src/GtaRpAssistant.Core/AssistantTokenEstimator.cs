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
            + (request.Personalization?.Memories.Sum(x => x.Content.Length + x.Category.ToString().Length) ?? 0)
            + (request.InvalidResponse?.Length ?? 0);

        return FixedPolicyAndSchemaTokens + (int)Math.Ceiling(characters / 3d);
    }

    public static int EstimateOutputBudget(AssistantRequestType requestType) =>
        requestType == AssistantRequestType.ProblemSolving ? 700 : 420;
}
