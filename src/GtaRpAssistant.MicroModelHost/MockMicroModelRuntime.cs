using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.MicroModelHost;

public sealed class MockMicroModelRuntime
{
    public Task<MicroModelResponse> GenerateAsync(MicroModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verified = request.VerifiedFacts.Where(fact => fact.Verified).Take(8).ToArray();
        object payload;
        if (verified.Length == 0)
        {
            payload = new
            {
                decision = "abstain",
                presentationType = "context_answer",
                title = "Недостаточно информации",
                message = "Недостаточно подтверждённых фактов для точного ответа.",
                usedFactIds = Array.Empty<string>(),
                evidenceTranscriptIds = request.Transcript.Take(6).Select(entry => entry.Id).ToArray(),
                confidence = 0.0,
                needsVisualContext = false,
                needsSmartModel = false,
            };
        }
        else
        {
            var fact = verified[0];
            var message = fact.Text.Length <= 350 ? fact.Text : fact.Text[..350];
            payload = new
            {
                decision = "show",
                presentationType = "mechanic_help",
                title = "Тестовая grounded-подсказка",
                message,
                usedFactIds = new[] { fact.Id },
                evidenceTranscriptIds = request.Transcript.Take(6).Select(entry => entry.Id).ToArray(),
                confidence = 1.0,
                needsVisualContext = false,
                needsSmartModel = false,
            };
        }

        var json = JsonSerializer.Serialize(payload);
        using var _ = JsonDocument.Parse(json);
        return Task.FromResult(new MicroModelResponse(json));
    }
}
