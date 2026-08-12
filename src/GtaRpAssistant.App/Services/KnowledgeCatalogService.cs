using System.IO;
using GtaRpAssistant.Knowledge;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App.Services;

public sealed record KnowledgeCatalogSummary(int OfficialArticles, int CommunityArticles)
{
    public int TotalArticles => OfficialArticles + CommunityArticles;
}

public sealed record KnowledgeDocumentItem(
    string Id, string Title, string Source, string SourceUrl, string Trust,
    string Server, DateTimeOffset UpdatedAt, int FactCount, string Preview, bool IsEnabled = true)
{
    public string UpdatedLabel => UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy");
    public string FactLabel => $"{FactCount} фактов / chunks";
}

public sealed class KnowledgeCatalogService(SqliteKnowledgeRepository repository, ILogger<KnowledgeCatalogService> logger)
{
    private IReadOnlyList<KnowledgePackArticle> _articles = [];
    public IReadOnlyList<KnowledgeDocumentItem> Documents => _articles.Select(ToDocument).ToArray();
    public event EventHandler? CatalogChanged;

    public async Task<KnowledgeCatalogSummary> InitializeAsync(CancellationToken cancellationToken)
    {
        var official = await new KnowledgePackLoader().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp"), cancellationToken);
        var community = await new CommunityReferenceLoader().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community"), cancellationToken);
        _articles = official.Concat(community).ToArray();
        await repository.InitializeAsync(_articles, cancellationToken);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
        logger.LogInformation("Knowledge catalog initialized; total={Total}; official={Official}; community={Community}",
            official.Count + community.Count, official.Count, community.Count);
        return new(official.Count, community.Count);
    }

    public async Task ReindexAsync(CancellationToken cancellationToken)
    {
        await repository.InitializeAsync(_articles, cancellationToken);
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private static KnowledgeDocumentItem ToDocument(KnowledgePackArticle article) => new(
        article.Id, article.Title, article.Source.Title, article.Source.Url ?? "",
        article.Id.StartsWith("community.", StringComparison.OrdinalIgnoreCase) ? "community" : "official",
        string.Join(", ", article.ServerScope), article.UpdatedAt, article.Facts.Count,
        string.Join(Environment.NewLine, article.Facts.Select(x => "• " + x.Text)));
}
