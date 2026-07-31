using GtaRpAssistant.Core;
using GtaRpAssistant.LocalData;
using Microsoft.Data.Sqlite;

namespace GtaRpAssistant.IntegrationTests;

public sealed class PersistentConversationTests
{
    [Fact]
    public void Store_PreservesConversationAndGroundingMetadataAcrossRestart()
    {
        using var database = TemporaryDatabase.Create();
        var created = DateTimeOffset.UtcNow;
        var first = new SqliteAssistantConversationStore(database.ConnectionString);
        first.Add(new(Guid.NewGuid(), created, ConversationRole.User, "Как вступить в Merryweather?", null, null, [], "clubs"));
        first.Add(new(Guid.NewGuid(), created.AddSeconds(1), ConversationRole.Assistant, "Нужен 10 уровень.", "prepared", "knowledge", ["community.clubs.merryweather.requirements"], "clubs"));
        var conversationId = first.CurrentConversationId;

        SqliteConnection.ClearAllPools();
        var reopened = new SqliteAssistantConversationStore(database.ConnectionString);
        var snapshot = reopened.GetCurrent();

        Assert.Equal(conversationId, reopened.CurrentConversationId);
        Assert.Equal(2, snapshot.Turns.Count);
        Assert.Equal("prepared", snapshot.Turns[1].ProviderId);
        Assert.Equal("knowledge", snapshot.Turns[1].ModelId);
        Assert.Equal("community.clubs.merryweather.requirements", Assert.Single(snapshot.Turns[1].UsedFactIds));
    }

    [Fact]
    public void Store_ListsRenamesOpensAndDeletesConversations()
    {
        using var database = TemporaryDatabase.Create();
        var store = new SqliteAssistantConversationStore(database.ConnectionString);
        store.Add(UserTurn("Первый вопрос"));
        var firstId = store.CurrentConversationId;
        store.StartNewConversation();
        store.Add(UserTurn("Второй вопрос"));
        var secondId = store.CurrentConversationId;

        store.RenameConversation(firstId, "Клубы GTA5RP");
        Assert.Equal(2, store.ListConversations().Count);
        Assert.True(store.TryOpenConversation(firstId));
        Assert.Equal("Клубы GTA5RP", store.ListConversations().Single(x => x.Id == firstId).Title);
        Assert.Equal("Первый вопрос", Assert.Single(store.GetCurrent().Turns).Text);

        store.DeleteConversation(firstId);
        Assert.Equal(secondId, store.CurrentConversationId);
        Assert.Single(store.ListConversations());
    }

    [Fact]
    public void Store_IgnoresMalformedFactMetadataInsteadOfCrashing()
    {
        using var database = TemporaryDatabase.Create();
        var store = new SqliteAssistantConversationStore(database.ConnectionString);
        store.Add(UserTurn("Проверка"));

        using (var connection = new SqliteConnection(database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE messages SET used_fact_ids_json='{broken';";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var reopened = new SqliteAssistantConversationStore(database.ConnectionString);
        Assert.Empty(Assert.Single(reopened.GetCurrent().Turns).UsedFactIds);
    }

    private static AssistantConversationTurn UserTurn(string text) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.User, text, null, null, [], "general");

    private sealed class TemporaryDatabase : IDisposable
    {
        private TemporaryDatabase(string directory)
        {
            Directory = directory;
            ConnectionString = $"Data Source={Path.Combine(directory, "assistant-data.db")}";
        }

        private string Directory { get; }
        public string ConnectionString { get; }

        public static TemporaryDatabase Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "GtaRpAssistant.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            return new(directory);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
