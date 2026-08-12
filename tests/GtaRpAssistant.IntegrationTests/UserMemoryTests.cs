using GtaRpAssistant.Core;
using GtaRpAssistant.LocalData;

namespace GtaRpAssistant.IntegrationTests;

public sealed class UserMemoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gta-rp-memory-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_PersistsCrudAndPersonalityInDedicatedDatabase()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "user-memory.db");
        var store = new SqliteUserMemoryStore($"Data Source={path};Pooling=False");
        var created = store.Upsert(null, UserMemoryCategory.FavoriteActivity, "Люблю работать дальнобойщиком");
        store.Upsert(created.Id, UserMemoryCategory.PlayStyle, "Предпочитаю спокойную ролевую игру");
        store.SavePersonality(new(2, 1, 0, 2));

        var reopened = new SqliteUserMemoryStore($"Data Source={path};Pooling=False");
        var item = Assert.Single(reopened.List());
        Assert.Equal(UserMemoryCategory.PlayStyle, item.Category);
        Assert.Equal("Предпочитаю спокойную ролевую игру", item.Content);
        Assert.Equal(new PersonalityProfile(2, 1, 0, 2), reopened.GetPersonality());

        reopened.Delete(item.Id);
        Assert.Empty(reopened.List());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ContextProvider_SelectsRelevantConfirmedMemoryWithoutChangingProfile()
    {
        Directory.CreateDirectory(_directory);
        var store = new SqliteUserMemoryStore($"Data Source={Path.Combine(_directory, "user-memory.db")};Pooling=False");
        store.Upsert(null, UserMemoryCategory.FavoriteActivity, "Люблю рыбалку и морские занятия");
        store.Upsert(null, UserMemoryCategory.FavoriteActivity, "Люблю работу таксиста");
        store.SavePersonality(new(9, -1, 1, 0));

        var context = new UserPersonalizationContextProvider(store).Build("Как начать рыбалку?", 1);
        Assert.Single(context.Memories);
        Assert.Contains("рыбалку", context.Memories[0].Content);
        Assert.Equal(new PersonalityProfile(2, 0, 1, 0), context.Personality);
    }

    [Fact]
    public void Clear_RemovesMemoriesButKeepsIndependentPersonality()
    {
        Directory.CreateDirectory(_directory);
        var store = new SqliteUserMemoryStore($"Data Source={Path.Combine(_directory, "user-memory.db")};Pooling=False");
        store.Upsert(null, UserMemoryCategory.ConfirmedFact, "Игрок сам подтвердил свой ник");
        store.SavePersonality(new(0, 0, 0, 1));
        store.Clear();
        Assert.Empty(store.List());
        Assert.Equal(new PersonalityProfile(0, 0, 0, 1), store.GetPersonality());
    }

    [Fact]
    public void ExplicitFeedback_AdaptsOnlyAfterOptInAndCreatesTransparentLog()
    {
        Directory.CreateDirectory(_directory);
        var store = new SqliteUserMemoryStore($"Data Source={Path.Combine(_directory, "user-memory.db")};Pooling=False");
        var service = new UserPersonalizationContextProvider(store);
        Assert.False(service.ApplyExplicitFeedback("Отвечай короче"));
        Assert.Equal(1, store.GetPersonality().DetailLevel);

        store.SavePersonality(new PersonalityProfile(1, 0, 1, 0, AdaptiveEnabled: true));
        Assert.True(service.ApplyExplicitFeedback("Пожалуйста, отвечай короче"));
        Assert.Equal(0, store.GetPersonality().DetailLevel);
        var change = Assert.Single(store.ListPersonalityChanges());
        Assert.Equal("detail", change.Trait);
        Assert.DoesNotContain("Пожалуйста", change.Reason);

        store.ResetPersonality();
        Assert.Equal(new PersonalityProfile(), store.GetPersonality());
        Assert.Empty(store.ListPersonalityChanges());
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
