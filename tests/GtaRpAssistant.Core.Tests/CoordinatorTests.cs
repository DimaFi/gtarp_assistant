using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task VerifiedPreparedAnswer_IsPresentedWithoutProvider()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        var catalog = new FakeCatalog(provider);
        var events = new CapturingEvents();
        await using var coordinator = Create(overlay, catalog, prepared: true, events: events);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;
        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);
        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, catalog.AvailabilityCalls);
        Assert.Single(overlay.Answers);
        Assert.Equal(AssistantSessionState.Listening, coordinator.State);
        var metrics = events.SingleMetrics();
        Assert.Equal(nameof(AnswerRoute.Deterministic), metrics.Route);
        Assert.Equal("verified_prepared_answer", metrics.RouteReason);
        Assert.True(metrics.AvoidedLlm);
        Assert.Equal(0, metrics.ProviderAvailabilityChecks);
        Assert.Equal(0, metrics.EstimatedInputTokens);
    }

    [Fact]
    public async Task GroundedQuestion_UsesAvailableLocalProvider()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        var catalog = new FakeCatalog(provider);
        var events = new CapturingEvents();
        await using var coordinator = Create(overlay, catalog, prepared: false, events: events);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;
        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);
        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, catalog.AvailabilityCalls);
        var metrics = events.SingleMetrics();
        Assert.Equal(1, metrics.ProviderAvailabilityChecks);
        Assert.Equal(1, metrics.LlmCalls);
        Assert.True(metrics.EstimatedInputTokens > 0);
        Assert.False(metrics.AvoidedLlm);
        Assert.NotNull(metrics.KnowledgeMethod);
        Assert.True(metrics.KnowledgeScore > 0);
    }

    [Fact]
    public async Task GeneralConversation_UsesLocalModelWithoutKnowledge_AndKeepsFollowUpContext()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider((_, request) => new GroundedAnswerResponse(JsonSerializer.Serialize(new
        {
            decision = "show",
            title = "Давай выберем",
            message = request.Question.Contains("наоборот", StringComparison.OrdinalIgnoreCase)
                ? "Если сделать наоборот, сначала решим, чего вы хотите избежать."
                : "Можно спокойно поговорить или придумать небольшую цель на вечер.",
            usedFactIds = Array.Empty<string>(),
            needsScreen = false,
            canSpeak = false,
        })));
        var catalog = new FakeCatalog(provider);
        await using var coordinator = new AssistantSessionCoordinator(
            new(TimeSpan.FromMinutes(3)), new RuleBasedIntentDetector([]), new EmptyKnowledge(), new ContextSelector(),
            new AiRouter(), new GroundedAnswerValidator(), catalog, overlay, new TranscriptDeduplicator(),
            new ProactivePolicy(), new NullEvents());
        coordinator.Start(true);

        async Task<AssistantAnswer?> Ask(string text)
        {
            var now = DateTimeOffset.UtcNow;
            return await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, text, 1),
                AssistantActivationKind.ManualText, "all", false, false), default);
        }

        var first = await Ask("Мне скучно, поговори со мной.");
        var second = await Ask("А если сделать наоборот?");

        Assert.Equal(AnswerDecision.Show, first!.Decision);
        Assert.Equal(AnswerDecision.Show, second!.Decision);
        Assert.Equal(2, provider.Calls);
        Assert.All(provider.Requests, request =>
        {
            Assert.Equal(AssistantResponseMode.OpenConversation, request.ResponseMode);
            Assert.Empty(request.VerifiedFacts);
        });
        Assert.NotEmpty(provider.Requests[1].Conversation!);
    }

    [Fact]
    public async Task ManualRequest_OffersMemoryCandidateWithoutPersistingIt()
    {
        var observer = new CapturingMemoryCandidates();
        var overlay = new FakeOverlay();
        await using var coordinator = new AssistantSessionCoordinator(
            new(TimeSpan.FromMinutes(3)), new RuleBasedIntentDetector([]), new EmptyKnowledge(), new ContextSelector(),
            new AiRouter(), new GroundedAnswerValidator(), new RouteCatalog([]), overlay, new TranscriptDeduplicator(),
            new ProactivePolicy(), new NullEvents(), memoryCandidates: observer);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;

        await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
            "Мне нравится рыбалка", 1), AssistantActivationKind.ManualText, "all", false, false), default);

        Assert.Equal("Мне нравится рыбалка", Assert.Single(observer.Observed));
    }

    [Fact]
    public async Task BroadEarningQuestion_AndClarifyingFollowUp_UseExplicitAssumptionsWithoutProvider()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        var catalog = new FakeCatalog(provider);
        await using var coordinator = Create(overlay, catalog, prepared: false);
        coordinator.Start(true);

        async Task<AssistantAnswer?> Ask(string text)
        {
            var now = DateTimeOffset.UtcNow;
            return await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, text, 1),
                AssistantActivationKind.ManualText, "all", false, false), default);
        }

        var first = await Ask("как мне начать зарабатывать в гта пять рп");
        var second = await Ask("что тебе нужно чтобы дать мне подсказку");

        Assert.Contains("Предположу", first!.Message);
        Assert.Contains("могу продолжить и без уточнений", second!.Message);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, catalog.AvailabilityCalls);
    }

    [Fact]
    public async Task RepeatedGroundedQuestion_UsesValidatedCacheBeforeProviderDiscovery()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        var catalog = new FakeCatalog(provider);
        var cache = new FakeAnswerCache();
        var events = new CapturingEvents();
        await using var coordinator = Create(overlay, catalog, prepared: false, events: events, answerCache: cache);
        coordinator.Start(true);

        async Task<AssistantAnswer?> AskAsync()
        {
            coordinator.ClearContext();
            coordinator.StartNewConversation();
            var now = DateTimeOffset.UtcNow;
            return await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
                "сколько стоит контракт?", 1), AssistantActivationKind.ManualText, "all", false, false), default);
        }

        var first = await AskAsync();
        var second = await AskAsync();

        Assert.Equal("fake", first!.ProviderId);
        Assert.Equal("answer-cache", second!.ProviderId);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, catalog.AvailabilityCalls);
        var metrics = events.Metrics();
        Assert.Equal(2, metrics.Count);
        Assert.Equal(1, metrics[1].CacheLookups);
        Assert.Equal(1, metrics[1].CacheHits);
        Assert.Equal(0, metrics[1].ProviderAvailabilityChecks);
        Assert.Equal(0, metrics[1].LlmCalls);
    }

    [Fact]
    public async Task LongConversation_SendsBoundedRollingSummaryAndStructuredStateToLocalProvider()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        await using var coordinator = Create(overlay, provider, prepared: false);
        coordinator.Start(true);

        for (var i = 0; i < 5; i++)
        {
            var now = DateTimeOffset.UtcNow.AddSeconds(i);
            await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
                $"контракт вопрос номер {i}?", 1), AssistantActivationKind.ManualText, "all", false, false), default);
        }

        var request = provider.Requests[^1];
        Assert.NotNull(request.ConversationSummary);
        Assert.Contains("вопрос номер 0", request.ConversationSummary);
        Assert.Equal("контракт вопрос номер 0?", request.SessionState!.Goal);
        Assert.Equal("контракт вопрос номер 4?", request.SessionState.OpenQuestion);
        Assert.True(request.ConversationSummary.Length <= 360);
    }

    [Fact]
    public async Task ScreenQuestion_UsesFreshLocalContextWithoutCallingProvider()
    {
        var now = DateTimeOffset.UtcNow;
        var screen = new ScreenContextStore();
        screen.Publish(new(now, KnownScreenKind.Shop, .86, [], [new("price", "Цена 2500$", .92, ScreenRegion.Full)], [], now.AddSeconds(20)));
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        await using var coordinator = Create(overlay, new FakeCatalog(provider), prepared: false, screenContext: screen);
        coordinator.Start(true);

        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "Что сейчас написано на экране?", 1), AssistantActivationKind.ManualText, "all", true, false), default);

        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal("local-screen-context", answer.ProviderId);
        Assert.Contains("2500", answer.Message);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Personalization_IsAttachedAfterKnowledgeSelectionWithoutChangingVerifiedFacts()
    {
        var overlay = new FakeOverlay(); var provider = new FakeProvider(); var personalization = new FakePersonalization();
        await using var coordinator = Create(overlay, new FakeCatalog(provider), prepared: false, personalization);
        coordinator.Start(true); var now = DateTimeOffset.UtcNow;
        await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);

        var request = Assert.Single(provider.Requests);
        Assert.Equal(["f"], request.VerifiedFacts.Select(x => x.Id));
        Assert.Equal("Личный стиль", Assert.Single(request.Personalization!.Memories).Content);
    }

    [Fact]
    public async Task GameAudio_IsContextOnly()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        await using var coordinator = Create(overlay, provider, prepared: true);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;
        var result = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.GameAudio, now, now, "помощник почему контракт не запускается", 1), AssistantActivationKind.AutomaticVoice, "all", false, false), default);
        Assert.Null(result);
        Assert.Empty(overlay.Answers);
    }

    [Fact]
    public async Task InvalidProviderJson_IsRepairedOnceAndConversationIsStored()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider((call, request) => call == 1
            ? new GroundedAnswerResponse("{")
            : ValidResponse(request));
        await using var coordinator = Create(overlay, provider, prepared: false);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;

        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
            "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);

        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal(2, provider.Calls);
        Assert.False(provider.Requests[0].IsRepair);
        Assert.True(provider.Requests[1].IsRepair);
        Assert.Equal("fake", answer.ProviderId);
        Assert.Equal(2, coordinator.Conversation.Turns.Count);
    }

    [Fact]
    public async Task NoAvailableProvider_ReturnsVerifiedKnowledgeAnswer()
    {
        var overlay = new FakeOverlay();
        await using var coordinator = Create(overlay, new RouteCatalog([]), prepared: false);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;

        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
            "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);

        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Contains("требования", answer.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("knowledge-extractive", answer.ProviderId);
        Assert.Single(overlay.Answers);
        Assert.Equal(AssistantSessionState.Listening, coordinator.State);
    }

    [Fact]
    public async Task UnavailablePrimary_FallsBackToNextConfiguredProvider()
    {
        var overlay = new FakeOverlay();
        var primary = new FakeProvider("primary", (_, _) => throw new HttpRequestException("offline"));
        var fallback = new FakeProvider("fallback");
        await using var coordinator = Create(overlay, new RouteCatalog([primary, fallback]), prepared: false);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;

        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
            "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);

        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal("fallback", answer.ProviderId);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task AllConfiguredProvidersUnavailable_FallsBackToVerifiedKnowledge()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider("offline", (_, _) => throw new HttpRequestException("offline"));
        await using var coordinator = Create(overlay, new RouteCatalog([provider]), prepared: false);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;

        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
            "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);

        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal("knowledge-fallback", answer.ProviderId);
        Assert.Contains("требования", answer.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AssistantSessionCoordinator Create(FakeOverlay overlay, FakeProvider provider, bool prepared)
        => Create(overlay, new FakeCatalog(provider), prepared);

    private static AssistantSessionCoordinator Create(FakeOverlay overlay, IChatProviderCatalog catalog, bool prepared, IUserPersonalizationContextProvider? personalization = null, IScreenContextStore? screenContext = null, ISessionEventSink? events = null, IAnswerCache? answerCache = null)
    {
        var fact = new KnowledgeFact("f", "a", "Проверьте актуальные требования", true, DateTimeOffset.UtcNow);
        var knowledge = new FakeKnowledge(new("a", "Контракт", 1, [fact], false, false, prepared ? "Проверьте актуальные требования" : null, prepared));
        return new(new(TimeSpan.FromMinutes(3)), new RuleBasedIntentDetector(["контракт"]), knowledge, new ContextSelector(), new AiRouter(), new GroundedAnswerValidator(), catalog, overlay, new TranscriptDeduplicator(), new ProactivePolicy(), events ?? new NullEvents(), personalization: personalization, screenContext: screenContext, answerCache: answerCache);
    }

    private sealed class FakeKnowledge(KnowledgeMatch match) : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<KnowledgeMatch>>([match]);
        public Task<KnowledgeArticle?> GetArticleAsync(string articleId, CancellationToken cancellationToken) => Task.FromResult<KnowledgeArticle?>(null);
    }
    private sealed class FakeOverlay : IOverlayService
    {
        public List<AssistantAnswer> Answers { get; } = [];
        public bool IsVisible => Answers.Count > 0;
        public Task ShowAsync(AssistantAnswer answer, CancellationToken cancellationToken) { Answers.Add(answer); return Task.CompletedTask; }
        public Task HideAsync() => Task.CompletedTask;
    }
    private sealed class FakeCatalog(FakeProvider provider) : IChatProviderCatalog
    {
        public int AvailabilityCalls { get; private set; }
        public Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
        {
            AvailabilityCalls++;
            return Task.FromResult(new ChatProviderAvailability(provider, null, true, false));
        }
    }
    private sealed class RouteCatalog(IReadOnlyList<IChatProvider> providers) : IChatProviderCatalog
    {
        public Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
        {
            var local = providers.FirstOrDefault(x => x.Capabilities.IsLocal);
            var cloud = providers.FirstOrDefault(x => !x.Capabilities.IsLocal);
            return Task.FromResult(new ChatProviderAvailability(local, cloud, local is not null, cloud is not null, providers));
        }
    }
    private sealed class FakeProvider : IChatProvider
    {
        private readonly Func<int, GroundedAnswerRequest, GroundedAnswerResponse>? _responseFactory;
        public FakeProvider(Func<int, GroundedAnswerRequest, GroundedAnswerResponse>? responseFactory = null) : this("fake", responseFactory) { }
        public FakeProvider(string id, Func<int, GroundedAnswerRequest, GroundedAnswerResponse>? responseFactory = null)
        {
            Id = id;
            _responseFactory = responseFactory;
        }
        public int Calls { get; private set; }
        public List<GroundedAnswerRequest> Requests { get; } = [];
        public string Id { get; }
        public ProviderKind Kind => ProviderKind.BuiltIn;
        public ProviderCapabilities Capabilities => new() { SupportsTextInput = true, SupportsChat = true, SupportsStructuredOutput = true, SupportsJsonMode = true, IsLocal = true };
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderHealth(true, "ok"));
        public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelInfo>>([new("fake", "Fake")]);
        public Task<GroundedAnswerResponse> CreateGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request);
            return Task.FromResult(_responseFactory?.Invoke(Calls, request) ?? ValidResponse(request));
        }
    }
    private static GroundedAnswerResponse ValidResponse(GroundedAnswerRequest request) => new(JsonSerializer.Serialize(new
    {
        decision = "show",
        title = "Ответ",
        message = request.VerifiedFacts[0].Text,
        usedFactIds = new[] { request.VerifiedFacts[0].Id },
        needsScreen = false,
        canSpeak = false,
    }));
    private sealed class NullEvents : ISessionEventSink { public void Write(SessionEvent sessionEvent) { } }
    private sealed class CapturingEvents : ISessionEventSink
    {
        public List<SessionEvent> Items { get; } = [];
        public void Write(SessionEvent sessionEvent) => Items.Add(sessionEvent);
        public AssistantRequestMetrics SingleMetrics()
        {
            var detail = Assert.Single(Items.Where(x => x.Name == "Assistant request metrics")).Detail;
            return JsonSerializer.Deserialize<AssistantRequestMetrics>(detail!)!;
        }
        public IReadOnlyList<AssistantRequestMetrics> Metrics() => Items.Where(x => x.Name == "Assistant request metrics")
            .Select(x => JsonSerializer.Deserialize<AssistantRequestMetrics>(x.Detail!)!).ToArray();
    }
    private sealed class EmptyKnowledge : IKnowledgeRepository
    {
        public Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeMatch>>([]);
        public Task<KnowledgeArticle?> GetArticleAsync(string articleId, CancellationToken cancellationToken) => Task.FromResult<KnowledgeArticle?>(null);
    }
    private sealed class FakeAnswerCache : IAnswerCache
    {
        private readonly Dictionary<string, AnswerCacheEntry> _entries = [];
        public Task<AnswerCacheEntry?> TryGetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_entries.GetValueOrDefault(key));
        public Task StoreAsync(string key, AssistantAnswer answer, TimeSpan ttl, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            _entries[key] = new(answer, now, now.Add(ttl), 0);
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken) { _entries.Clear(); return Task.CompletedTask; }
    }
    private sealed class FakePersonalization : IUserPersonalizationContextProvider
    {
        public bool ApplyExplicitFeedback(string userText) => false;
        public UserPersonalizationContext Build(string question, int maxMemories = 8) => new([new(Guid.NewGuid(), UserMemoryCategory.CommunicationPreference, "Личный стиль", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)], new());
    }
    private sealed class CapturingMemoryCandidates : IUserMemoryCandidateService
    {
        public List<string> Observed { get; } = [];
        public UserMemoryCandidate? Observe(string userText, DateTimeOffset at) { Observed.Add(userText); return null; }
        public IReadOnlyList<UserMemoryCandidate> List(DateTimeOffset at) => [];
        public UserMemoryItem? Approve(Guid id, DateTimeOffset at) => null;
        public bool Reject(Guid id) => false;
        public void Clear() { }
    }
}
