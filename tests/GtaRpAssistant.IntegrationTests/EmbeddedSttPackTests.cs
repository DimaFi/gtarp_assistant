using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GtaRpAssistant.Infrastructure.Windows;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.IntegrationTests;

public sealed class EmbeddedSttPackTests
{
    [Fact]
    public async Task ValidPack_IsAcceptedAndExposesResolvedPaths()
    {
        using var pack = TestPack.Create();
        var locator = new EmbeddedSttPackLocator(() => pack.Directory, "unused");

        var result = await locator.InspectAsync(CancellationToken.None);

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(Path.Combine(pack.Directory, "runtime", "whisper-server.exe"), result.EntryPointPath);
        Assert.Equal(Path.Combine(pack.Directory, "models", "ggml-base-q8_0.bin"), result.ModelPath);
    }

    [Fact]
    public void PortablePack_IsDiscoveredWhenCustomPathIsEmpty()
    {
        using var pack = TestPack.Create();
        var locator = new EmbeddedSttPackLocator(() => "", "missing-default", pack.Directory);

        Assert.Equal(Path.GetFullPath(pack.Directory), locator.ResolveDirectory());
    }

    [Fact]
    public async Task ModifiedModel_IsRejected()
    {
        using var pack = TestPack.Create();
        await File.AppendAllTextAsync(Path.Combine(pack.Directory, "models", "ggml-base-q8_0.bin"), "tampered");

        var result = await new EmbeddedSttPackLocator(() => pack.Directory, "unused").InspectAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("не совпадает", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CachedInspection_IsInvalidatedWhenPackFileChanges()
    {
        using var pack = TestPack.Create();
        var locator = new EmbeddedSttPackLocator(() => pack.Directory, "unused");
        Assert.True((await locator.InspectAsync(CancellationToken.None)).IsValid);

        await File.AppendAllTextAsync(Path.Combine(pack.Directory, "models", "ggml-base-q8_0.bin"), "tampered");
        var result = await locator.InspectAsync(CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task TraversalPath_IsRejectedBeforeAnyProcessCanStart()
    {
        using var pack = TestPack.Create(manifest => manifest with { EntryPoint = "../whisper-server.exe" });

        var result = await new EmbeddedSttPackLocator(() => pack.Directory, "unused").InspectAsync(CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("Небезопасный путь", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeArguments_AreLoopbackCpuOnlyAndBounded()
    {
        var manifest = TestPack.CreateManifest([]);
        var info = new ProcessStartInfo("whisper-server.exe");

        WhisperCppSpeechToTextProvider.AddArguments(info, @"D:\pack\model.bin", @"D:\empty", 49152, manifest);
        var args = info.ArgumentList.ToArray();

        Assert.Contains("127.0.0.1", args);
        Assert.Contains("49152", args);
        Assert.Contains("--no-gpu", args);
        Assert.Contains("--no-flash-attn", args);
        Assert.Equal("2", args[Array.IndexOf(args, "--threads") + 1]);
        Assert.DoesNotContain("0.0.0.0", args);
    }

    [Fact]
    public async Task RealPack_ReusesRuntimeAndCancellationKillsIt_WhenAssetsAreProvided()
    {
        var packDirectory = Environment.GetEnvironmentVariable("GTA_RP_STT_TEST_PACK");
        var wavePath = Environment.GetEnvironmentVariable("GTA_RP_STT_TEST_WAV");
        if (string.IsNullOrWhiteSpace(packDirectory) || string.IsNullOrWhiteSpace(wavePath)) return;
        var segment = ReadPcm16MonoWave(wavePath);
        await using var provider = new WhisperCppSpeechToTextProvider(new(() => packDirectory, "unused"));

        var first = await provider.TranscribeAsync(segment, CancellationToken.None);
        var processId = provider.RuntimeProcessId;
        var second = await provider.TranscribeAsync(segment with { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(first.Text));
        Assert.False(string.IsNullOrWhiteSpace(second.Text));
        Assert.NotNull(processId);
        Assert.Equal(processId, provider.RuntimeProcessId);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.TranscribeAsync(segment with { Id = Guid.NewGuid() }, cancellation.Token));
        Assert.Null(provider.RuntimeProcessId);
    }

    private static AudioSegment ReadPcm16MonoWave(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || BitConverter.ToInt16(bytes, 20) != 1 || BitConverter.ToInt16(bytes, 22) != 1
            || BitConverter.ToInt32(bytes, 24) != 16_000 || BitConverter.ToInt16(bytes, 34) != 16)
            throw new InvalidDataException("Smoke WAV must be PCM16 mono 16 kHz.");
        var dataOffset = FindChunk(bytes, "data"u8.ToArray());
        var length = BitConverter.ToInt32(bytes, dataOffset + 4);
        var pcm = bytes.AsMemory(dataOffset + 8, length);
        var duration = TimeSpan.FromSeconds(pcm.Length / 2d / 16_000d);
        var endedAt = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, endedAt - duration, endedAt, 16_000, 1, pcm);
    }

    private static int FindChunk(byte[] bytes, byte[] name)
    {
        for (var offset = 12; offset <= bytes.Length - 8;)
        {
            if (bytes.AsSpan(offset, 4).SequenceEqual(name)) return offset;
            offset += 8 + BitConverter.ToInt32(bytes, offset + 4);
        }
        throw new InvalidDataException("WAV data chunk is missing.");
    }

    private sealed class TestPack : IDisposable
    {
        private TestPack(string directory) => Directory = directory;
        public string Directory { get; }

        public static TestPack Create(Func<EmbeddedSttPackManifest, EmbeddedSttPackManifest>? transform = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "GtaRpAssistant.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path.Combine(directory, "runtime"));
            System.IO.Directory.CreateDirectory(Path.Combine(directory, "models"));
            File.WriteAllText(Path.Combine(directory, "runtime", "whisper-server.exe"), "runtime");
            File.WriteAllText(Path.Combine(directory, "models", "ggml-base-q8_0.bin"), "model");
            File.WriteAllText(Path.Combine(directory, "LICENSE-whisper.cpp.txt"), "MIT");
            var files = new[]
            {
                Describe(directory, "runtime/whisper-server.exe"),
                Describe(directory, "models/ggml-base-q8_0.bin"),
                Describe(directory, "LICENSE-whisper.cpp.txt"),
            };
            var manifest = transform?.Invoke(CreateManifest(files)) ?? CreateManifest(files);
            File.WriteAllText(Path.Combine(directory, EmbeddedSttPackLocator.ManifestFileName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            return new(directory);
        }

        public static EmbeddedSttPackManifest CreateManifest(IReadOnlyList<EmbeddedSttPackFile> files) => new()
        {
            Id = "gta-rp-assistant-stt-base-q8",
            Version = "1.0.0",
            Runtime = "whisper.cpp",
            RuntimeVersion = "1.9.1",
            EntryPoint = "runtime/whisper-server.exe",
            ModelId = "whisper-base-q8_0-ru",
            ModelFile = "models/ggml-base-q8_0.bin",
            LicenseFile = "LICENSE-whisper.cpp.txt",
            RuntimeSource = "https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.1",
            ModelSource = "https://huggingface.co/ggerganov/whisper.cpp",
            Files = files,
        };

        private static EmbeddedSttPackFile Describe(string root, string relative)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            using var stream = File.OpenRead(path);
            return new()
            {
                Path = relative,
                SizeBytes = stream.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            };
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
