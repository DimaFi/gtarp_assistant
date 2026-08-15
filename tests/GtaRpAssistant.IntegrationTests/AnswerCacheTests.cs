using GtaRpAssistant.Core;
using GtaRpAssistant.LocalData;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.IntegrationTests;

public sealed class AnswerCacheTests
{
    [Fact]
    public async Task SqliteCache_PersistsValidatedAnswerWithoutRawQuestionAndTracksHits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gta-rp-answer-cache-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            var fact = new KnowledgeFact("f", "a", "Проверенный факт", true, DateTimeOffset.UtcNow);
            var key = AnswerCacheKeyBuilder.Create("секретный вопрос пользователя", "all", new("a", "Статья", 1, [fact], false, false), null);
            var answer = new AssistantAnswer(AnswerDecision.Show, "Ответ", "Безопасный ответ", ["f"], "Статья", fact.UpdatedAt, false,
                GroundedAnswerValidator.PassedReason, ProviderId: "local", ModelId: "model");

            using (var cache = new SqliteAnswerCache(connectionString))
                await cache.StoreAsync(key, answer, TimeSpan.FromDays(2), default);

            using (var reopened = new SqliteAnswerCache(connectionString))
            {
                var first = await reopened.TryGetAsync(key, default);
                var second = await reopened.TryGetAsync(key, default);
                Assert.Equal("Безопасный ответ", first!.Answer.Message);
                Assert.Equal(1, first.HitCount);
                Assert.Equal(2, second!.HitCount);
            }

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT cache_key,payload_json FROM answer_cache;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(key, reader.GetString(0));
            Assert.DoesNotContain("секретный вопрос пользователя", reader.GetString(1), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }

    [Fact]
    public async Task SqliteCache_ClearRemovesEntriesAndNonShowAnswerIsNotStored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gta-rp-answer-cache-{Guid.NewGuid():N}.db");
        try
        {
            using var cache = new SqliteAnswerCache($"Data Source={path}");
            var key = new string('a', 64);
            var show = new AssistantAnswer(AnswerDecision.Show, "Ответ", "Текст", [], null, null, false, "ok");
            var abstain = show with { Decision = AnswerDecision.Abstain };
            await cache.StoreAsync(key, abstain, TimeSpan.FromDays(1), default);
            Assert.Null(await cache.TryGetAsync(key, default));

            await cache.StoreAsync(key, show, TimeSpan.FromDays(1), default);
            Assert.NotNull(await cache.TryGetAsync(key, default));
            await cache.ClearAsync(default);
            Assert.Null(await cache.TryGetAsync(key, default));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }
}
