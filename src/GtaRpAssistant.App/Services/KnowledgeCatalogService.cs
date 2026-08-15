using System.Globalization;
using System.IO;
using System.Text.Json;
using GtaRpAssistant.Knowledge;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App.Services;

public sealed record KnowledgeCatalogSummary(int OfficialArticles, int CommunityArticles)
{
    public int TotalArticles => OfficialArticles + CommunityArticles;
}

public sealed record KnowledgeDocumentItem(string Id, string Title, string Source, string SourceUrl, string Trust,
    string Server, DateTimeOffset UpdatedAt, int FactCount, string Preview, bool IsEnabled = true)
{
    public string UpdatedLabel => UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy");
    public string FactLabel => $"{FactCount} фактов / chunks";
}

public sealed record KnowledgeImportPreview(string Path, IReadOnlyList<KnowledgePackArticle> Articles)
{
    public string Description => string.Join(Environment.NewLine, Articles.Take(5).Select(x =>
        $"• {x.Title} · {x.Source.Title} · {x.Facts.Count} фактов")) +
        (Articles.Count > 5 ? $"{Environment.NewLine}…и ещё {Articles.Count - 5}" : "");
}

public sealed class KnowledgeCatalogService
{
    private sealed record CatalogState(IReadOnlyList<KnowledgePackArticle> Imported, IReadOnlyList<string> Disabled);
    private readonly SqliteKnowledgeRepository _repository;
    private readonly ILogger<KnowledgeCatalogService> _logger;
    private readonly string _statePath;
    private readonly string _historyDirectory;
    private IReadOnlyList<KnowledgePackArticle> _builtIn = [];
    private List<KnowledgePackArticle> _imported = [];
    private HashSet<string> _disabled = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public KnowledgeCatalogService(SqliteKnowledgeRepository repository, ILogger<KnowledgeCatalogService> logger, string dataDirectory)
    {
        _repository = repository;
        _logger = logger;
        _statePath = Path.Combine(dataDirectory, "knowledge-catalog.json");
        _historyDirectory = Path.Combine(dataDirectory, "knowledge-history");
    }

