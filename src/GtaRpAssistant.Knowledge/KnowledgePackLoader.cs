using System.Text.Json;

namespace GtaRpAssistant.Knowledge;

public sealed record LoadedKnowledgePack(KnowledgePackManifest Manifest, IReadOnlyList<KnowledgePackArticle> Articles);

public sealed class KnowledgePackLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<KnowledgePackArticle>> LoadAsync(string packDirectory, CancellationToken cancellationToken) =>
        (await LoadPackAsync(packDirectory, cancellationToken)).Articles;

    public async Task<LoadedKnowledgePack> LoadPackAsync(string packDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packDirectory);
        var root = Path.GetFullPath(packDirectory);
        var manifestPath = Path.Combine(root, "manifest.json");
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<KnowledgePackManifest>(manifestStream, Options, cancellationToken)
            ?? throw new InvalidDataException("manifest.json is empty.");

        var articles = new List<KnowledgePackArticle>(manifest.ArticleFiles.Count);
        foreach (var relative in manifest.ArticleFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(relative)) throw new InvalidDataException("Manifest contains an empty article path.");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var relativeToRoot = Path.GetRelativePath(root, full);
            if (relativeToRoot == ".." || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidDataException($"Article path leaves the pack directory: {relative}");

            await using var stream = File.OpenRead(full);
            articles.Add(await JsonSerializer.DeserializeAsync<KnowledgePackArticle>(stream, Options, cancellationToken)
                ?? throw new InvalidDataException($"Article is empty: {relative}"));
        }

        KnowledgePackValidator.Validate(manifest, articles);
        return new(manifest, articles);
    }
}

public static class KnowledgePackValidator
{
    public static void Validate(KnowledgePackManifest manifest, IReadOnlyList<KnowledgePackArticle> articles)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Project) || manifest.Version < 1)
            throw new InvalidDataException("Manifest id, project and positive version are required.");
        if (manifest.ArticleFiles.Count != articles.Count)
            throw new InvalidDataException("Manifest article count does not match loaded article count.");
        if (manifest.ArticleFiles.Any(string.IsNullOrWhiteSpace) || manifest.ArticleFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.ArticleFiles.Count)
            throw new InvalidDataException("Manifest article paths must be non-empty and unique.");

        var articleIds = new HashSet<string>(StringComparer.Ordinal);
        var factIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var article in articles)
        {
            if (string.IsNullOrWhiteSpace(article.Id) || !articleIds.Add(article.Id))
                throw new InvalidDataException($"Article ID is empty or duplicated: {article.Id}");
            if (!string.Equals(article.Project, manifest.Project, StringComparison.Ordinal))
                throw new InvalidDataException($"Article project differs from manifest: {article.Id}");
            if (article.Version < 1 || string.IsNullOrWhiteSpace(article.Title) || string.IsNullOrWhiteSpace(article.Summary))
                throw new InvalidDataException($"Article title, summary and positive version are required: {article.Id}");
            if (article.ServerScope.Count == 0 || article.ServerScope.Any(string.IsNullOrWhiteSpace) || article.Aliases.Count == 0 || article.Aliases.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Article requires non-empty server scope and aliases: {article.Id}");
            if (article.Facts.Count == 0 || article.Facts.Any(f => string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.Text) || !factIds.Add(f.Id)))
                throw new InvalidDataException($"Article facts must be non-empty and globally unique: {article.Id}");
            if (article.PreparedAnswers.Any(x => string.IsNullOrWhiteSpace(x.QuestionPattern) || string.IsNullOrWhiteSpace(x.Answer)))
                throw new InvalidDataException($"Prepared answers must contain a question and answer: {article.Id}");
            if (string.IsNullOrWhiteSpace(article.Source.Title))
                throw new InvalidDataException($"Article source title is required: {article.Id}");
            if (article.Verified && article.Facts.Any(f => !f.Verified))
                throw new InvalidDataException($"Verified article contains an unverified fact: {article.Id}");
        }
    }
}
