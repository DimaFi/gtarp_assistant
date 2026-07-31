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
        await using var coordinator = Create(overlay, provider, prepared: true);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;
        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);
        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal(0, provider.Calls);
        Assert.Single(overlay.Answers);
        Assert.Equal(AssistantSessionState.Listening, coordinator.State);
    }

    [Fact]
    public async Task GroundedQuestion_UsesAvailableLocalProvider()
    {
        var overlay = new FakeOverlay();
        var provider = new FakeProvider();
        await using var coordinator = Create(overlay, provider, prepared: false);
        coordinator.Start(true);
        var now = DateTimeOffset.UtcNow;
        var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "почему контракт не запускается", 1), AssistantActivationKind.ManualText, "all", false, false), default);
        Assert.Equal(AnswerDecision.Show, answer!.Decision);
        Assert.Equal(1, provider.Calls);
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

    private static AssistantSessionCoordinator Create(FakeOverlay overlay, IChatProviderCatalog catalog, bool prepared)
    {
        var fact = new KnowledgeFact("f", "a", "Проверьте актуальные требования", true, DateTimeOffset.UtcNow);
        var knowledge = new FakeKnowledge(new("a", "Контракт", 1, [fact], false, false, prepared ? "Проверьте актуальные требования" : null, prepared));
        return new(new(TimeSpan.FromMinutes(3)), new RuleBasedIntentDetector(["контракт"]), knowledge, new ContextSelector(), new AiRouter(), new GroundedAnswerValidator(), catalog, overlay, new TranscriptDeduplicator(), new ProactivePolicy(), new NullEvents());
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
        public Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) => Task.FromResult(new ChatProviderAvailability(provider, null, true, false));
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
}
