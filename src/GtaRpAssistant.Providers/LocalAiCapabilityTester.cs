using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Providers;

public sealed class LocalAiCapabilityTester : ILocalAiCapabilityTester
{
    public async Task<LocalAiCapabilityReport> TestAsync(IChatProvider provider, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var health = await provider.CheckHealthAsync(cancellationToken);
        if (!health.IsAvailable) return new(false, false, false, false, false, false, false, false, TimeSpan.Zero, "Модель недоступна", [health.Message]);

        var fact = new KnowledgeFact("cap.fact.1", "cap.article", "Для теста совместимости подтверждён следующий шаг: остановиться и уточнить требования.", true, DateTimeOffset.UtcNow, "all");
        var elapsed = new List<TimeSpan>();
        CapabilitySample grounded = await RunAsync(provider, new("Что делать?", [fact], "all", "", AssistantRequestType.ProblemSolving), elapsed, warnings, cancellationToken);
        CapabilitySample abstain = await RunAsync(provider, new("Назови скрытую награду", [], "all", "", AssistantRequestType.DirectKnowledgeQuestion), elapsed, warnings, cancellationToken);
        var prior = new AssistantConversationTurn(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.Assistant, grounded.Payload?.Message ?? "Остановитесь и уточните требования.", provider.Id, null, grounded.Payload?.UsedFactIds ?? [], "cap");
        CapabilitySample followUp = await RunAsync(provider, new("А почему?", [fact], "all", "", AssistantRequestType.FollowUpQuestion, [prior]), elapsed, warnings, cancellationToken);

        var structured = grounded.HasFullSchema && abstain.HasFullSchema && followUp.HasFullSchema;
        var russian = grounded.Payload is not null && Regex.IsMatch($"{grounded.Payload.Title} {grounded.Payload.Message} {grounded.Payload.Summary}", @"\p{IsCyrillic}");
        var groundedValidation = grounded.Json is null
            ? null
            : new GroundedAnswerValidator().Validate(grounded.Json, new("cap.article", "Capability test", 1, [fact], false, false), "all", false);
        var grounding = grounded.Payload is { Decision: "show", UsedFactIds.Count: > 0 }
            && grounded.Payload.UsedFactIds.All(id => id == fact.Id)
            && groundedValidation is { Decision: AnswerDecision.Show, DiagnosticReason: GroundedAnswerValidator.PassedReason };
        var safeAbstain = abstain.Json is null
            ? null
            : new GroundedAnswerValidator().Validate(abstain.Json, new("cap.none", "Capability test", 0, [], false, false), "all", false);
        var abstains = abstain.Payload is not null
            && abstain.Payload.Decision.Equals("abstain", StringComparison.OrdinalIgnoreCase)
            && abstain.Payload.UsedFactIds.Count == 0
            && safeAbstain is { Decision: AnswerDecision.Abstain, DiagnosticReason: GroundedAnswerValidator.PassedReason };
        var followUpValidation = followUp.Json is null
            ? null
            : new GroundedAnswerValidator().Validate(followUp.Json, new("cap.article", "Capability test", 1, [fact], false, false), "all", false);
        var follows = followUp.Payload is not null
            && (followUp.Payload.Decision is "show" or "clarify")
            && followUp.Payload.UsedFactIds.Count > 0
            && followUp.Payload.UsedFactIds.All(id => id == fact.Id)
            && followUpValidation is { DiagnosticReason: GroundedAnswerValidator.PassedReason };
        var context = follows;
        if (!russian) warnings.Add("Ответ capability test не является русскоязычным.");
        if (!structured) warnings.Add("Модель нестабильно возвращает JSON.");
        if (!grounding) warnings.Add("Модель не соблюдает usedFactIds.");
        if (!abstains) warnings.Add("Модель не умеет безопасно воздерживаться.");
        if (!follows) warnings.Add("Follow-up контекст не подтверждён.");
        var average = elapsed.Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)elapsed.Average(x => x.Ticks));
        var compatible = russian && structured && grounding && abstains && follows;
        return new(true, true, russian, structured, grounding, abstains, follows, context, average,
            compatible ? "Подходит" : "Использовать с предупреждением", warnings);
    }

    private static async Task<CapabilitySample> RunAsync(IChatProvider provider, GroundedAnswerRequest request, List<TimeSpan> elapsed, List<string> warnings, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var response = await provider.CreateGroundedAnswerAsync(request, cancellationToken);
            var payload = JsonSerializer.Deserialize<GroundedAnswerPayload>(response.Json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new(response.Json, payload, HasFullSchema(response.Json));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add(ex.GetType().Name);
            return new(null, null, false);
        }
        finally { timer.Stop(); elapsed.Add(timer.Elapsed); }
    }

    private static bool HasFullSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        string[] required =
        [
            "decision", "presentationType", "title", "message", "summary", "steps", "possibleCauses",
            "usedFactIds", "needsScreen", "canSpeak", "needsMoreInformation", "needsVisualContext", "followUpSuggestions"
        ];
        return required.All(name => document.RootElement.TryGetProperty(name, out _));
    }

    private sealed record CapabilitySample(string? Json, GroundedAnswerPayload? Payload, bool HasFullSchema);
}
