using System.Text.Json;
using System.Text.RegularExpressions;

namespace GtaRpAssistant.Knowledge;

public sealed partial class EclipseLegalReferenceLoader
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidUntil = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    public async Task<IReadOnlyList<KnowledgePackArticle>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var result = new List<KnowledgePackArticle>();
        foreach (var source in document.RootElement.GetProperty("rules").EnumerateArray())
            AddSource(source, result);
        return result;
    }

    private static void AddSource(JsonElement source, List<KnowledgePackArticle> output)
    {
        var sourceId = Text(source, "id");
        var sourceTitle = Text(source, "title");
        var sourceUrl = Text(source, "url");
        var codeName = CodeName(sourceId, sourceTitle);
        var codeAbbreviation = CodeAbbreviation(sourceId);
        var text = Text(source, "text").Replace("\r\n", "\n", StringComparison.Ordinal);
        var matches = ArticleHeadingRegex().Matches(text);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var number = match.Groups[1].Value;
            if (!seen.Add(number)) continue;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var body = NormalizeWhitespace(text[match.Index..end]);
            if (body.Length < 12) continue;
            var answer = BuildPreparedAnswer(codeAbbreviation, number, body);
            var idNumber = number.Replace('.', '-');
            var articleId = $"official.eclipse.legal.{sourceId}.{idNumber}";
            var aliases = new[]
            {
                $"статья {number} {codeName}", $"статья {number} {codeAbbreviation}", $"{codeAbbreviation} {number}",
                $"статья {number} эклипс", $"что означает статья {number} {codeAbbreviation}"
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var questions = new[]
            {
                new PreparedAnswer($"что такое {codeAbbreviation} {number}", answer),
                new PreparedAnswer($"что означает статья {number} {codeAbbreviation}", answer),
                new PreparedAnswer($"что означает статья {number} на эклипсе", answer),
            };
            output.Add(new(articleId, "gta5rp", ["all", "Eclipse"], $"{codeAbbreviation} {number} — Eclipse", "official", "legal-article", aliases,
                $"Официальная статья {number} из документа «{sourceTitle}».",
                [new($"{articleId}.fact.1", body, true)], questions, new(sourceTitle, sourceUrl), 1, UpdatedAt, true, false, ValidUntil, "Official Eclipse forum source"));
        }
    }

    private static string BuildPreparedAnswer(string abbreviation, string number, string body)
    {
        var penalty = PenaltyRegex().Match(body).Value.Trim();
        var definition = PenaltyRegex().Replace(body, "").Trim();
        var prefix = $"{abbreviation} {number}: ";
        var suffix = string.IsNullOrWhiteSpace(penalty) ? "" : $" {penalty}";
        var available = Math.Max(40, 340 - prefix.Length - suffix.Length);
        if (definition.Length > available)
        {
            var cut = definition.LastIndexOf(' ', available);
            definition = definition[..(cut > 40 ? cut : available)].TrimEnd(',', ';', ':') + "…";
        }
        return (prefix + definition + suffix).Trim();
    }

    private static string NormalizeWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Replace('\u200b', ' ').Replace('\u00a0', ' '), " ").Trim();

    private static string CodeAbbreviation(string id) => id switch
    {
        "criminal" => "УК", "procedural" => "ПК", "admin" => "АК", "road" => "ДК", "labor" => "ТК", "judicial" => "СК",
        "constitution" => "Конституция", "ethics" => "ЭК", "air" => "ВК", _ => "Закон"
    };

    private static string CodeName(string id, string title) => id switch
    {
        "criminal" => "уголовного кодекса", "procedural" => "процессуального кодекса", "admin" => "административного кодекса",
        "road" => "дорожного кодекса", "labor" => "трудового кодекса", "judicial" => "судебного кодекса", _ => title
    };

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? "";

    [GeneratedRegex(@"(?im)^\s*Статья\s+([0-9]+(?:\.[0-9]+){0,2})(?=\.(?!\d)|[^0-9.]|$)[^\n]*")]
    private static partial Regex ArticleHeadingRegex();
    [GeneratedRegex(@"(?:^|\s)-\s*(?:от\s+)?\d[^.\n]*(?:\(\([^)]*\)\))?", RegexOptions.IgnoreCase)]
    private static partial Regex PenaltyRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
