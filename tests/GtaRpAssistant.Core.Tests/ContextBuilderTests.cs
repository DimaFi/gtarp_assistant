using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class ContextBuilderTests
{
    [Fact]
    public void BalancedContext_IsBoundedAndKeepsVerifiedFactsAheadOfHistoryAndMemory()
    {
        var now = DateTimeOffset.UtcNow;
        var facts = Enumerable.Range(0, 10).Select(i => new KnowledgeFact(
            $"fact-{i}", "article", $"Контракт: проверенный факт {i} {new string('ф', 80)}", true, now)).ToArray();
        var entries = Enumerable.Range(0, 10).Select(i => new TranscriptEntry(
            Guid.NewGuid(), AudioSourceKind.GameAudio, now.AddSeconds(i), now.AddSeconds(i), $"игровая реплика {i} {new string('т', 180)}", 1)).ToArray();
        var turns = Enumerable.Range(0, 10).Select(i => new AssistantConversationTurn(
            Guid.NewGuid(), now.AddMinutes(i), i % 2 == 0 ? ConversationRole.User : ConversationRole.Assistant,
            $"реплика диалога {i} {new string('д', 180)}", null, null, [], "article")).ToArray();
        var memories = Enumerable.Range(0, 10).Select(i => new UserMemoryItem(
            Guid.NewGuid(), UserMemoryCategory.PlayStyle, $"предпочтение {i} {new string('п', 70)}", now, now.AddMinutes(i))).ToArray();
        var builder = new AssistantContextBuilder();

        var result = builder.Build(new(
            "Что важно знать про контракт?",
            "all",
            new("article", "Контракт", 1, facts, false, false),
            new(entries, null),
            AssistantRequestType.DirectKnowledgeQuestion,
            turns,
            new(memories, new(2, 1, 1, 0))));

        Assert.NotEmpty(result.Request.VerifiedFacts);
        Assert.All(result.Request.VerifiedFacts, fact => Assert.True(fact.Verified));
        Assert.True(result.Request.VerifiedFacts.Sum(x => x.Text.Length) <= result.Budget.FactsCharacters);
        Assert.True(result.Request.TranscriptContext.Length <= result.Budget.TranscriptCharacters);
        Assert.True(result.Request.Conversation!.Sum(x => x.Text.Length) <= result.Budget.ConversationCharacters);
        Assert.True(result.Request.Personalization!.Memories.Sum(x => x.Content.Length) <= result.Budget.MemoryCharacters);
        Assert.InRange(result.Request.Personalization.Memories.Count, 0, 3);
        Assert.True(result.WasTrimmed);
        Assert.True(result.EstimatedInputTokens <= result.Budget.TargetInputTokens);
        Assert.Equal(300, result.Request.MaxOutputTokens);
    }

    [Fact]
    public void ProblemSolving_GetsLargerButStillBoundedOutputBudget()
    {
        var fact = new KnowledgeFact("f", "a", "Проверь условия контракта", true, DateTimeOffset.UtcNow);
        var result = new AssistantContextBuilder().Build(new(
            "Почему контракт не запускается?", "all", new("a", "Контракт", 1, [fact], false, false),
            new([], null), AssistantRequestType.ProblemSolving, [], null));

        Assert.Equal(450, result.Request.MaxOutputTokens);
        Assert.True(result.EstimatedInputTokens <= result.Budget.TargetInputTokens);
    }

    [Fact]
    public void CurrentUserRequest_IsNotDuplicatedInsideTranscriptContext()
    {
        var now = DateTimeOffset.UtcNow;
        var current = new TranscriptEntry(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "текущий вопрос marker", 1);
        var previous = new TranscriptEntry(Guid.NewGuid(), AudioSourceKind.GameAudio, now.AddSeconds(-1), now, "прошлая реплика", 1);
        var fact = new KnowledgeFact("f", "a", "Проверенный факт", true, now);

        var result = new AssistantContextBuilder().Build(new(
            current.Text, "all", new("a", "Статья", 1, [fact], false, false),
            new([previous, current], current), AssistantRequestType.DirectKnowledgeQuestion, [], null));

        Assert.Contains("прошлая реплика", result.Request.TranscriptContext);
        Assert.DoesNotContain("текущий вопрос marker", result.Request.TranscriptContext);
    }
}
