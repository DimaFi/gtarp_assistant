using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class ConversationTests
{
    [Fact]
    public void Store_RespectsCapacityAndTruncatesTurnData()
    {
        var store = new InMemoryAssistantConversationStore(4);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++)
            store.Add(Turn(now.AddSeconds(i), $"message-{i}" + new string('x', 1300), "article"));

        var snapshot = store.GetCurrent();

        Assert.Equal(4, snapshot.Turns.Count);
        Assert.EndsWith("message-5" + new string('x', 1191), snapshot.Turns[^1].Text);
        Assert.All(snapshot.Turns, turn => Assert.True(turn.Text.Length <= 1200));
    }

    [Fact]
    public void Store_StartsFreshAfterIdlePeriodOrSituationChange()
    {
        var store = new InMemoryAssistantConversationStore(idleTtl: TimeSpan.FromMinutes(1));
        var now = DateTimeOffset.UtcNow;
        store.Add(Turn(now, "first", "article-a"));
        store.Add(Turn(now.AddMinutes(2), "after idle", "article-a"));
        Assert.Single(store.GetCurrent().Turns);

        store.Add(Turn(now.AddMinutes(2).AddSeconds(1), "new situation", "article-b"));
        var snapshot = store.GetCurrent();
        Assert.Single(snapshot.Turns);
        Assert.Equal("article-b", snapshot.SituationId);
    }

    [Fact]
    public void Classifier_DetectsFollowUpAndProblemSolving()
    {
        var conversation = new AssistantConversationSnapshot([Turn(DateTimeOffset.UtcNow, "Как выполнить контракт?", "contract")], DateTimeOffset.UtcNow, "contract");

        Assert.Equal(AssistantRequestType.FollowUpQuestion, AssistantRequestClassifier.Classify("А почему?", conversation));
        Assert.Equal(AssistantRequestType.ProblemSolving, AssistantRequestClassifier.Classify("Контракт не запускается, что проверить?", new([], null, null)));
    }

    [Theory]
    [InlineData("Подскажи пожалуйста, как мне начать зарабатывать?", "Заработок и работа в GTA RP")]
    [InlineData("Что делать с машиной на штрафстоянке?", "Транспорт в GTA RP")]
    [InlineData("Как дрессировать питомца?", "Питомцы и дрессировка")]
    [InlineData("Где находится больница?", "Где находится больница")]
    public void ConversationTitle_IsGeneratedFromIntentAndRemainsCompact(string question, string expected) =>
        Assert.Equal(expected, ConversationTitleGenerator.FromContext(question));

    [Fact]
    public void Store_AutomaticallyNamesChatButAllowsManualRename()
    {
        var store = new InMemoryAssistantConversationStore();
        store.Add(Turn(DateTimeOffset.UtcNow, "Как мне начать зарабатывать без машины?", "general"));
        var conversation = Assert.Single(store.ListConversations());
        Assert.Equal("Заработок и работа в GTA RP", conversation.Title);

        store.RenameConversation(conversation.Id, "Мой план новичка");

        Assert.Equal("Мой план новичка", Assert.Single(store.ListConversations()).Title);
    }

    [Fact]
    public void Store_CanManageMultipleConversations()
    {
        var store = new InMemoryAssistantConversationStore();
        store.Add(Turn(DateTimeOffset.UtcNow, "Первый вопрос", "general"));
        var firstId = store.CurrentConversationId;

        store.StartNewConversation();
        store.Add(Turn(DateTimeOffset.UtcNow.AddSeconds(1), "Второй вопрос", "general"));
        var secondId = store.CurrentConversationId;
        store.RenameConversation(firstId, "Сохранённый разговор");

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, store.ListConversations().Count);
        Assert.True(store.TryOpenConversation(firstId));
        Assert.Equal("Первый вопрос", Assert.Single(store.GetCurrent().Turns).Text);
        Assert.Equal("Сохранённый разговор", store.ListConversations().Single(x => x.Id == firstId).Title);

        store.DeleteConversation(firstId);
        Assert.Equal(secondId, store.CurrentConversationId);
        Assert.Single(store.ListConversations());
    }

    [Fact]
    public void ConfigurableStore_DoesNotCreatePersistentStoreUntilOptIn()
    {
        var enabled = false;
        var factoryCalls = 0;
        var transient = new InMemoryAssistantConversationStore();
        var persistent = new InMemoryAssistantConversationStore();
        var store = new ConfigurableAssistantConversationStore(
            () => enabled,
            transient,
            () => { factoryCalls++; return persistent; });

        store.Add(Turn(DateTimeOffset.UtcNow, "Обычный вопрос", "general"));
        Assert.Equal(0, factoryCalls);
        Assert.Single(transient.GetCurrent().Turns);
        Assert.Empty(persistent.GetCurrent().Turns);

        enabled = true;
        store.Add(Turn(DateTimeOffset.UtcNow.AddSeconds(1), "Запомни этот вопрос", "general"));
        Assert.Equal(1, factoryCalls);
        Assert.Single(persistent.GetCurrent().Turns);

        enabled = false;
        Assert.Equal("Обычный вопрос", Assert.Single(store.GetCurrent().Turns).Text);
    }

    private static AssistantConversationTurn Turn(DateTimeOffset time, string text, string situation) =>
        new(Guid.NewGuid(), time, ConversationRole.User, text, null, null, [], situation);
}
