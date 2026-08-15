using GtaRpAssistant.Core;
using GtaRpAssistant.Knowledge;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.Knowledge.Tests;

public sealed class KnowledgeTests : IAsyncLifetime
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"gta-rp-{Guid.NewGuid():N}.db");
    private SqliteKnowledgeRepository Repository => new($"Data Source={_db}");
    private static KnowledgePackArticle Article(bool verified = true, DateTimeOffset? validUntil = null, string[]? scope = null) => new("a1", "gta5rp", scope ?? ["all"], "Контракты", "family", "contract", ["семейный контракт", "запустить контракт"], "Сведения", [new("f1", "Нужно проверить требования", verified)], [new("почему не запускается контракт", "Проверьте требования")], new("Test", null), 1, DateTimeOffset.UtcNow, verified, !verified, validUntil, verified ? "tester" : null);
    public async Task InitializeAsync() => await Repository.InitializeAsync([Article()], default);
    public Task DisposeAsync() { SqliteConnectionClear(); if (File.Exists(_db)) File.Delete(_db); return Task.CompletedTask; }
    private static void SqliteConnectionClear() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    [Fact] public async Task ExactAlias_FindsArticle() { var match = (await Repository.SearchAsync(new("семейный контракт"), default)).Single(); Assert.Equal("a1", match.ArticleId); Assert.Equal(KnowledgeRetrievalMethod.ExactAlias, match.Relevance!.Method); Assert.False(match.Relevance.RequiresSemanticRerank); }
    [Fact] public async Task PreparedAnswer_IsFound() { var m = (await Repository.SearchAsync(new("почему не запускается контракт"), default)).Single(); Assert.Equal("Проверьте требования", m.PreparedAnswer); Assert.True(m.HasVerifiedPreparedAnswer); }
    [Fact] public async Task Fts_FindsArticle() => Assert.NotEmpty(await Repository.SearchAsync(new("Контракты"), default));
    [Fact] public async Task FactFts_FindsArticleByFactText() => Assert.Equal("a1", (await Repository.SearchAsync(new("проверить требования"), default)).Single().ArticleId);
    [Fact] public async Task Fts_IgnoresQuestionStopWordsInsteadOfReturningUnrelatedArticle() => Assert.Empty(await Repository.SearchAsync(new("какая погода завтра в Саратове"), default));
    [Fact]
    public async Task Fts_UsesLightRussianStemmingForInflectedQuestion()
    {
        var article = Article() with
        {
            Id = "medical",
            Title = "Артериальное кровотечение",
            Aliases = ["артериальное кровотечение"],
            Summary = "Игровая медицинская помощь",
            Facts = [new("medical.fact", "При артериальном кровотечении нужен жгут выше раны", true)],
            PreparedAnswers = [],
        };
        await Repository.InitializeAsync([article], default);

        Assert.Equal("medical", Assert.Single(await Repository.SearchAsync(new("что делать при артериальном кровотечении"), default)).ArticleId);
    }
    [Fact] public async Task WrongServer_IsRejected() { await Repository.InitializeAsync([Article(scope: ["server-a"])], default); Assert.Empty(await Repository.SearchAsync(new("семейный контракт", "server-b"), default)); }
    [Fact] public async Task OutdatedArticle_IsMarked() { await Repository.InitializeAsync([Article(validUntil: DateTimeOffset.UtcNow.AddDays(-1))], default); Assert.True((await Repository.SearchAsync(new("семейный контракт"), default)).Single().IsOutdated); }
    [Fact] public async Task DemoArticle_IsNotVerifiedPreparedAnswer() { await Repository.InitializeAsync([Article(verified: false)], default); Assert.False((await Repository.SearchAsync(new("почему не запускается контракт"), default)).Single().HasVerifiedPreparedAnswer); }
    [Fact] public void Validator_RejectsDuplicateIds() { var a = Article(); Assert.Throws<InvalidDataException>(() => KnowledgePackValidator.Validate(new("p", "gta5rp", 1, DateTimeOffset.UtcNow, ["a", "b"]), [a, a])); }
    [Fact]
    public async Task SimilarTopResults_WithDifferentVerifiedFacts_AreMarkedAsConflict()
    {
        var first = Article() with { Id = "a1", Facts = [new("f1", "Первое проверенное условие", true)] };
        var second = Article() with { Id = "a2", Facts = [new("f2", "Другое проверенное условие", true)] };
        await Repository.InitializeAsync([first, second], default);
        var matches = await Repository.SearchAsync(new("семейный контракт"), default);
        Assert.True(matches[0].HasConflict);
    }

    [Fact]
    public async Task Fts_RelevanceFilter_RemovesWeakMatchesWithoutCreatingConflict()
    {
        var first = Article() with
        {
            Id = "a1",
            Title = "Первая работа",
            Aliases = ["первая"],
            Summary = "работа и заработок",
            Facts = [new("f1", "Первая работа доступна новичкам", true)],
            PreparedAnswers = [],
        };
        var second = Article() with
        {
            Id = "a2",
            Title = "Вторая работа",
            Aliases = ["вторая"],
            Summary = "работа и транспорт",
            Facts = [new("f2", "Вторая работа использует транспорт", true)],
            PreparedAnswers = [],
        };
        await Repository.InitializeAsync([first, second], default);

        var matches = await Repository.SearchAsync(new("какая работа использует транспорт"), default);

        var match = Assert.Single(matches);
        Assert.Equal("a2", match.ArticleId);
        Assert.False(match.HasConflict);
        Assert.Equal(KnowledgeRetrievalMethod.FullText, match.Relevance!.Method);
        Assert.False(match.Relevance.RequiresSemanticRerank);
    }

    [Fact]
    public async Task AmbiguousFts_RequestsOptionalSemanticRerank()
    {
        var first = Article() with { Id = "a1", Title = "Работа курьера", Aliases = ["курьер"], Summary = "работа транспорт", Facts = [new("f1", "Работа использует транспорт", true)], PreparedAnswers = [] };
        var second = Article() with { Id = "a2", Title = "Работа дальнобойщика", Aliases = ["дальнобойщик"], Summary = "работа транспорт", Facts = [new("f2", "Работа использует транспорт", true)], PreparedAnswers = [] };
        await Repository.InitializeAsync([first, second], default);

        var top = (await Repository.SearchAsync(new("работа транспорт"), default)).First();

        Assert.True(top.Relevance!.RequiresSemanticRerank);
        Assert.Equal("small_top_result_margin", top.Relevance.Reason);
    }

    [Fact]
    public async Task ExactOfficialAndCommunityMatches_PreferOfficialWithoutFalseConflict()
    {
        var official = Article() with
        {
            Id = "official.character.pets",
            Facts = [new("official.fact", "Официальное условие дрессировки", true)],
            PreparedAnswers = [new("как дрессировать питомца", "Награждайте только за правильную команду")],
        };
        var community = Article() with
        {
            Id = "community.guide.pet-training",
            Category = "community",
            Facts = [new("community.fact", "По данным игроков: дополнительная подсказка", true)],
            PreparedAnswers = [new("как дрессировать питомца", "По данным игроков: повторите команду")],
        };
        await Repository.InitializeAsync([community, official], default);

        var matches = await Repository.SearchAsync(new("как дрессировать питомца"), default);

        Assert.Equal(official.Id, matches[0].ArticleId);
        Assert.False(matches[0].HasConflict);
        Assert.False(matches[1].HasConflict);
    }

    [Fact]
    public async Task ServerScope_UsesExactValueInsteadOfSubstring()
    {
        await Repository.InitializeAsync([Article(scope: ["server-alpha"])], default);
        Assert.Empty(await Repository.SearchAsync(new("семейный контракт", "server"), default));
    }

    [Fact]
    public async Task LegacyDatabase_IsMigratedWithoutLosingArticlesOrScopes()
    {
        var legacyDb = Path.Combine(Path.GetTempPath(), $"gta-rp-legacy-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={legacyDb}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE articles(id TEXT PRIMARY KEY, title TEXT NOT NULL, project TEXT NOT NULL, server_scope TEXT NOT NULL, summary TEXT NOT NULL, updated_at TEXT NOT NULL, verified INTEGER NOT NULL, demo INTEGER NOT NULL, valid_until TEXT NULL, source_title TEXT NOT NULL);
                    CREATE TABLE aliases(article_id TEXT NOT NULL, alias TEXT NOT NULL);
                    INSERT INTO articles VALUES('legacy','Legacy','gta5rp','server-a,server-b','summary','2026-01-01T00:00:00+00:00',0,1,NULL,'Legacy source');
                    INSERT INTO aliases VALUES('legacy','legacy alias');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var repository = new SqliteKnowledgeRepository($"Data Source={legacyDb}");
            await repository.InitializeAsync([], default);

            var match = Assert.Single(await repository.SearchAsync(new("legacy alias", "server-b"), default));
            Assert.Equal("legacy", match.ArticleId);

            await using var migrated = new SqliteConnection($"Data Source={legacyDb}");
            await migrated.OpenAsync();
            await using var version = migrated.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(KnowledgeDatabaseMigrator.CurrentVersion, Convert.ToInt32(await version.ExecuteScalarAsync()));
            await using var columns = migrated.CreateCommand();
            columns.CommandText = "SELECT article_version, source_url, verified_by FROM articles WHERE id='legacy';";
            await using var reader = await columns.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(legacyDb)) File.Delete(legacyDb);
        }
    }

    [Fact]
    public async Task CurrentSchema_PersistsSourceVersionAndReviewer()
    {
        var article = Article() with { Source = new("Official", "https://example.test/rules"), Version = 7, VerifiedBy = "reviewer" };
        await Repository.InitializeAsync([article], default);

        await using var connection = new SqliteConnection($"Data Source={_db}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT source_url, article_version, verified_by FROM articles WHERE id='a1';";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("https://example.test/rules", reader.GetString(0));
        Assert.Equal(7, reader.GetInt32(1));
        Assert.Equal("reviewer", reader.GetString(2));
    }

    [Fact]
    public void Governance_RejectsVerifiedArticleWithoutHttpsSourceOrReviewer()
    {
        var article = Article() with { Source = new("Official", "http://example.test/rules"), VerifiedBy = null };
        var report = KnowledgeGovernanceValidator.Inspect([article], DateTimeOffset.UtcNow);

        Assert.Contains(report.Issues, x => x.Code == "invalid-source" && x.Severity == KnowledgeIssueSeverity.Error);
        Assert.Contains(report.Issues, x => x.Code == "missing-reviewer" && x.Severity == KnowledgeIssueSeverity.Error);
    }

    [Fact]
    public void Governance_AcceptsReviewedHttpsArticleWithFutureExpiry()
    {
        var now = new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
        var article = Article(validUntil: now.AddMonths(3)) with
        {
            Source = new("Official", "https://example.test/rules"),
            UpdatedAt = now.AddDays(-1),
            VerifiedBy = "reviewer"
        };

        Assert.Empty(KnowledgeGovernanceValidator.Inspect([article], now).Issues);
    }

    [Fact]
    public async Task OfficialCompactPack_LoadsAndStaysWithinPerArticleBudget()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp");
        var pack = await new KnowledgePackLoader().LoadPackAsync(directory, default);

        Assert.Equal("gta5rp.official.compact", pack.Manifest.Id);
        Assert.Equal(48, pack.Articles.Count);
        Assert.All(pack.Articles, article =>
        {
            Assert.InRange(article.Facts.Count, 1, GroundingContextSelector.DefaultMaxFacts);
            Assert.True(article.Facts.Sum(x => x.Text.Length) <= GroundingContextSelector.DefaultMaxCharacters);
        });
    }

    [Theory]
    [InlineData("комиссия банкомата", "official.finance.bank")]
    [InlineData("как разделить стак", "official.character.inventory")]
    [InlineData("как создать организацию", "official.organization.basics")]
    [InlineData("почему не идет прогресс контракта", "official.organization.contracts")]
    [InlineData("где вход в шахту", "official.work.mine")]
    [InlineData("что нужно для работы в такси", "official.work.taxi")]
    [InlineData("где заказы дальнобойщика", "official.work.trucker")]
    [InlineData("что нужно для рыбалки", "official.work.fishing")]
    [InlineData("где взять вызов пожарного", "official.work.firefighter")]
    [InlineData("что нужно для поиска сокровищ", "official.work.treasures")]
    [InlineData("какой уровень нужен почтальону", "official.work.postman")]
    [InlineData("где взять заказы курьера", "official.work.courier")]
    [InlineData("где продать шкуры", "official.work.hunting")]
    [InlineData("как оплатить налог на дом", "official.property.home")]
    [InlineData("где купить квартиру", "official.property.apartment")]
    [InlineData("сколько коммерческих помещений можно арендовать", "official.property.commercial")]
    [InlineData("как эвакуировать свою машину", "official.transport.types")]
    [InlineData("как поставить машину на учет", "official.transport.registration")]
    [InlineData("как проверить износ машины", "official.transport.service")]
    [InlineData("какой первый взнос по лизингу", "official.transport.leasing")]
    [InlineData("как включить беззвучный режим телефона", "official.phone.info")]
    [InlineData("как пригласить в brawl", "official.phone.brawl")]
    [InlineData("как опубликовать сайт в dash", "official.phone.browser")]
    [InlineData("чем ординар отличается от экспресса", "official.phone.fivebets")]
    [InlineData("сколько стоит абонемент в спортзал", "official.activities.sport")]
    [InlineData("как играть в дартс", "official.activities.sport-games")]
    [InlineData("какая ставка в тренировочном комплексе", "official.activities.training-complex")]
    [InlineData("что нужно для вступления в car meet", "official.clubs.car-meet")]
    [InlineData("скидка на эвакуацию car meet", "official.clubs.car-meet-perks")]
    [InlineData("какие есть мотоклубы", "official.clubs.bikers")]
    [InlineData("где купить алкоголь для топливо для души", "official.clubs.bikers-activities")]
    public async Task OfficialCompactPack_AnswersCommonQuestions(string question, string expectedArticle)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp");
        var articles = await new KnowledgePackLoader().LoadAsync(directory, default);
        await Repository.InitializeAsync(articles, default);

        Assert.Equal(expectedArticle, (await Repository.SearchAsync(new(question), default)).First().ArticleId);
    }

    [Fact]
    public async Task SourceChecker_RejectsLoopbackSourcesWithoutSendingARequest()
    {
        using var client = new HttpClient(new ThrowingHandler());
        var article = Article() with { Source = new("Local", "https://127.0.0.1/private") };

        var result = Assert.Single(await KnowledgeSourceChecker.CheckAsync([article], client, default));
        Assert.False(result.Available);
        Assert.Contains("local or private", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EclipseCriminalArticles_SpokenNumbersResolveToPreparedLocalAnswer()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp");
        var articles = await new KnowledgePackLoader().LoadAsync(directory, default);
        await Repository.InitializeAsync(articles, default);

        var question = "лоберте слушай мне нужно узнать что обозначают статьи уголовного кодекса двенадцать точка шесть двенадцать точка один и семнадцать точка четыре на эклипсе";
        var match = Assert.Single(await Repository.SearchAsync(new(question), default));

        Assert.Equal("official.eclipse.legal.key-articles", match.ArticleId);
        Assert.True(match.HasVerifiedPreparedAnswer);
        Assert.Contains("УК 12.1", match.PreparedAnswer, StringComparison.Ordinal);
        Assert.Contains("УК 12.6", match.PreparedAnswer, StringComparison.Ordinal);
        Assert.Contains("УК 17.4", match.PreparedAnswer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("что такое ук 12.1", "official.eclipse.legal.criminal.12-1")]
    [InlineData("что означает статья 17.4 ук", "official.eclipse.legal.criminal.17-4")]
    [InlineData("что такое дк 40", "official.eclipse.legal.road.40")]
    [InlineData("что означает статья 6.2 ук", "official.eclipse.legal.criminal.6-2")]
    public async Task EclipseLegalBase_IndexesEveryArticleSeparately(string question, string expectedId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "official", "eclipse-legal-base.json");
        var articles = await new EclipseLegalReferenceLoader().LoadAsync(path, default);
        Assert.True(articles.Count > 300, $"Expected full legal index, got {articles.Count} articles.");
        await Repository.InitializeAsync(articles, default);

        var match = (await Repository.SearchAsync(new(question), default)).First();

        Assert.Equal(expectedId, match.ArticleId);
        Assert.True(match.HasVerifiedPreparedAnswer);
        Assert.NotNull(match.PreparedAnswer);
        Assert.True(match.PreparedAnswer!.Length <= 350);
    }

    [Fact]
    public async Task CommunityReference_LoadsLargeStructuredCatalogWithPlayerLabel()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community");
        var articles = await new CommunityReferenceLoader().LoadAsync(directory, default);

        Assert.Equal(445, articles.Count);
        Assert.Contains(articles, x => x.Title == "Вращайте барабан");
        Assert.Contains(articles, x => x.Title == "Рецепт: Оливье");
        Assert.Contains(articles, x => x.Title == "Артериальное кровотечение");
        Assert.All(articles, x => Assert.StartsWith("По данным игроков:", Assert.Single(x.Facts).Text));
        Assert.All(articles.SelectMany(x => x.PreparedAnswers), x => Assert.StartsWith("По данным игроков:", x.Answer));
    }

    [Theory]
    [InlineData("награда за Вращайте барабан", "Вращайте барабан")]
    [InlineData("рецепт Оливье", "Рецепт: Оливье")]
    [InlineData("что делать при артериальное кровотечение", "Артериальное кровотечение")]
    [InlineData("износ двигателя", "Износ: Двигатель")]
    public async Task CommunityReference_IsSearchable(string question, string expectedTitle)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community");
        var articles = await new CommunityReferenceLoader().LoadAsync(directory, default);
        await Repository.InitializeAsync(articles, default);

        var match = (await Repository.SearchAsync(new(question), default)).First();
        Assert.Equal(expectedTitle, match.Title);
        Assert.StartsWith("По данным игроков:", Assert.Single(match.Facts).Text);
    }

    [Theory]
    [InlineData("когда следующий ивент", "Примерный календарь событий", "разработчики могут изменить даты")]
    [InlineData("где взять макросы", "Где искать макросы и настройки интерфейса", "материалах фракции")]
    [InlineData("доход дальнобойщика на 5 уровне", "Доход дальнобойщика на 5 уровне", "100–120 тыс.")]
    [InlineData("как дрессировать питомца", "Как дрессировать питомца", "Четыре правильных выполнения")]
    [InlineData("какое лакомство дать собаке", "Лакомства для дрессировки питомцев", "Молодог")]
    [InlineData("что дает вопрос ровные ли сегодня дороги", "Шар: ровные ли сегодня дороги", "расход топлива в 2 раза")]
    [InlineData("что дает вопрос сегодня хороший улов", "Шар: сегодня хороший улов", "Верхняя граница")]
    [InlineData("какие задания в merryweather", "Задания клуба Merryweather", "Подводный сапёр")]
    [InlineData("что дают ранги rednecks", "Rednecks: вступление и бонусы рангов", "суммарно +16%")]
    [InlineData("какие задания в car meet", "Задания клуба Car Meet", "100 000 очков дрифта")]
    [InlineData("сколько жертвовать epsilon", "Репутация Epsilon за пожертвования", "83 000 → 100")]
    [InlineData("где найти discord клубов gta5rp", "Сообщество игроков клубов GTA5RP", "внешний ресурс сообщества")]
    public async Task CommunityPlayerGuides_HaveExactPreparedAnswers(string question, string expectedTitle, string expectedText)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community");
        var articles = await new CommunityReferenceLoader().LoadAsync(directory, default);
        await Repository.InitializeAsync(articles, default);

        var match = (await Repository.SearchAsync(new(question), default)).First();
        Assert.Equal(expectedTitle, match.Title);
        Assert.NotNull(match.PreparedAnswer);
        Assert.StartsWith("По данным игроков:", match.PreparedAnswer);
        Assert.Contains(expectedText, match.PreparedAnswer, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Request must not be sent.");
    }
}
