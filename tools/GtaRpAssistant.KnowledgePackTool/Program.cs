using System.Text.Json;
using GtaRpAssistant.Knowledge;

var parsed = Parse(args);
if (parsed is null)
{
    PrintUsage();
    return 2;
}

try
{
    var pack = await new KnowledgePackLoader().LoadPackAsync(parsed.Value.Directory, CancellationToken.None);
    if (parsed.Value.Command == "check-sources")
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GtaRpAssistant-KnowledgeAudit/0.2 (+local release validation)");
        var checks = await KnowledgeSourceChecker.CheckAsync(pack.Articles, client, CancellationToken.None);
        if (parsed.Value.Json)
            Console.WriteLine(JsonSerializer.Serialize(checks, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        else
            foreach (var check in checks) Console.WriteLine($"{(check.Available ? "ok" : "error")} [{check.ArticleId}] {check.StatusCode?.ToString() ?? "-"} {check.Url} — {check.Message}");
        return checks.All(x => x.Available) ? 0 : 1;
    }
    if (parsed.Value.Command == "inspect")
    {
        var inspection = KnowledgePackInspection.Create(pack);
        if (parsed.Value.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(inspection, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Pack: {inspection.Id} ({inspection.Project}) v{inspection.Version}");
            Console.WriteLine($"Created: {inspection.CreatedAt:O}");
            Console.WriteLine($"Articles: {inspection.Articles}; facts: {inspection.Facts}; prepared answers: {inspection.PreparedAnswers}");
            Console.WriteLine($"Verified: {inspection.VerifiedArticles}; demo: {inspection.DemoArticles}; expired: {inspection.ExpiredArticles}");
            Console.WriteLine($"Servers: {string.Join(", ", inspection.Servers)}");
            Console.WriteLine($"Source hosts: {(inspection.SourceHosts.Count == 0 ? "none" : string.Join(", ", inspection.SourceHosts))}");
        }
        return 0;
    }

    if (parsed.Value.Command == "lint" || parsed.Value.Strict)
    {
        var report = KnowledgeGovernanceValidator.Inspect(pack.Articles);
        foreach (var issue in report.Issues)
            Console.WriteLine($"{issue.Severity.ToString().ToLowerInvariant()} {issue.Code} [{issue.ArticleId}]: {issue.Message}");
        Console.WriteLine($"Governance lint complete. Errors: {report.Issues.Count(x => x.Severity == KnowledgeIssueSeverity.Error)}, warnings: {report.Issues.Count(x => x.Severity == KnowledgeIssueSeverity.Warning)}");
        if (report.HasErrors || parsed.Value.Strict && report.HasWarnings) return 1;
    }

    Console.WriteLine($"Knowledge pack valid. Articles: {pack.Articles.Count}, facts: {pack.Articles.Sum(x => x.Facts.Count)}");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
{
    Console.Error.WriteLine($"Invalid knowledge pack: {ex.Message}");
    return 1;
}

static (string Command, string Directory, bool Strict, bool Json)? Parse(string[] arguments)
{
    if (arguments.Length == 1 && !arguments[0].StartsWith('-')) return ("validate", arguments[0], false, false);
    if (arguments.Length < 2) return null;
    var command = arguments[0].ToLowerInvariant();
    if (command is not ("validate" or "lint" or "inspect" or "check-sources")) return null;
    var options = arguments.Skip(2).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (options.Any(x => x is not ("--strict" or "--json"))) return null;
    if (command is "inspect" or "check-sources" && options.Contains("--strict")) return null;
    if (command is not ("inspect" or "check-sources") && options.Contains("--json")) return null;
    return (command, arguments[1], options.Contains("--strict"), options.Contains("--json"));
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  GtaRpAssistant.KnowledgePackTool <pack-directory>");
    Console.Error.WriteLine("  GtaRpAssistant.KnowledgePackTool validate <pack-directory> [--strict]");
    Console.Error.WriteLine("  GtaRpAssistant.KnowledgePackTool lint <pack-directory> [--strict]");
    Console.Error.WriteLine("  GtaRpAssistant.KnowledgePackTool inspect <pack-directory> [--json]");
    Console.Error.WriteLine("  GtaRpAssistant.KnowledgePackTool check-sources <pack-directory> [--json]");
}
