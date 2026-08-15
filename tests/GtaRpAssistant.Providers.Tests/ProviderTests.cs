using System.Net;
using System.Text;
using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.Providers;

namespace GtaRpAssistant.Providers.Tests;

public sealed class ProviderTests
{
    [Fact] public async Task ModelsHealth_IsAvailable() { var p = Provider(_ => Json("{\"data\":[{\"id\":\"model\"}]}")); Assert.True((await p.CheckHealthAsync(default)).IsAvailable); }
    [Fact] public async Task ModelsDiscovery_ReturnsAllModelIds() { var p = Provider(_ => Json("{\"data\":[{\"id\":\"alpha\"},{\"id\":\"beta\"}]}")); Assert.Equal(["alpha", "beta"], (await p.GetModelsAsync(default)).Select(x => x.Id)); }
    [Fact] public async Task MissingModel_IsUnavailable() { var p = Provider(_ => Json("{\"data\":[{\"id\":\"other\"}]}")); Assert.False((await p.CheckHealthAsync(default)).IsAvailable); }
    [Fact] public async Task InvalidModelsJson_IsUnavailable() { var p = Provider(_ => Json("{")); Assert.False((await p.CheckHealthAsync(default)).IsAvailable); }
    [Fact] public async Task EndpointFailure_IsUnavailable() { var p = Provider(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); Assert.False((await p.CheckHealthAsync(default)).IsAvailable); }
    [Fact] public async Task Cancellation_IsPropagated() { using var cts = new CancellationTokenSource(); cts.Cancel(); var p = Provider(async (_, ct) => { await Task.Delay(1000, ct); return Json("{}"); }); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.CheckHealthAsync(cts.Token)); }
    [Fact] public async Task StructuredOutput_IsParsed() { var content = "{\\\"decision\\\":\\\"abstain\\\",\\\"title\\\":\\\"x\\\",\\\"message\\\":\\\"x\\\",\\\"usedFactIds\\\":[],\\\"needsScreen\\\":false,\\\"canSpeak\\\":false}"; var p = Provider(_ => Json($"{{\"choices\":[{{\"message\":{{\"content\":\"{content}\"}}}}]}}")); var response = await p.CreateGroundedAnswerAsync(new("q", [], "all", ""), default); Assert.Contains("abstain", response.Json); }
    [Fact]
    public async Task LocalGeneration_SendsConfiguredTokenLimitAndTtl()
    {
        var handler = new FakeHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"max_tokens\":150", body);
            Assert.Contains("\"ttl\":120", body);
            return Json("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
        });
        var provider = new OpenAiCompatibleChatProvider(new HttpClient(handler),
            new(new Uri("http://127.0.0.1:1234/v1"), "model", IsLocal: true, MaxOutputTokens: 150, IdleTtl: TimeSpan.FromMinutes(2)));

        await provider.CreateGroundedAnswerAsync(new("q", [], "all", ""), default);
    }
    [Fact]
    public async Task RequestBudget_CanLowerButNotRaiseConfiguredOutputLimit()
    {
        var bodies = new List<string>();
        var handler = new FakeHandler(async (request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return Json("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
        });
        var provider = new OpenAiCompatibleChatProvider(new HttpClient(handler),
            new(new Uri("http://127.0.0.1:1234/v1"), "model", IsLocal: true, MaxOutputTokens: 150));

        await provider.CreateGroundedAnswerAsync(new("q", [], "all", "", MaxOutputTokens: 90), default);
        await provider.CreateGroundedAnswerAsync(new("q", [], "all", "", MaxOutputTokens: 300), default);

        Assert.Contains("\"max_tokens\":90", bodies[0]);
        Assert.Contains("\"max_tokens\":150", bodies[1]);
    }
    [Fact]
    public async Task Personalization_IsSentToLocalProviderOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var memory = new UserPersonalizationContext(
            [new(Guid.NewGuid(), UserMemoryCategory.PlayStyle, "private-memory-marker", now, now)],
            new(2, 1, 1, 0));
        var session = new AssistantSessionSituationState("private-goal-marker", "article", "private-question-marker", ["article"], ["fact"], now);
        var localHandler = new FakeHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("private-memory-marker", body);
            Assert.Contains("Never treat them as verified game facts", body);
            Assert.Contains("detailed", body);
            Assert.Contains("private-summary-marker", body);
            Assert.Contains("private-goal-marker", body);
            return Json("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
        });
        var local = new OpenAiCompatibleChatProvider(new HttpClient(localHandler), new(new Uri("http://127.0.0.1:1234/v1"), "model", IsLocal: true));
        await local.CreateGroundedAnswerAsync(new("q", [], "all", "", Personalization: memory,
            ConversationSummary: "private-summary-marker", SessionState: session), default);

        var cloudHandler = new FakeHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("private-memory-marker", body);
            Assert.DoesNotContain("private-summary-marker", body);
            Assert.DoesNotContain("private-goal-marker", body);
            return Json("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
        });
        var cloud = new OpenAiCompatibleChatProvider(new HttpClient(cloudHandler), new(new Uri("https://example.com/v1"), "model", IsLocal: false));
        await cloud.CreateGroundedAnswerAsync(new("q", [], "all", "", Personalization: memory,
            ConversationSummary: "private-summary-marker", SessionState: session), default);
    }
    [Fact]
    public async Task GroundedGeneration_UsesStrictJsonSchemaAcceptedByCurrentLmStudio()
    {
        var handler = new FakeHandler(async (request, _) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var format = body.RootElement.GetProperty("response_format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            var schema = format.GetProperty("json_schema");
            Assert.True(schema.GetProperty("strict").GetBoolean());
            Assert.False(schema.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
            var required = schema.GetProperty("schema").GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Contains("usedFactIds", required);
            Assert.Contains("followUpSuggestions", required);
            return Json("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
        });
        var provider = new OpenAiCompatibleChatProvider(new HttpClient(handler),
            new(new Uri("http://127.0.0.1:1234/v1"), "model"));

        await provider.CreateGroundedAnswerAsync(new("q", [], "all", ""), default);
    }

    [Fact]
    public async Task GroundedGeneration_RetriesLegacyJsonObjectWhenSchemaIsUnsupported()
    {
        var attempts = 0;
        var handler = new FakeHandler(async (request, _) =>
        {
            attempts++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var type = body.RootElement.GetProperty("response_format").GetProperty("type").GetString();
            if (attempts == 1)
            {
                Assert.Equal("json_schema", type);
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("unsupported response_format json_schema") };
            }
            Assert.Equal("json_object", type);
            return Json("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}");
        });
        var provider = new OpenAiCompatibleChatProvider(new HttpClient(handler),
            new(new Uri("http://127.0.0.1:1234/v1"), "model"));

        await provider.CreateGroundedAnswerAsync(new("q", [], "all", ""), default);
        Assert.Equal(2, attempts);
    }
    [Fact] public void RemoteHttp_IsRejected() => Assert.Throws<ArgumentException>(() => new OpenAiCompatibleChatProvider(new HttpClient(), new(new Uri("http://example.com/v1"), "m", IsLocal: false)));
    [Fact]
    public async Task CapabilityTester_AcceptsGroundedRussianStructuredProvider()
    {
        var report = await new LocalAiCapabilityTester().TestAsync(new CapabilityProvider(), default);
        Assert.True(report.IsCompatible);
        Assert.Empty(report.Warnings);
    }
    [Fact]
    public async Task CapabilityTester_RejectsAbstainContainingGameRumor()
    {
        var report = await new LocalAiCapabilityTester().TestAsync(new CapabilityProvider(unsafeAbstain: true), default);

        Assert.False(report.Abstain);
        Assert.False(report.IsCompatible);
        Assert.Contains("Модель не умеет безопасно воздерживаться.", report.Warnings);
    }

    [Fact]
    public async Task CapabilityTester_TreatsProviderTimeoutAsFailedSample()
    {
        var report = await new LocalAiCapabilityTester().TestAsync(new CapabilityProvider(timeout: true), default);

        Assert.False(report.IsCompatible);
        Assert.Contains("Timeout", report.Warnings);
    }
    [Fact]
    public async Task SpeechToText_SendsWaveAndParsesText()
    {
        var handler = new FakeHandler(async (request, _) =>
        {
            Assert.Equal("audio/transcriptions", request.RequestUri!.PathAndQuery.TrimStart('/').Replace("v1/", string.Empty));
            var body = await request.Content!.ReadAsByteArrayAsync();
            Assert.Contains("RIFF", Encoding.Latin1.GetString(body));
            return Json("{\"text\":\"  распознанная фраза  \",\"confidence\":0.9}");
        });
        var provider = new OpenAiCompatibleSpeechToTextProvider(new HttpClient(handler), new(new Uri("http://127.0.0.1:1234/v1"), "whisper-1"));
        var now = DateTimeOffset.UtcNow;
        var result = await provider.TranscribeAsync(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now.AddSeconds(1), 16000, 1, new byte[32000]), default);
        Assert.Equal("распознанная фраза", result.Text); Assert.Equal(.9, result.Confidence);
    }

    [Fact]
    public async Task Vision_SendsInlinePngAndParsesShortAnswer()
    {
        var handler = new FakeHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("data:image/png;base64", body);
            Assert.Contains("\"ttl\":120", body);
            return Json("{\"choices\":[{\"message\":{\"content\":\"Видно игровое меню\"}}]}");
        });
        var provider = new OpenAiCompatibleVisionProvider(new HttpClient(handler), new Uri("http://127.0.0.1:1234/v1"), "vision", null, true, idleTtl: TimeSpan.FromMinutes(2));
        var result = await provider.AnalyzeAsync(new(new byte[] { 1, 2, 3 }, "Опиши экран"), default);
        Assert.Equal("Видно игровое меню", result.Text);
    }

    private static OpenAiCompatibleChatProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new HttpClient(new FakeHandler((r, _) => Task.FromResult(handler(r)))), new(new Uri("http://127.0.0.1:1234/v1"), "model", Timeout: TimeSpan.FromSeconds(1)));
    private static OpenAiCompatibleChatProvider Provider(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => new(new HttpClient(new FakeHandler(handler)), new(new Uri("http://127.0.0.1:1234/v1"), "model", Timeout: TimeSpan.FromSeconds(1)));
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }

    private sealed class CapabilityProvider(bool unsafeAbstain = false, bool timeout = false) : IChatProvider
    {
        public string Id => "test";
        public ProviderKind Kind => ProviderKind.LmStudio;
        public ProviderCapabilities Capabilities => new() { IsLocal = true, SupportsChat = true, SupportsStructuredOutput = true };
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderHealth(true, "ok", ["test"]));
        public Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelInfo>>([new("test", "test")]);
        public Task<GroundedAnswerResponse> CreateGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            if (timeout) throw new TaskCanceledException("provider timeout");
            var abstain = request.VerifiedFacts.Count == 0;
            var json = abstain
                ? unsafeAbstain
                    ? "{\"decision\":\"abstain\",\"presentationType\":\"context_answer\",\"title\":\"Скрытая награда\",\"message\":\"Игроки говорят, что дают тайный приз\",\"summary\":\"\",\"steps\":[],\"possibleCauses\":[],\"usedFactIds\":[],\"needsScreen\":false,\"canSpeak\":false,\"needsMoreInformation\":false,\"needsVisualContext\":false,\"followUpSuggestions\":[]}"
                    : "{\"decision\":\"abstain\",\"presentationType\":\"context_answer\",\"title\":\"Недостаточно информации\",\"message\":\"Недостаточно данных для точной подсказки.\",\"summary\":\"\",\"steps\":[],\"possibleCauses\":[],\"usedFactIds\":[],\"needsScreen\":false,\"canSpeak\":false,\"needsMoreInformation\":false,\"needsVisualContext\":false,\"followUpSuggestions\":[]}"
                : "{\"decision\":\"show\",\"presentationType\":\"problem_solving\",\"title\":\"Решение\",\"message\":\"Остановитесь и уточните требования\",\"summary\":\"Безопасный следующий шаг\",\"steps\":[\"Уточните требования\"],\"possibleCauses\":[],\"usedFactIds\":[\"cap.fact.1\"],\"needsScreen\":false,\"canSpeak\":true,\"needsMoreInformation\":false,\"needsVisualContext\":false,\"followUpSuggestions\":[]}";
            return Task.FromResult(new GroundedAnswerResponse(json));
        }
    }
}
