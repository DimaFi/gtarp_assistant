using System.IO;
using GtaRpAssistant.Knowledge;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App.Services;

public sealed record KnowledgeCatalogSummary(int OfficialArticles, int CommunityArticles)
{
    public int TotalArticles => OfficialArticles + CommunityArticles;
}

public sealed class KnowledgeCatalogService(SqliteKnowledgeRepository repository, ILogger<KnowledgeCatalogService> logger)
{
    public async Task<KnowledgeCatalogSummary> InitializeAsync(CancellationToken cancellationToken)
    {
        var official = await new KnowledgePackLoader().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "knowledge", "packs", "gta5rp"), cancellationToken);
        var community = await new CommunityReferenceLoader().LoadAsync(
            Path.Combine(AppContext.BaseDirectory, "knowledge", "reference", "community"), cancellationToken);
        await repository.InitializeAsync(official.Concat(community).ToArray(), cancellationToken);
        logger.LogInformation("Knowledge catalog initialized; total={Total}; official={Official}; community={Community}",
            official.Count + community.Count, official.Count, community.Count);
        return new(official.Count, community.Count);
    }
}
