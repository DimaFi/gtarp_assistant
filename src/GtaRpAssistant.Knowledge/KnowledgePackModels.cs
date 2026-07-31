using System.Text.Json.Serialization;

namespace GtaRpAssistant.Knowledge;

public sealed record KnowledgePackManifest(string Id, string Project, int Version, DateTimeOffset CreatedAt, IReadOnlyList<string> ArticleFiles);
public sealed record ArticleFact(string Id, string Text, bool Verified);
public sealed record PreparedAnswer(string QuestionPattern, string Answer);
public sealed record ArticleSource(string Title, string? Url);
public sealed record KnowledgePackArticle(
    string Id,
    string Project,
    IReadOnlyList<string> ServerScope,
    string Title,
    string Category,
    string Mechanic,
    IReadOnlyList<string> Aliases,
    string Summary,
    IReadOnlyList<ArticleFact> Facts,
    IReadOnlyList<PreparedAnswer> PreparedAnswers,
    ArticleSource Source,
    int Version,
    DateTimeOffset UpdatedAt,
    bool Verified,
    bool Demo,
    DateTimeOffset? ValidUntil = null,
    string? VerifiedBy = null);

[JsonSerializable(typeof(KnowledgePackManifest))]
[JsonSerializable(typeof(KnowledgePackArticle))]
public partial class KnowledgeJsonContext : JsonSerializerContext;
