using System.IO;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GtaRpAssistant.App.Tests;

public sealed class KnowledgeCatalogServiceTests
{
    [Fact]
    public async Task ImportToggleAndRollbackPersistAndRebuildIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "gta-rp-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = new SqliteKnowledgeRepository($"Data Source={Path.Combine(root, "knowledge.db")}");
            var service = new KnowledgeCatalogService(repository, NullLogger<KnowledgeCatalogService>.Instance, root);
            await service.InitializeAsync(CancellationToken.None);
            var csv = Path.Combine(root, "import.csv");
            await File.WriteAllTextAsync(csv, "id,title,fact,source,server\ncommunity.import.test,Тест,Проверенный факт,Пользователь,all");

            var preview = await service.PreviewImportAsync(csv, CancellationToken.None);
            Assert.Single(preview.Articles);
            await service.ImportAsync(preview, CancellationToken.None);
            Assert.Contains(service.Documents, x => x.Id == "community.import.test" && x.IsEnabled);

            await service.ToggleAsync("community.import.test", CancellationToken.None);
            Assert.Contains(service.Documents, x => x.Id == "community.import.test" && !x.IsEnabled);
            Assert.Empty(await repository.SearchAsync(new("Проверенный факт", "all", 5), CancellationToken.None));

            Assert.True(await service.RollbackAsync(CancellationToken.None));
            Assert.Contains(service.Documents, x => x.Id == "community.import.test" && x.IsEnabled);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