    private IReadOnlyList<KnowledgePackArticle> AllArticles => _builtIn.Concat(_imported).GroupBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Last()).ToArray();
    public IReadOnlyList<KnowledgeDocumentItem> Documents => AllArticles.Select(x => ToDocument(x, !_disabled.Contains(x.Id))).ToArray();
    public bool CanRollback => Directory.Exists(_historyDirectory) && Directory.EnumerateFiles(_historyDirectory, "*.json").Any();
    public event EventHandler? CatalogChanged;

    public async Task<KnowledgeCatalogSummary> InitializeAsync(CancellationToken cancellationToken)
    {
        var official = await new KnowledgePackLoader().LoadAsync(Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp"), cancellationToken);
        var community = await new CommunityReferenceLoader().LoadAsync(Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community"), cancellationToken);
        _builtIn = official.Concat(community).ToArray();
        await LoadStateAsync(cancellationToken);
        await ReindexAsync(cancellationToken);
        _logger.LogInformation("Knowledge catalog initialized; total={Total}; imported={Imported}; disabled={Disabled}", AllArticles.Count, _imported.Count, _disabled.Count);
        return new(AllArticles.Count(x => !x.Id.StartsWith("community.", StringComparison.OrdinalIgnoreCase)), AllArticles.Count(x => x.Id.StartsWith("community.", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task ReindexAsync(CancellationToken cancellationToken)
    {
        await _repository.RebuildAsync(AllArticles.Where(x => !_disabled.Contains(x.Id)), cancellationToken);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleAsync(string articleId, CancellationToken cancellationToken)
    {
        await SaveSnapshotAsync(cancellationToken);
        if (!_disabled.Add(articleId)) _disabled.Remove(articleId);
        await SaveStateAsync(cancellationToken);
        await ReindexAsync(cancellationToken);
    }

    public async Task<KnowledgeImportPreview> PreviewImportAsync(string path, CancellationToken cancellationToken)
    {
        var articles = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? await LoadCsvAsync(path, cancellationToken)
            : await LoadJsonAsync(path, cancellationToken);
        ValidateImported(articles);
        return new(path, articles);
    }

    public async Task ImportAsync(KnowledgeImportPreview preview, CancellationToken cancellationToken)
    {
        await SaveSnapshotAsync(cancellationToken);
        foreach (var article in preview.Articles)
        {
            _imported.RemoveAll(x => string.Equals(x.Id, article.Id, StringComparison.Ordinal));
            _imported.Add(article);
            _disabled.Remove(article.Id);
        }
        await SaveStateAsync(cancellationToken);
        await ReindexAsync(cancellationToken);
    }

    public async Task<bool> RollbackAsync(CancellationToken cancellationToken)
    {
        if (!CanRollback) return false;
        var latest = Directory.EnumerateFiles(_historyDirectory, "*.json").OrderByDescending(x => x, StringComparer.Ordinal).First();
        var state = await ReadStateAsync(latest, cancellationToken);
        _imported = state.Imported.ToList();
        _disabled = state.Disabled.ToHashSet(StringComparer.Ordinal);
        File.Delete(latest);
        await SaveStateAsync(cancellationToken);
        await ReindexAsync(cancellationToken);
        return true;
    }

    private async Task LoadStateAsync(CancellationToken ct)
    {
        if (!File.Exists(_statePath)) return;
        var state = await ReadStateAsync(_statePath, ct);
        _imported = state.Imported.ToList();
        _disabled = state.Disabled.ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<CatalogState> ReadStateAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CatalogState>(stream, JsonOptions, ct) ?? new([], []);
    }

    private async Task SaveSnapshotAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_historyDirectory);
        var path = Path.Combine(_historyDirectory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfffffff}.json");
        await WriteStateAsync(path, ct);
        foreach (var old in Directory.EnumerateFiles(_historyDirectory, "*.json").OrderByDescending(x => x).Skip(20)) File.Delete(old);
    }

    private Task SaveStateAsync(CancellationToken ct) => WriteStateAsync(_statePath, ct);
    private async Task WriteStateAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, new CatalogState(_imported, _disabled.ToArray()), JsonOptions, ct);
        File.Move(temp, path, true);
    }

    private static async Task<IReadOnlyList<KnowledgePackArticle>> LoadJsonAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("articles", out var nested)) root = nested;
        var articles = root.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<KnowledgePackArticle>>(root.GetRawText(), JsonOptions)
            : [JsonSerializer.Deserialize<KnowledgePackArticle>(root.GetRawText(), JsonOptions)!];
        return articles?.Where(x => x is not null).ToArray() ?? [];
    }

    private static async Task<IReadOnlyList<KnowledgePackArticle>> LoadCsvAsync(string path, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(path, ct);
        if (lines.Length < 2) throw new InvalidDataException("CSV должен содержать заголовок и хотя бы одну строку.");
        var headers = lines[0].Split(',').Select((x, i) => (x.Trim().Trim('"').ToLowerInvariant(), i)).ToDictionary(x => x.Item1, x => x.i);
        string Cell(string[] row, string key, string fallback = "") => headers.TryGetValue(key, out var i) && i < row.Length ? row[i].Trim().Trim('"') : fallback;
        var result = new List<KnowledgePackArticle>();
        for (var i = 1; i < lines.Length; i++)
        {
            var row = lines[i].Split(','); var title = Cell(row, "title"); var fact = Cell(row, "fact");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(fact)) continue;
            var id = Cell(row, "id", $"community.import.{Path.GetFileNameWithoutExtension(path)}.{i}");
            result.Add(new(id, "gta5rp", [Cell(row, "server", "all")], title, "community", "import", [title], fact,
                [new(id + ".fact.1", fact, true)], [], new(Cell(row, "source", Path.GetFileName(path)), null), 1, DateTimeOffset.UtcNow, true, false, null, "User import"));
        }
        return result;
    }

    private static void ValidateImported(IReadOnlyList<KnowledgePackArticle> articles)
    {
        if (articles.Count == 0) throw new InvalidDataException("В файле не найдено документов.");
        var manifest = new KnowledgePackManifest("user-import", "gta5rp", 1, DateTimeOffset.UtcNow, articles.Select((_, i) => i.ToString(CultureInfo.InvariantCulture)).ToArray());
        KnowledgePackValidator.Validate(manifest, articles);
    }

    private static KnowledgeDocumentItem ToDocument(KnowledgePackArticle article, bool enabled) => new(article.Id, article.Title, article.Source.Title,
        article.Source.Url ?? "", article.Id.StartsWith("community.", StringComparison.OrdinalIgnoreCase) ? "community" : "official",
        string.Join(", ", article.ServerScope), article.UpdatedAt, article.Facts.Count,
        string.Join(Environment.NewLine, article.Facts.Select(x => "• " + x.Text)), enabled);
}
