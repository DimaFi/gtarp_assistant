using System.Text;

namespace GtaRpAssistant.Core;

public enum ScreenObservationMode { Off, EventTriggered, LowFrequency }
public enum KnownScreenKind { Unknown, Dialog, Shop, Inventory, Quest, Notification }

public readonly record struct ScreenRegion(double X, double Y, double Width, double Height)
{
    public static ScreenRegion Full => new(0, 0, 1, 1);
}

public sealed record ScreenFrame(int Width, int Height, byte[] GrayscalePixels, DateTimeOffset CapturedAt);
public sealed record ScreenFrameDiff(double ChangedRatio, IReadOnlyList<ScreenRegion> ChangedRegions)
{
    public bool HasMeaningfulChange(double threshold = .015) => ChangedRatio >= threshold;
}

public interface IScreenFrameDiffer { ScreenFrameDiff Compare(ScreenFrame previous, ScreenFrame current); }
public sealed record ScreenOcrResult(IReadOnlyList<ScreenTextField> Fields);
public interface ILocalScreenOcr
{
    bool IsAvailable { get; }
    Task<ScreenOcrResult> RecognizeAsync(ReadOnlyMemory<byte> pngImage, CancellationToken cancellationToken);
}

public static class KnownScreenRecognizer
{
    public static (KnownScreenKind Kind, double Confidence) Recognize(IEnumerable<ScreenTextField> fields)
    {
        var text = string.Join(' ', fields.Select(x => x.Text)).ToLowerInvariant();
        if (ContainsAny(text, "купить", "продать", "цена", "магазин")) return (KnownScreenKind.Shop, .86);
        if (ContainsAny(text, "инвентарь", "использовать", "выбросить")) return (KnownScreenKind.Inventory, .84);
        if (ContainsAny(text, "задание", "квест", "награда", "цель")) return (KnownScreenKind.Quest, .82);
        if (ContainsAny(text, "принять", "отклонить", "далее", "ответить")) return (KnownScreenKind.Dialog, .76);
        if (fields.Any()) return (KnownScreenKind.Unknown, .35);
        return (KnownScreenKind.Unknown, 0);
    }
    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);
}

public static class ScreenFieldProfiler
{
    public static IReadOnlyList<ScreenTextField> Apply(KnownScreenKind kind, IEnumerable<ScreenTextField> fields) =>
        fields.Select(field => field with { Role = Role(kind, field) }).ToArray();

    private static string Role(KnownScreenKind kind, ScreenTextField field)
    {
        var text = field.Text.ToLowerInvariant();
        if (field.Bounds.Y < .22) return "title";
        if (text.Contains('$') || text.Contains("₽") || text.Contains("цена") || text.Contains("стоимость")) return "price";
        return kind switch
        {
            KnownScreenKind.Shop => field.Bounds.X > .55 ? "details" : "item",
            KnownScreenKind.Inventory => "item",
            KnownScreenKind.Quest => field.Bounds.Y > .72 ? "action" : "objective",
            KnownScreenKind.Dialog => field.Bounds.Y > .72 ? "button" : "dialogue",
            KnownScreenKind.Notification => "notification",
            _ => "text",
        };
    }
}

