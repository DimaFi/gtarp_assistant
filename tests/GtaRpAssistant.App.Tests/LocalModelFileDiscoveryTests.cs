using System.IO;
using System.Text;
using GtaRpAssistant.App.Services;

namespace GtaRpAssistant.App.Tests;

public sealed class LocalModelFileDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GtaRpAssistant.ModelDiscovery", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scan_FindsNestedValidGgufAndRejectsUnsafeCandidates()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "Модели с пробелами")).FullName;
        Write(Path.Combine(nested, "assistant.gguf"), "GGUFpayload");
        Write(Path.Combine(nested, "assistant-mmproj.gguf"), "GGUFpayload");
        Write(Path.Combine(nested, "assistant-00001-of-00002.gguf"), "GGUFpayload");
        Write(Path.Combine(nested, "broken.gguf"), "NOPEpayload");

        var result = await new LocalModelFileDiscovery().ScanAsync(_root, default);

        Assert.Equal(4, result.Count);
        Assert.True(Assert.Single(result, x => x.DisplayName == "assistant.gguf").IsSupported);
        Assert.Contains("mmproj", Assert.Single(result, x => x.DisplayName.Contains("mmproj", StringComparison.Ordinal)).UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("составных", Assert.Single(result, x => x.DisplayName.Contains("00001", StringComparison.Ordinal)).UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("сигнатуру", Assert.Single(result, x => x.DisplayName == "broken.gguf").UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_MissingDirectoryFailsClearly() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new LocalModelFileDiscovery().ScanAsync(Path.Combine(_root, "missing"), default));

    private static void Write(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
