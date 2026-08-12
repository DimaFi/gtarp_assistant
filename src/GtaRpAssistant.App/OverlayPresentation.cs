using GtaRpAssistant.Core;

namespace GtaRpAssistant.App;

public enum OverlayTone { Success, Warning, Neutral }
public enum OverlayActivity { None, Listening, Thinking, Answering }

public sealed record OverlayPresentation(
    string Title,
    string Message,
    string Status,
    string Source,
    string Updated,
    bool IsCommunity,
    OverlayTone Tone,
    string? Summary = null,
    IReadOnlyList<string>? Steps = null,
    IReadOnlyList<string>? PossibleCauses = null,
    IReadOnlyList<string>? FollowUpSuggestions = null,
    string? Provider = null,
    OverlayActivity Activity = OverlayActivity.None)
{
    public IReadOnlyList<string> CompactSteps => Steps?.Take(3).ToArray() ?? [];
}

public static class OverlayPresentationFactory
{
    private const string CommunityPrefix = "По данным игроков:";

    public static OverlayPresentation Create(AssistantAnswer answer)
    {
        var tone = answer.Decision switch
        {
            AnswerDecision.Show => OverlayTone.Success,
            AnswerDecision.AskForMoreInformation => OverlayTone.Warning,
            _ => OverlayTone.Neutral,
        };
        var status = answer.Decision switch
        {
            AnswerDecision.Show => "Подтверждено",
            AnswerDecision.AskForMoreInformation => "Нужно уточнение",
            _ => "Недостаточно данных",
        };
        var source = answer.SourceTitle is null ? "Проверенный источник не найден" : answer.SourceTitle;
        var updated = answer.SourceUpdatedAt is null ? "Дата обновления неизвестна" : $"Обновлено {answer.SourceUpdatedAt:dd.MM.yyyy}";
        var isCommunity = answer.Message.StartsWith(CommunityPrefix, StringComparison.OrdinalIgnoreCase);
        return new(answer.Title, answer.Message, status, source, updated, isCommunity, tone,
            answer.ProblemSolution?.Summary,
            answer.ProblemSolution?.Steps,
            answer.ProblemSolution?.PossibleCauses,
            answer.ProblemSolution?.FollowUpSuggestions,
            answer.ProviderId is null ? null : $"{answer.ProviderId}{(answer.ModelId is null ? "" : $" · {answer.ModelId}")}",
            Activity: OverlayActivity.Answering);
    }

    public static OverlayPresentation Create(MicroModelStatus status)
    {
        var tone = status.State switch
        {
            MicroModelState.Faulted or MicroModelState.MemoryLimitExceeded => OverlayTone.Warning,
            MicroModelState.Ready or MicroModelState.Idle => OverlayTone.Success,
            _ => OverlayTone.Neutral,
        };
        var label = status.State switch
        {
            MicroModelState.Starting => "Запуск",
            MicroModelState.Generating => "Формирование ответа",
            MicroModelState.Ready => "Готово",
            MicroModelState.Idle => "Ожидание TTL",
            MicroModelState.MemoryLimitExceeded => "Лимит памяти",
            MicroModelState.Faulted => "Ошибка",
            _ => status.State.ToString(),
        };
        var activity = status.State is MicroModelState.Starting or MicroModelState.Generating
            ? OverlayActivity.Thinking
            : OverlayActivity.None;
        return new(
            "Локальная MicroModel",
            status.Message,
            label,
            "Отдельный локальный процесс · mock runtime",
            $"Состояние обновлено {status.UpdatedAt:HH:mm:ss}",
            false,
            tone,
            Activity: activity);
    }

    public static OverlayPresentation CreateListening() => new(
        "Голосовой вопрос",
        "Говорите — микрофон слушает в течение 20 секунд.",
        "Слушаю",
        "Локальный микрофон",
        "Временный контекст не сохраняется",
        false,
        OverlayTone.Neutral,
        Activity: OverlayActivity.Listening);
}
