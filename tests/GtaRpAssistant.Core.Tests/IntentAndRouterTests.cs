using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class IntentAndRouterTests
{
    private readonly RuleBasedIntentDetector _detector = new(["контракт"]);
    private static TranscriptEntry Entry(AudioSourceKind source, string text) { var now = DateTimeOffset.UtcNow; return new(Guid.NewGuid(), source, now, now, text, 1); }
    [Fact] public async Task ExplicitUserQuestion_IsDetected() { var e = Entry(AudioSourceKind.UserMicrophone, "почему контракт не запускается"); var result = await _detector.DetectAsync(new([e], e), default); Assert.True(result.ShouldConsiderHint, result.Reason); }
    [Fact] public async Task OrdinaryConversation_IsIgnored() { var e = Entry(AudioSourceKind.UserMicrophone, "едем дальше"); Assert.False((await _detector.DetectAsync(new([e], e), default)).ShouldConsiderHint); }
    [Fact] public async Task GameAudio_CannotActivate() { var e = Entry(AudioSourceKind.GameAudio, "помощник как сделать контракт"); Assert.False((await _detector.DetectAsync(new([e], e), default)).ShouldConsiderHint); }
    [Fact] public async Task WakeWord_IsDetected() { var e = Entry(AudioSourceKind.UserMicrophone, "помощник подскажи"); Assert.True((await _detector.DetectAsync(new([e], e), default)).ExplicitWakeWord); }
    [Fact]
    public async Task CustomWakePhrase_WithPunctuation_IsNormalizedAndActivates()
    {
        var detector = new RuleBasedIntentDetector(["контракт"]) { WakeWord = "Лаберти, слушай" };
        var entry = Entry(AudioSourceKind.UserMicrophone, "Лаберти слушай, помоги мне");

        var result = await detector.DetectAsync(new([entry], entry), default);

        Assert.True(result.ExplicitWakeWord);
        Assert.True(result.ShouldConsiderHint);
    }
    [Fact] public async Task IrrelevantQuestion_IsIgnored() { var e = Entry(AudioSourceKind.UserMicrophone, "как приготовить суп"); Assert.False((await _detector.DetectAsync(new([e], e), default)).ShouldConsiderHint); }
    [Theory]
    [InlineData(true, false, false, false, false, AnswerRoute.Deterministic)]
    [InlineData(false, false, true, true, true, AnswerRoute.Abstain)]
    [InlineData(false, true, true, false, false, AnswerRoute.LocalChat)]
    [InlineData(false, true, false, true, true, AnswerRoute.CloudChat)]
    [InlineData(false, true, false, true, false, AnswerRoute.Deterministic)]
    [InlineData(false, true, false, false, false, AnswerRoute.Deterministic)]
    public void Router_IsDeterministic(bool prepared, bool grounding, bool local, bool cloud, bool allowed, AnswerRoute expected) => Assert.Equal(expected, new AiRouter().Select(new(prepared, grounding, local, cloud, allowed)));

    [Fact]
    public void Router_UsesConfiguredProviderRouteWithoutInferringLocalPriority() =>
        Assert.Equal(AnswerRoute.ConfiguredChat, new AiRouter().Select(new(false, true, true, true, true, true)));

    [Theory]
    [InlineData(true, true, AnswerRoute.Deterministic, "verified_prepared_answer", false)]
    [InlineData(false, false, AnswerRoute.Abstain, "insufficient_grounding", false)]
    [InlineData(false, true, null, "provider_availability_required", true)]
    public void Router_PreflightAvoidsProviderDiscoveryWhenKnowledgeAlreadyDecides(
        bool prepared, bool grounding, AnswerRoute? expected, string reason, bool requiresProviderAvailability)
    {
        var decision = new AiRouter().SelectBeforeProvider(new(prepared, grounding));

        Assert.Equal(expected, decision.Route);
        Assert.Equal(reason, decision.Reason);
        Assert.Equal(requiresProviderAvailability, decision.RequiresProviderAvailability);
    }

    [Fact]
    public void AnswerCacheKey_IsOpaqueStableAndVersionedByKnowledgeAndServer()
    {
        var updated = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var fact = new KnowledgeFact("fact-1", "article", "Цена составляет 2500 долларов", true, updated, "all");
        var match = new KnowledgeMatch("article", "Цена", 1, [fact], false, false);

        var first = AnswerCacheKeyBuilder.Create("СКОЛЬКО стоит?", "all", match, null);
        var normalizedEquivalent = AnswerCacheKeyBuilder.Create("сколько   стоит", "all", match, null);
        var changedFact = AnswerCacheKeyBuilder.Create("сколько стоит", "all",
            match with { Facts = [fact with { Text = "Цена составляет 3000 долларов", UpdatedAt = updated.AddMinutes(1) }] }, null);
        var changedServer = AnswerCacheKeyBuilder.Create("сколько стоит", "server-2", match, null);

        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("сколько", first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(first, normalizedEquivalent);
        Assert.NotEqual(first, changedFact);
        Assert.NotEqual(first, changedServer);
    }

    [Theory]
    [InlineData("как сделать автокликер для автоматического фарма")]
    [InlineData("кто выиграет следующую войну семей")]
    public void QuestionPolicy_BlocksUnsafeOrUnverifiableRequests(string question) =>
        Assert.True(AssistantQuestionPolicy.TryGetBlockReason(question, out _));

    [Theory]
    [InlineData("где взять макросы и настройки интерфейса")]
    [InlineData("когда следующий ивент")]
    [InlineData("как дрессировать питомца")]
    public void QuestionPolicy_AllowsSupportedKnowledgeRequests(string question) =>
        Assert.False(AssistantQuestionPolicy.TryGetBlockReason(question, out _));

    [Fact]
    public void GroundingSelector_PrefersRelevantFactsAndRespectsBudget()
    {
        var facts = Enumerable.Range(1, 10).Select(i => new KnowledgeFact($"f{i}", "a", i == 8 ? "Комиссия банкомата составляет четыре процента" : new string('я', 300), true, DateTimeOffset.UtcNow)).ToArray();
        var selected = GroundingContextSelector.Select("комиссия банкомата", facts, maxFacts: 3, maxCharacters: 700);

        Assert.Equal("f8", selected[0].Id);
        Assert.True(selected.Count <= 3);
        Assert.True(selected.Sum(x => x.Text.Length) <= 700);
    }

    [Theory]
    [InlineData("привет")]
    [InlineData("Кто ты?")]
    [InlineData("Что ты умеешь?")]
    [InlineData("Спасибо!")]
    public void ConversationGrounding_RecognizesSafeSmallTalk(string question)
    {
        var match = AssistantConversationGrounding.TryCreate(question);

        Assert.NotNull(match);
        Assert.All(match.Facts, fact => Assert.True(fact.Verified));
    }

    [Theory]
    [InlineData("сколько дают BP за достижение")]
    [InlineData("когда следующий ивент")]
    [InlineData("придумай правило сервера")]
    public void ConversationGrounding_DoesNotReplaceGameKnowledge(string question) =>
        Assert.Null(AssistantConversationGrounding.TryCreate(question));

    [Fact]
    public void InferenceGrounding_AssumesBeginnerInsteadOfBlockingBroadEarningQuestion()
    {
        var match = AssistantInferenceGrounding.TryCreate("как мне начать зарабатывать в гта пять рп", AssistantRequestType.GeneralConversation,
            new("", new(null, null, null, [], [], DateTimeOffset.UtcNow)));
        Assert.NotNull(match);
        Assert.Contains("Предположу", match.PreparedAnswer);
        Assert.True(match.HasVerifiedPreparedAnswer);
    }
}
