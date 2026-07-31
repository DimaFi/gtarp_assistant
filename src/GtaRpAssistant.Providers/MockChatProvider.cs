using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Providers;

public sealed class MockChatProvider : IChatProvider
{
    public string Id => "mock";
    public ProviderKind Kind => ProviderKind.BuiltIn;
    public ProviderCapabilities Capabilities => new() { SupportsTextInput = true, SupportsChat = true, SupportsStructuredOutput = true, SupportsJsonMode = true, IsLocal = true };
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderHealth(true, "Mock готов"));
    public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelInfo>>([new("mock", "Mock")]);

    public Task<GroundedAnswerResponse> CreateGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fact = request.VerifiedFacts.FirstOrDefault();
        var payload = fact is null
            ? new { decision = "abstain", title = "Недостаточно информации", message = "Недостаточно данных для точной подсказки.", usedFactIds = Array.Empty<string>(), needsScreen = false, canSpeak = false }
            : new { decision = "show", title = "Подсказка", message = fact.Text, usedFactIds = new[] { fact.Id }, needsScreen = false, canSpeak = false };
        return Task.FromResult(new GroundedAnswerResponse(JsonSerializer.Serialize(payload)));
    }
}
