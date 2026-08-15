using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class ValidatorTests
{
    private static readonly KnowledgeFact Fact = new("f1", "a1", "Нужно 4 участника", true, DateTimeOffset.UtcNow);
    private static KnowledgeMatch Match(bool conflict = false, bool outdated = false) => new("a1", "Статья", 1, [Fact], conflict, outdated);
    private static string Json(string message = "Нужно 4 участника", string[]? ids = null) => System.Text.Json.JsonSerializer.Serialize(new { decision = "show", title = "Ответ", message, usedFactIds = ids ?? ["f1"], needsScreen = false, canSpeak = true });
    [Fact] public void InvalidJson_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate("{", Match(), "all", false).Decision);
    [Fact] public void UnknownFact_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json(ids: ["bad"]), Match(), "all", false).Decision);
    [Fact] public void UnsupportedNumber_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json("Нужно 5 участников"), Match(), "all", false).Decision);
    [Fact] public void LongMessage_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json(new string('x', 351)), Match(), "all", false).Decision);
    [Fact] public void Conflict_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json(), Match(conflict: true), "all", false).Decision);
    [Fact] public void Outdated_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json(), Match(outdated: true), "all", false).Decision);
    [Fact] public void ValidAnswer_ShowsAndForcesVoiceOff() { var answer = new GroundedAnswerValidator().Validate(Json(), Match(), "all", false); Assert.Equal(AnswerDecision.Show, answer.Decision); Assert.False(answer.CanSpeak); }
    [Fact] public void CommunityAnswer_PreservesPlayerDataLabel() { var fact = Fact with { Text = "По данным игроков: награда 25 BP" }; var match = Match() with { Facts = [fact] }; var answer = new GroundedAnswerValidator().Validate(Json("Награда 25 BP"), match, "all", false); Assert.StartsWith("По данным игроков:", answer.Message); }
    [Fact] public void UnsupportedUrl_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json("Откройте https://evil.example"), Match(), "all", false).Decision);
    [Fact] public void ForbiddenAutomation_Abstains() => Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(Json("Используйте автокликер"), Match(), "all", false).Decision);

    [Fact]
    public void OpenConversation_AllowsNaturalAnswerWithoutFacts()
    {
        var answer = new GroundedAnswerValidator().Validate(
            Json("Если вы устали, фильм потребует меньше вовлечения.", []),
            AssistantOpenConversationPolicy.EmptyMatch(), "all", false, AssistantResponseMode.OpenConversation);

        Assert.Equal(AnswerDecision.Show, answer.Decision);
        Assert.Equal(GroundedAnswerValidator.PassedReason, answer.DiagnosticReason);
        Assert.Empty(answer.UsedFactIds);
    }

    [Fact]
    public void OpenConversation_RejectsInventedFactId()
    {
        var answer = new GroundedAnswerValidator().Validate(
            Json("Попробуйте другой вариант.", ["invented.fact"]),
            AssistantOpenConversationPolicy.EmptyMatch(), "all", false, AssistantResponseMode.OpenConversation);

        Assert.Equal(AnswerDecision.Abstain, answer.Decision);
        Assert.NotEqual(GroundedAnswerValidator.PassedReason, answer.DiagnosticReason);
    }

    [Fact]
    public void NoVerifiedFacts_ReplacesGameRumorWithSafeLocalAbstain()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "abstain",
            title = "Скрытая награда",
            message = "Игроки говорят, что за это дают секретный приз",
            usedFactIds = Array.Empty<string>(),
            needsScreen = false,
            canSpeak = false,
        });

        var answer = new GroundedAnswerValidator().Validate(json, new("none", "Нет данных", 0, [], false, false), "all", false);

        Assert.Equal(AnswerDecision.Abstain, answer.Decision);
        Assert.Equal(GroundedAnswerValidator.SafeAbstainTitle, answer.Title);
        Assert.Equal(GroundedAnswerValidator.SafeAbstainMessage, answer.Message);
        Assert.DoesNotContain("награ", answer.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(GroundedAnswerValidator.PassedReason, answer.DiagnosticReason);
    }

    [Fact]
    public void CanonicalAbstainWithoutVerifiedFacts_PassesValidation()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "abstain",
            title = GroundedAnswerValidator.SafeAbstainTitle,
            message = GroundedAnswerValidator.SafeAbstainMessage,
            summary = "",
            steps = Array.Empty<string>(),
            possibleCauses = Array.Empty<string>(),
            usedFactIds = Array.Empty<string>(),
            needsScreen = false,
            canSpeak = false,
            needsMoreInformation = false,
            needsVisualContext = false,
            followUpSuggestions = Array.Empty<string>(),
        });

        var answer = new GroundedAnswerValidator().Validate(json, new("none", "Нет данных", 0, [], false, false), "all", false);

        Assert.Equal(AnswerDecision.Abstain, answer.Decision);
        Assert.Equal(GroundedAnswerValidator.PassedReason, answer.DiagnosticReason);
    }

    [Fact]
    public void AbstainRumorIsRejectedEvenWhenVerifiedFactsExist()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "abstain",
            title = "Возможный ответ",
            message = "Игроки говорят о другой скрытой механике",
            usedFactIds = Array.Empty<string>(),
            needsScreen = false,
            canSpeak = false,
        });

        var answer = new GroundedAnswerValidator().Validate(json, Match(), "all", false);

        Assert.Equal(AnswerDecision.Abstain, answer.Decision);
        Assert.Equal(GroundedAnswerValidator.SafeAbstainMessage, answer.Message);
        Assert.NotEqual(GroundedAnswerValidator.PassedReason, answer.DiagnosticReason);
    }

    [Fact]
    public void StructuredProblemSolution_IsPreserved()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "show",
            title = "Контракт",
            message = "Проверьте актуальные требования",
            usedFactIds = new[] { "f1" },
            needsScreen = false,
            canSpeak = false,
            summary = "Сначала проверьте условия",
            steps = new[] { "Откройте меню контракта", "Сверьте требования" },
            possibleCauses = new[] { "Условие ещё не выполнено" },
            followUpSuggestions = new[] { "Какое условие отображается?" },
        });

        var answer = new GroundedAnswerValidator().Validate(json, Match(), "all", false);

        Assert.Equal(AnswerDecision.Show, answer.Decision);
        Assert.Equal(2, answer.ProblemSolution!.Steps.Count);
        Assert.Single(answer.ProblemSolution.FollowUpSuggestions);
    }

    [Fact]
    public void UnsupportedNumberInsideStep_Abstains()
    {
        var json = JsonSerializer.Serialize(new
        {
            decision = "show",
            title = "Ответ",
            message = "Нужно 4 участника",
            usedFactIds = new[] { "f1" },
            needsScreen = false,
            canSpeak = false,
            steps = new[] { "Подождите 99 минут" },
        });

        Assert.Equal(AnswerDecision.Abstain, new GroundedAnswerValidator().Validate(json, Match(), "all", false).Decision);
    }
}
