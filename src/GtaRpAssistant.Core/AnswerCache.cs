using System.Security.Cryptography;
using System.Text;

namespace GtaRpAssistant.Core;

public sealed record AnswerCacheEntry(AssistantAnswer Answer, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int HitCount);

public interface IAnswerCache
{
    Task<AnswerCacheEntry?> TryGetAsync(string key, CancellationToken cancellationToken);
    Task StoreAsync(string key, AssistantAnswer answer, TimeSpan ttl, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryAnswerCache(int capacity = 256) : IAnswerCache
{
    private readonly object _gate = new();
    private readonly int _capacity = Math.Clamp(capacity, 16, 2048);
    private readonly Dictionary<string, AnswerCacheEntry> _entries = new(StringComparer.Ordinal);

    public Task<AnswerCacheEntry?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry)) return Task.FromResult<AnswerCacheEntry?>(null);
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key);
                return Task.FromResult<AnswerCacheEntry?>(null);
            }
            var hit = entry with { HitCount = entry.HitCount + 1 };
            _entries[key] = hit;
            return Task.FromResult<AnswerCacheEntry?>(hit);
        }
    }

    public Task StoreAsync(string key, AssistantAnswer answer, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (answer.Decision != AnswerDecision.Show) return Task.CompletedTask;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _entries[key] = new(answer, now, now.Add(ttl), 0);
            if (_entries.Count > _capacity)
                foreach (var expiredKey in _entries.OrderBy(x => x.Value.ExpiresAt).Take(_entries.Count - _capacity).Select(x => x.Key).ToArray())
                    _entries.Remove(expiredKey);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _entries.Clear();
        return Task.CompletedTask;
    }
}

public sealed class ConfigurableAnswerCache(
    Func<bool> persistenceEnabled,
    IAnswerCache transientCache,
    Func<IAnswerCache> persistentCacheFactory) : IAnswerCache
{
    private readonly Lazy<IAnswerCache> _persistentCache = new(persistentCacheFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    private IAnswerCache Active => persistenceEnabled() ? _persistentCache.Value : transientCache;

    public Task<AnswerCacheEntry?> TryGetAsync(string key, CancellationToken cancellationToken) => Active.TryGetAsync(key, cancellationToken);
    public Task StoreAsync(string key, AssistantAnswer answer, TimeSpan ttl, CancellationToken cancellationToken) => Active.StoreAsync(key, answer, ttl, cancellationToken);
    public Task ClearAsync(CancellationToken cancellationToken) => Active.ClearAsync(cancellationToken);
}

public static class AnswerCacheKeyBuilder
{
    public const string PolicyVersion = "smart-assistant-v1";

    public static string Create(string question, string server, KnowledgeMatch match, UserPersonalizationContext? personalization)
    {
        var value = new StringBuilder(2048)
            .Append(PolicyVersion).Append('\n')
            .Append(TranscriptDeduplicator.Normalize(question)).Append('\n')
            .Append(server.Trim().ToLowerInvariant()).Append('\n')
            .Append(match.ArticleId).Append('|').Append(match.HasConflict).Append('|').Append(match.IsOutdated).Append('\n')
            .Append(match.PreparedAnswer).Append('\n');

        foreach (var fact in match.Facts.OrderBy(x => x.Id, StringComparer.Ordinal))
            value.Append(fact.Id).Append('|')
                .Append(fact.Verified).Append('|')
                .Append(fact.ServerScope).Append('|')
                .Append(fact.UpdatedAt.ToUniversalTime().Ticks).Append('|')
                .Append(fact.Text).Append('\n');

        if (personalization is not null)
        {
            var profile = personalization.Personality.Normalize();
            value.Append("profile|").Append(profile.DetailLevel).Append('|').Append(profile.HumorLevel).Append('|')
                .Append(profile.InitiativeLevel).Append('|').Append(profile.Tone).Append('|').Append(profile.AdaptiveEnabled).Append('\n');
            foreach (var memory in personalization.Memories.OrderBy(x => x.Id))
                value.Append("memory|").Append(memory.Id).Append('|').Append(memory.Category).Append('|')
                    .Append(memory.UpdatedAt.ToUniversalTime().Ticks).Append('|').Append(memory.Content).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()))).ToLowerInvariant();
    }
}
