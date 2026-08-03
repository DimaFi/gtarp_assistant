using System.Security.Cryptography;
using System.Text.Json;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record EmbeddedSttPackFile
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public long SizeBytes { get; init; }
}

public sealed record EmbeddedSttPackLimits
{
    public int Threads { get; init; } = 2;
    public int StartupTimeoutSeconds { get; init; } = 90;
    public int RequestTimeoutSeconds { get; init; } = 45;
    public int IdleTtlSeconds { get; init; } = 120;
    public long HardMemoryLimitBytes { get; init; } = 1_100L * 1024 * 1024;
}

public sealed record EmbeddedSttPackManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Runtime { get; init; }
    public required string RuntimeVersion { get; init; }
    public string Architecture { get; init; } = "win-x64";
    public required string EntryPoint { get; init; }
    public required string ModelId { get; init; }
    public required string ModelFile { get; init; }
    public string Language { get; init; } = "ru";
    public string InferencePath { get; init; } = "/inference";
    public required string LicenseFile { get; init; }
    public required string RuntimeSource { get; init; }
    public required string ModelSource { get; init; }
    public IReadOnlyList<EmbeddedSttPackFile> Files { get; init; } = [];
    public EmbeddedSttPackLimits Limits { get; init; } = new();
}

public sealed record EmbeddedSttPackInspection(
    bool IsValid,
    string Directory,
    EmbeddedSttPackManifest? Manifest,
    string Message)
{
    public string? EntryPointPath => Manifest is null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, Manifest.EntryPoint));
    public string? ModelPath => Manifest is null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, Manifest.ModelFile));
}

public sealed class EmbeddedSttPackLocator(Func<string?> configuredPath, string defaultDirectory)
{
    public const string ManifestFileName = "stt-pack.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedKey;
    private EmbeddedSttPackInspection? _cachedInspection;

    public string ResolveDirectory()
    {
        var configured = configuredPath();
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? defaultDirectory
            : Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')));
    }

    public async Task<EmbeddedSttPackInspection> InspectAsync(CancellationToken cancellationToken)
    {
        string directory;
        try { directory = ResolveDirectory(); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, configuredPath() ?? "", null, "Путь к локальному STT-паку некорректен.");
        }

        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
            return new(false, directory, null, $"STT-пак не установлен: отсутствует {ManifestFileName}.");

