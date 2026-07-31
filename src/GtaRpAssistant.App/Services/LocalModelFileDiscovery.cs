using System.IO;
using System.Text.RegularExpressions;

namespace GtaRpAssistant.App.Services;

public sealed record LocalModelFileCandidate(
    string Path,
    string DisplayName,
    long SizeBytes,
    bool IsSupported,
    string? UnsupportedReason);

public interface ILocalModelFileDiscovery
{
    Task<IReadOnlyList<LocalModelFileCandidate>> ScanAsync(string directory, CancellationToken cancellationToken);
}

public sealed partial class LocalModelFileDiscovery : ILocalModelFileDiscovery
{
    private const int MaximumDirectories = 2_000;
    private const int MaximumCandidates = 500;

    public Task<IReadOnlyList<LocalModelFileCandidate>> ScanAsync(string directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Папка модели не указана.", nameof(directory));
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Папка модели не найдена: {root}");
        return Task.Run<IReadOnlyList<LocalModelFileCandidate>>(() => Scan(root, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<LocalModelFileCandidate> Scan(string root, CancellationToken cancellationToken)
    {
        var result = new List<LocalModelFileCandidate>();
        var pending = new Stack<string>();
        pending.Push(root);
        var visited = 0;
        while (pending.Count > 0 && result.Count < MaximumCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (++visited > MaximumDirectories) break;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.gguf", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Add(Inspect(file));
                    if (result.Count >= MaximumCandidates) break;
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                // A single protected/broken child must not make the explicitly selected tree unusable.
            }
        }

        return result.OrderByDescending(x => x.IsSupported).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static LocalModelFileCandidate Inspect(string path)
    {
        var file = new FileInfo(path);
        string? reason = null;
        if (file.Name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            reason = "Файл mmproj дополняет vision-модель и не является самостоятельной chat-моделью.";
        else if (ShardNameRegex().IsMatch(file.Name))
            reason = "Автоматический импорт составных GGUF пока не поддерживается.";
        else if (!HasGgufMagic(path))
            reason = "Файл не содержит сигнатуру GGUF.";
        return new(file.FullName, file.Name, file.Exists ? file.Length : 0, reason is null, reason);
    }

    private static bool HasGgufMagic(string path)
    {
        try
        {
            Span<byte> magic = stackalloc byte[4];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4, FileOptions.SequentialScan);
            return stream.Read(magic) == magic.Length && magic.SequenceEqual("GGUF"u8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    [GeneratedRegex(@"-\d{5}-of-\d{5}\.gguf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShardNameRegex();
}