public static class ScreenContextAnswerFactory
{
    public static AssistantAnswer Create(ScreenContextSnapshot snapshot)
    {
        var fields = snapshot.TextFields
            .Where(x => x.Confidence >= .35 && !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => (x.Role, Text: Clean(x.Text)))
            .Where(x => x.Text.Length > 1)
            .DistinctBy(x => $"{x.Role}\0{x.Text}", StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        if (fields.Length == 0)
            return new(AnswerDecision.AskForMoreInformation, "Экран не распознан", "Свежий кадр есть, но локальный OCR не смог уверенно прочитать текст.", [], "Временное локальное наблюдение", snapshot.CapturedAt, false, "Local screen context empty", ProviderId: "local-screen-context");

        var screenName = snapshot.ScreenKind switch
        {
            KnownScreenKind.Shop => "магазин",
            KnownScreenKind.Inventory => "инвентарь",
            KnownScreenKind.Quest => "задание",
            KnownScreenKind.Dialog => "диалог",
            KnownScreenKind.Notification => "уведомление",
            _ => "неизвестное окно",
        };
        var message = $"Похоже, открыт {screenName}.\n" + string.Join("\n", fields.Select(x => $"{Label(x.Role)}: {x.Text}"));
        if (message.Length > 900) message = message[..897].TrimEnd() + "…";
        return new(AnswerDecision.Show, "Локальное распознавание экрана", message, [], "Временное локальное наблюдение — не источник игровых правил", snapshot.CapturedAt, false, "Local screen context", ProviderId: "local-screen-context");
    }

    private static string Clean(string value)
    {
        var safe = new string(value.Where(character => !char.IsControl(character) && character is not '\u202A' and not '\u202B' and not '\u202D' and not '\u202E' and not '\u2066' and not '\u2067' and not '\u2068' and not '\u2069').ToArray());
        safe = safe.Replace("http://", "[ссылка скрыта]", StringComparison.OrdinalIgnoreCase).Replace("https://", "[ссылка скрыта]", StringComparison.OrdinalIgnoreCase);
        safe = string.Join(' ', safe.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        return safe.Length <= 160 ? safe : safe[..157].TrimEnd() + "…";
    }
    private static string Label(string role) => role switch
    {
        "title" => "Заголовок", "price" => "Цена", "details" => "Описание", "item" => "Элемент",
        "objective" => "Цель", "action" => "Действие", "button" => "Кнопка", "dialogue" => "Текст",
        "notification" => "Уведомление", _ => "Текст",
    };
}

public sealed class GridScreenFrameDiffer(int columns = 8, int rows = 6, byte pixelThreshold = 24) : IScreenFrameDiffer
{
    public ScreenFrameDiff Compare(ScreenFrame previous, ScreenFrame current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height) return new(1, [ScreenRegion.Full]);
        var cellChanged = new bool[columns * rows];
        var changed = 0;
        var total = current.GrayscalePixels.Length;
        for (var i = 0; i < total; i++)
        {
            if (Math.Abs(current.GrayscalePixels[i] - previous.GrayscalePixels[i]) < pixelThreshold) continue;
            changed++;
            var x = i % current.Width;
            var y = i / current.Width;
            var cellX = Math.Min(columns - 1, x * columns / current.Width);
            var cellY = Math.Min(rows - 1, y * rows / current.Height);
            cellChanged[cellY * columns + cellX] = true;
        }
        var regions = new List<ScreenRegion>();
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
                if (cellChanged[y * columns + x]) regions.Add(new((double)x / columns, (double)y / rows, 1d / columns, 1d / rows));
        return new(total == 0 ? 0 : (double)changed / total, regions);
    }
}

public sealed record ScreenTextField(string Role, string Text, double Confidence, ScreenRegion Bounds);
public sealed record ScreenNumberField(string Role, decimal Value, string RawText, double Confidence, ScreenRegion Bounds);
public sealed record ScreenContextSnapshot(
    DateTimeOffset CapturedAt,
    KnownScreenKind ScreenKind,
    double Confidence,
    IReadOnlyList<ScreenRegion> ChangedRegions,
    IReadOnlyList<ScreenTextField> TextFields,
    IReadOnlyList<ScreenNumberField> NumericFields,
    DateTimeOffset ExpiresAt)
{
    public bool IsFresh(DateTimeOffset now) => now < ExpiresAt;

    public string ToDisplayText(int maxCharacters = 900)
    {
        var builder = new StringBuilder($"Screen={ScreenKind}; confidence={Confidence:0.00}");
        foreach (var field in TextFields.Where(x => !string.IsNullOrWhiteSpace(x.Text)).Take(12))
            builder.Append($"\n{field.Role}: {field.Text.Trim()}");
        foreach (var field in NumericFields.Take(8)) builder.Append($"\n{field.Role}: {field.RawText}");
        return builder.Length <= maxCharacters ? builder.ToString() : builder.ToString(0, maxCharacters);
    }
}

public interface IScreenContextStore
{
    ScreenContextSnapshot? GetFresh(DateTimeOffset now);
    void Publish(ScreenContextSnapshot snapshot);
    void Clear();
}

public sealed class ScreenContextStore : IScreenContextStore
{
    private ScreenContextSnapshot? _current;
    public ScreenContextSnapshot? GetFresh(DateTimeOffset now)
    {
        var snapshot = Volatile.Read(ref _current);
        if (snapshot is null || snapshot.IsFresh(now)) return snapshot;
        Interlocked.CompareExchange(ref _current, null, snapshot);
        return null;
    }
    public void Publish(ScreenContextSnapshot snapshot) => Volatile.Write(ref _current, snapshot);
    public void Clear() => Volatile.Write(ref _current, null);
}

public static class ScreenQuestionClassifier
{
    private static readonly string[] Markers = ["на экране", "написано", "в меню", "в окне", "цена", "баланс", "диалог", "уведомление", "кнопк"];
    public static bool NeedsScreenContext(string question) => Markers.Any(marker => question.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