        var stamp = File.GetLastWriteTimeUtc(manifestPath).Ticks;
        var length = new FileInfo(manifestPath).Length;
        var cacheKey = $"{manifestPath}|{stamp}|{length}";
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedInspection is { IsValid: true, Manifest: not null }
                && string.Equals(_cachedKey, cacheKey + "|" + BuildFileStamp(directory, _cachedInspection.Manifest.Files), StringComparison.OrdinalIgnoreCase))
                return _cachedInspection;
            var inspection = await ValidateAsync(directory, manifestPath, cancellationToken);
            if (inspection.IsValid)
            {
                _cachedKey = cacheKey + "|" + BuildFileStamp(directory, inspection.Manifest!.Files);
                _cachedInspection = inspection;
            }
            return inspection;
        }
        finally { _gate.Release(); }
    }

    private static string BuildFileStamp(string directory, IReadOnlyList<EmbeddedSttPackFile> files)
    {
        var values = new List<string>(files.Count);
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(SafePackPath(directory, file.Path));
                values.Add($"{file.Path}:{info.Exists}:{info.Length}:{info.LastWriteTimeUtc.Ticks}");
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                values.Add($"{file.Path}:invalid");
            }
        }
        return string.Join(';', values);
    }

    private static async Task<EmbeddedSttPackInspection> ValidateAsync(
        string directory,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<EmbeddedSttPackManifest>(manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                ?? throw new InvalidDataException("Манифест STT-пака пуст.");
            ValidateManifest(manifest);

            var declared = manifest.Files.ToDictionary(file => NormalizeRelativePath(file.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var required in new[] { manifest.EntryPoint, manifest.ModelFile, manifest.LicenseFile })
                if (!declared.ContainsKey(NormalizeRelativePath(required)))
                    throw new InvalidDataException($"Обязательный файл '{required}' не включён в список контроля целостности.");

            foreach (var file in declared.Values)
            {
                var path = SafePackPath(directory, file.Path);
                if (!File.Exists(path)) throw new FileNotFoundException($"Файл STT-пака отсутствует: {file.Path}");
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Ссылки внутри STT-пака запрещены: {file.Path}");
                if (file.SizeBytes > 0 && info.Length != file.SizeBytes)
                    throw new InvalidDataException($"Размер файла STT-пака не совпадает: {file.Path}");
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA-256 файла STT-пака не совпадает: {file.Path}");
            }

            return new(true, directory, manifest, $"Локальный STT-пак готов: {manifest.ModelId}.");
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return new(false, directory, null, $"STT-пак не прошёл проверку: {exception.Message}");
        }
    }

    private static void ValidateManifest(EmbeddedSttPackManifest manifest)
    {
        var requiredValues = new Dictionary<string, string?>
        {
            [nameof(manifest.Id)] = manifest.Id,
            [nameof(manifest.Version)] = manifest.Version,
            [nameof(manifest.Runtime)] = manifest.Runtime,
            [nameof(manifest.RuntimeVersion)] = manifest.RuntimeVersion,
            [nameof(manifest.EntryPoint)] = manifest.EntryPoint,
            [nameof(manifest.ModelId)] = manifest.ModelId,
            [nameof(manifest.ModelFile)] = manifest.ModelFile,
            [nameof(manifest.Language)] = manifest.Language,
            [nameof(manifest.InferencePath)] = manifest.InferencePath,
            [nameof(manifest.LicenseFile)] = manifest.LicenseFile,
            [nameof(manifest.RuntimeSource)] = manifest.RuntimeSource,
            [nameof(manifest.ModelSource)] = manifest.ModelSource,
        };
        var missing = requiredValues.FirstOrDefault(pair => string.IsNullOrWhiteSpace(pair.Value));
        if (!string.IsNullOrEmpty(missing.Key)) throw new InvalidDataException($"Обязательное поле манифеста отсутствует: {missing.Key}.");
        if (manifest.Files is null) throw new InvalidDataException("Список файлов STT-пака отсутствует.");
        if (manifest.Limits is null) throw new InvalidDataException("Лимиты STT-пака отсутствуют.");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"Неподдерживаемая версия манифеста: {manifest.SchemaVersion}.");
        if (!string.Equals(manifest.Runtime, "whisper.cpp", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Поддерживается только runtime whisper.cpp.");
        if (!string.Equals(manifest.Architecture, "win-x64", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("STT-пак должен быть собран для win-x64.");
        if (!string.Equals(manifest.Language, "ru", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("STT-пак должен использовать русскую модель.");
        if (!manifest.InferencePath.StartsWith('/') || manifest.InferencePath.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("Некорректный inference endpoint.");
        if (manifest.Files.Count == 0) throw new InvalidDataException("Список файлов STT-пака пуст.");
        if (manifest.Limits.Threads is < 1 or > 8) throw new InvalidDataException("Число CPU-потоков должно быть от 1 до 8.");
        if (manifest.Limits.StartupTimeoutSeconds is < 10 or > 300 || manifest.Limits.RequestTimeoutSeconds is < 5 or > 180)
            throw new InvalidDataException("Timeout STT-пака находится вне безопасного диапазона.");
        if (manifest.Limits.IdleTtlSeconds is < 10 or > 900) throw new InvalidDataException("Idle TTL STT-пака находится вне безопасного диапазона.");
        if (manifest.Limits.HardMemoryLimitBytes is < 256L * 1024 * 1024 or > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Лимит памяти STT-пака находится вне безопасного диапазона.");
        foreach (var source in new[] { manifest.RuntimeSource, manifest.ModelSource })
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Источники runtime и модели должны быть HTTPS URL.");
        foreach (var file in manifest.Files)
        {
            _ = NormalizeRelativePath(file.Path);
            if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException($"Некорректный SHA-256: {file.Path}");
            if (file.SizeBytes < 0) throw new InvalidDataException($"Некорректный размер файла: {file.Path}");
        }
    }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)) throw new InvalidDataException("Пути в STT-манифесте должны быть относительными.");
        var normalized = value.Replace('/', Path.DirectorySeparatorChar).Trim();
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Небезопасный путь в STT-манифесте: {value}");
        return normalized;
    }

    private static string SafePackPath(string directory, string relativePath)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, NormalizeRelativePath(relativePath)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Файл выходит за границы STT-пака.");
        return path;
    }
}
