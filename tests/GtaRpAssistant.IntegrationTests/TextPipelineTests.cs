using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.Knowledge;

namespace GtaRpAssistant.IntegrationTests;

public sealed class TextPipelineTests
{
    [Fact]
    public async Task MultipleSpokenEclipseArticles_AreCombinedWithoutAi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gta-rp-legal-multi-{Guid.NewGuid():N}.db");
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "official", "eclipse-legal-base.json");
            var articles = await new EclipseLegalReferenceLoader().LoadAsync(source, default);
            var repository = new SqliteKnowledgeRepository($"Data Source={path}");
            await repository.InitializeAsync(articles, default);
            var overlay = new CapturingOverlay();
            await using var coordinator = new AssistantSessionCoordinator(new(TimeSpan.FromMinutes(3)), new RuleBasedIntentDetector([]), repository,
                new ContextSelector(), new AiRouter(), new GroundedAnswerValidator(), new UnavailableProviderCatalog(), overlay,
                new TranscriptDeduplicator(), new ProactivePolicy(), new NullEventSink());
            coordinator.Start(true);
            var now = DateTimeOffset.UtcNow;

            var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
                "что означают статьи уголовного кодекса двенадцать точка шесть двенадцать точка один и семнадцать точка четыре", 1),
                AssistantActivationKind.ManualText, "all", false, false), default);

            Assert.Equal(AnswerDecision.Show, answer!.Decision);
            Assert.Equal("prepared-answer", answer.ProviderId);
            Assert.Contains("12.6", answer.Message);
            Assert.Contains("12.1", answer.Message);
            Assert.Contains("17.4", answer.Message);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task RealKnowledgeCatalog_AnswersRepresentativeTypedQuestionsWithoutAi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gta-rp-real-catalog-{Guid.NewGuid():N}.db");
        try
        {
            var packDirectory = Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp");
            var communityDirectory = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community");
            var articles = (await new KnowledgePackLoader().LoadAsync(packDirectory, default))
                .Concat(await new CommunityReferenceLoader().LoadAsync(communityDirectory, default))
                .ToArray();
            var repository = new SqliteKnowledgeRepository($"Data Source={path}");
            await repository.InitializeAsync(articles, default);
            var overlay = new CapturingOverlay();
            await using var coordinator = new AssistantSessionCoordinator(
                new(TimeSpan.FromMinutes(3)),
                new RuleBasedIntentDetector([]),
                repository,
                new ContextSelector(),
                new AiRouter(),
                new GroundedAnswerValidator(),
                new UnavailableProviderCatalog(),
                overlay,
                new TranscriptDeduplicator(),
                new ProactivePolicy(),
                new NullEventSink());
            coordinator.Start(true);

            var questions = new[]
            {
                "как получить достижение Вращайте барабан",
                "как получить достижение Большой куш",
                "как дрессировать питомца",
                "сколько стоит абонемент в спортзал",
                "как играть в дартс",
                "какая ставка в тренировочном комплексе",
                "что нужно для вступления в car meet",
                "какие есть мотоклубы",
            };
            foreach (var question in questions)
            {
                coordinator.StartNewConversation();
                var now = DateTimeOffset.UtcNow;
                var answer = await coordinator.ProcessAsync(new(
                    new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, question, 1),
                    AssistantActivationKind.ManualText,
                    "all",
                    false,
                    false), default);

                Assert.NotNull(answer);
                Assert.True(answer.Decision == AnswerDecision.Show,
                    $"Question '{question}' returned {answer.Decision}: {answer.DiagnosticReason}; message={answer.Message}");
                Assert.True(answer.UsedFactIds.Count > 0, $"Question '{question}' returned no grounded facts.");
                Assert.NotEqual(GroundedAnswerValidator.SafeAbstainMessage, answer.Message);
            }
            Assert.Equal(questions.Length, overlay.Answers.Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExpiredCommunityReference_IsFoundButCannotProduceAnswer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gta-rp-expired-community-{Guid.NewGuid():N}.db");
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community");
            var articles = await new CommunityReferenceLoader().LoadAsync(directory, default);
            var repository = new SqliteKnowledgeRepository($"Data Source={path}");
            await repository.InitializeAsync(articles, default);
            var match = Assert.Single(await repository.SearchAsync(new("что делать при артериальном кровотечении"), default));
            Assert.True(match.IsOutdated);

            var overlay = new CapturingOverlay();
            await using var coordinator = new AssistantSessionCoordinator(new(TimeSpan.FromMinutes(3)), new RuleBasedIntentDetector([]), repository,
                new ContextSelector(), new AiRouter(), new GroundedAnswerValidator(), new UnavailableProviderCatalog(), overlay,
                new TranscriptDeduplicator(), new ProactivePolicy(), new NullEventSink());
            coordinator.Start(true);
            var now = DateTimeOffset.UtcNow;
            var answer = await coordinator.ProcessAsync(new(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now,
                "что делать при артериальном кровотечении", 1), AssistantActivationKind.ManualText, "all", false, false), default);

            Assert.NotNull(answer);
            Assert.Equal(AnswerDecision.Abstain, answer.Decision);
            Assert.Equal(GroundedAnswerValidator.SafeAbstainMessage, answer.Message);
            Assert.Empty(answer.UsedFactIds);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task UserQuestion_ReachesValidatedDeterministicAnswer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gta-rp-integration-{Guid.NewGuid():N}.db");
        try
        {
            var article = new KnowledgePackArticle("a", "gta5rp", ["all"], "Контракт", "family", "contract", ["семейный контракт"], "summary", [new("f", "Проверьте актуальные требования", true)], [new("почему не запускается контракт", "Проверьте актуальные требования")], new("Test", null), 1, DateTimeOffset.UtcNow, true, false);
            var repository = new SqliteKnowledgeRepository($"Data Source={path}"); await repository.InitializeAsync([article], default);
            var now = DateTimeOffset.UtcNow; var entry = new TranscriptEntry(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "почему не запускается контракт", 1);
            var intent = await new RuleBasedIntentDetector(["контракт"]).DetectAsync(new([entry], entry), default); Assert.True(intent.ShouldConsiderHint);
            var knowledge = (await repository.SearchAsync(new(entry.Text), default)).Single();
            Assert.Equal(AnswerRoute.Deterministic, new AiRouter().Select(new(knowledge.HasVerifiedPreparedAnswer, true, false, false, false)));
            var json = JsonSerializer.Serialize(new { decision = "show", title = knowledge.Title, message = knowledge.PreparedAnswer, usedFactIds = new[] { "f" }, needsScreen = false, canSpeak = false });
            Assert.Equal(AnswerDecision.Show, new GroundedAnswerValidator().Validate(json, knowledge, "all", false).Decision);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task GameAudio_CannotStartPipeline()
    {
        var now = DateTimeOffset.UtcNow; var entry = new TranscriptEntry(Guid.NewGuid(), AudioSourceKind.GameAudio, now, now, "помощник почему контракт не запускается", 1);
        Assert.False((await new RuleBasedIntentDetector(["контракт"]).DetectAsync(new([entry], entry), default)).ShouldConsiderHint);
    }

    private sealed class CapturingOverlay : IOverlayService
    {
        public List<AssistantAnswer> Answers { get; } = [];
        public bool IsVisible => Answers.Count > 0;
        public Task ShowAsync(AssistantAnswer answer, CancellationToken cancellationToken)
        {
            Answers.Add(answer);
            return Task.CompletedTask;
        }
        public Task HideAsync() => Task.CompletedTask;
    }

    private sealed class UnavailableProviderCatalog : IChatProviderCatalog
    {
        public Task<ChatProviderAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ChatProviderAvailability(null, null, false, false));
    }

    private sealed class NullEventSink : ISessionEventSink
    {
        public void Write(SessionEvent value) { }
    }
}
