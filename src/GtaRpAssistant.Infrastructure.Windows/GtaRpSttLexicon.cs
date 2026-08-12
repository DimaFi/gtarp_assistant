using System.Text.RegularExpressions;

namespace GtaRpAssistant.Infrastructure.Windows;

public static partial class GtaRpSttLexicon
{
    public static string NormalizeTranscript(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var normalized = NormalizeCurrencies(value);
        normalized = Merryweather().Replace(normalized, "Мерривезер");
        normalized = Airdrop().Replace(normalized, "аирдроп");
        normalized = Rednecks().Replace(normalized, "Реднекс");
        normalized = CarMeet().Replace(normalized, "Кар Мит");
        return Epsilon().Replace(normalized, "Эпсилон");
    }

    public static string NormalizeCurrencies(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var normalized = BonusPoints().Replace(value, "BP");
        return DonatePoints().Replace(normalized, "DP");
    }

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}])(?:b\s*\.?\s*p|б\s*\.?\s*п(?:[иэ]ш(?:ки|ек|ка|ку|ками)?)?|би\s*пи|бонус(?:ные|ная|ную|ной|ных)?[\s-]+(?:п(?:о|а)ин(?:т|т?ы|тов|та|там|тами|иты)|пониты))(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex BonusPoints();

    [GeneratedRegex(@"(?ix)(?<![\p{L}\p{N}])(?:d\s*\.?\s*p|д\s*\.?\s*п(?:[иэ]ш(?:ки|ек|ка|ку|ками)?)?|ди\s*пи|донат(?:ные|ная|ную|ной|ных)?[\s-]+(?:поинт(?:ы|ов|а|ам|ами)?|валют(?:а|ы|у|ой)))(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex DonatePoints();

    [GeneratedRegex(@"(?ix)(?<!\p{L})(?:м[еэа]р(?:р)?[иэ]?[увв]?[еэ]з?[еэ]р)(?!\p{L})", RegexOptions.CultureInvariant)]
    private static partial Regex Merryweather();

    [GeneratedRegex(@"(?ix)(?<!\p{L})а[эи]ро?дроп", RegexOptions.CultureInvariant)]
    private static partial Regex Airdrop();

    [GeneratedRegex(@"(?ix)(?<!\p{L})р[ое]днекс(?=\p{L}*\b)", RegexOptions.CultureInvariant)]
    private static partial Regex Rednecks();

    [GeneratedRegex(@"(?ix)(?<!\p{L})кар\s*мит(?=\p{L}*\b)", RegexOptions.CultureInvariant)]
    private static partial Regex CarMeet();

    [GeneratedRegex(@"(?ix)(?<!\p{L})[иэ]пс(?:и)?[ои]н(?=\p{L}*\b)", RegexOptions.CultureInvariant)]
    private static partial Regex Epsilon();
}
