using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using System.Text;
using System.Text.Json;

namespace GtaRpAssistant.IntegrationTests;

public sealed class LocalAiEngineManagerTests
{
    [Fact]
    public async Task Manager_DelegatesToRegisteredEngine()
    {
        var adapter = new FakeAdapter();
        var manager = new LocalAiEngineManager([adapter]);
        var endpoint = new Uri("http://127.0.0.1:1234/v1");

        var snapshot = await manager.InspectAsync(LocalAiEngineKind.LmStudio, endpoint, default);
        await manager.StartServerAsync(LocalAiEngineKind.LmStudio, endpoint, default);
        var imported = await manager.ImportModelAsync(LocalAiEngineKind.LmStudio, "model.gguf", default);

        Assert.True(snapshot.IsInstalled);
        Assert.Equal("imported/model", imported.Key);
        Assert.Equal(3, adapter.Calls);
        Assert.Equal([LocalAiEngineKind.LmStudio], manager.SupportedEngines);
    }

    [Fact]
    public async Task Manager_RejectsUnregisteredEngine()
    {
        var manager = new LocalAiEngineManager([new FakeAdapter()]);

        await Assert.ThrowsAsync<NotSupportedException>(() => manager.InspectAsync(LocalAiEngineKind.Ollama,
            new Uri("http://127.0.0.1:11434"), default));
    }

    [Fact]
    public async Task LmStudioAdapter_ReportsInvalidConfiguredCliPath()
    {
        using var adapter = new LmStudioEngineAdapter(new PathSettings(@"Z:\missing\lms.exe", ""));

        var snapshot = await adapter.InspectAsync(new Uri("http://127.0.0.1:1/v1"), default);

        Assert.False(snapshot.CliAvailable);
        Assert.Null(snapshot.CliPath);
        Assert.Contains(@"Z:\missing\lms.exe", snapshot.Message);
    }

    [Fact]
    public async Task LmStudioAdapter_UsesConfiguredApplicationOutsideStandardFolders()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-lmstudio-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "LM Studio.exe");
        await File.WriteAllBytesAsync(executable, []);
        try
        {
            using var adapter = new LmStudioEngineAdapter(new PathSettings("", $"  \"{executable}\"  "));

            var snapshot = await adapter.InspectAsync(new Uri("http://127.0.0.1:1/v1"), default);

            Assert.True(snapshot.IsInstalled);
            Assert.Equal(Path.GetFullPath(executable), snapshot.ApplicationPath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LmStudioAdapter_ParsesFormatTypeVariantsAndSelectedVariant()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "llm",
              "key": "qwen/example",
              "display_name": "Qwen Example",
              "format": "gguf",
              "quantization": { "name": "Q4_K_M" },
              "size_bytes": 1234,
              "params_string": "4B",
              "max_context_length": 32768,
              "loaded_instances": [{ "id": "qwen/example:1" }],
              "capabilities": { "vision": true, "trained_for_tool_use": true },
              "variants": ["qwen/example@q4_k_m", { "key": "qwen/example@q8_0" }],
              "selected_variant": "qwen/example@q4_k_m"
            }
            """);

        var model = LmStudioEngineAdapter.ParseModel(document.RootElement);

        Assert.NotNull(model);
        Assert.Equal("gguf", model.Format);
        Assert.Equal(LocalAiModelType.Llm, model.Type);
        Assert.True(model.IsChatModel);
        Assert.Equal("llm", model.Engine);
        Assert.Equal(["qwen/example@q4_k_m", "qwen/example@q8_0"], model.Variants);
        Assert.Equal("qwen/example@q4_k_m", model.SelectedVariant);
        Assert.True(model.IsLoaded);
        Assert.Equal("qwen/example:1", model.InstanceId);
    }

    [Fact]
    public void LmStudioAdapter_ParsesEmbeddingAsNonChatModel()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "embedding",
              "key": "nomic/embed",
              "display_name": "Nomic Embed",
              "format": "gguf",
              "size_bytes": 42,
              "max_context_length": 2048,
              "loaded_instances": []
            }
            """);

        var model = LmStudioEngineAdapter.ParseModel(document.RootElement);

        Assert.NotNull(model);
        Assert.Equal("gguf", model.Format);
        Assert.Equal(LocalAiModelType.Embedding, model.Type);
        Assert.False(model.IsChatModel);
        Assert.Equal("embedding", model.Engine);
    }

    [Fact]
    public async Task LmStudioAdapter_ImportsUnicodePathThroughDryRunAndCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta rp модель " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cli = Path.Combine(directory, "lms.exe");
        var modelPath = Path.Combine(directory, "Моя модель.gguf");
        await File.WriteAllBytesAsync(cli, []);
        await File.WriteAllBytesAsync(modelPath, Encoding.ASCII.GetBytes("GGUFfixture"));
        var runner = new FakeCommandRunner(
            new(0, "[]", ""),
            new(0, "dry-run ok", ""),
            new(0, "imported user/my-model", ""),
            new(0, """
                [{"type":"llm","modelKey":"user/my-model","format":"gguf","displayName":"Моя модель","sizeBytes":11,"maxContextLength":4096,"variants":["user/my-model@q4_0"],"selectedVariant":"user/my-model@q4_0"}]
                """, ""));
        try
        {
            using var adapter = new LmStudioEngineAdapter(new PathSettings(cli, ""), runner);

            var imported = await adapter.ImportModelAsync(modelPath, default);

            Assert.Equal("user/my-model", imported.Key);
            Assert.True(imported.IsChatModel);
            Assert.Equal(4, runner.Calls.Count);
            Assert.Equal(["ls", "--llm", "--json"], runner.Calls[0].Arguments);
            var repository = LmStudioEngineAdapter.BuildLocalImportRepository(modelPath);
            Assert.Equal(["import", Path.GetFullPath(modelPath), "--copy", "--yes", "--user-repo", repository, "--dry-run"], runner.Calls[1].Arguments);
            Assert.Equal(["import", Path.GetFullPath(modelPath), "--copy", "--yes", "--user-repo", repository], runner.Calls[2].Arguments);
            Assert.Equal(["ls", "--llm", "--json"], runner.Calls[3].Arguments);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("project-mmproj-f16.gguf", "GGUFvalid")]
    [InlineData("model-00001-of-00002.gguf", "GGUFvalid")]
    [InlineData("model.bin", "GGUFvalid")]
    [InlineData("model.gguf", "NOTgguf")]
    public async Task LmStudioAdapter_RejectsUnsafeOrInvalidModelBeforeCli(string fileName, string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-invalid-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cli = Path.Combine(directory, "lms.exe");
        var modelPath = Path.Combine(directory, fileName);
        await File.WriteAllBytesAsync(cli, []);
        await File.WriteAllBytesAsync(modelPath, Encoding.ASCII.GetBytes(content));
        var runner = new FakeCommandRunner();
        try
        {
            using var adapter = new LmStudioEngineAdapter(new PathSettings(cli, ""), runner);

            await Assert.ThrowsAnyAsync<Exception>(() => adapter.ImportModelAsync(modelPath, default));

            Assert.Empty(runner.Calls);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LmStudioAdapter_DryRunFailureStopsBeforeCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-dry-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var cli = Path.Combine(directory, "lms.exe");
        var modelPath = Path.Combine(directory, "model.gguf");
        await File.WriteAllBytesAsync(cli, []);
        await File.WriteAllBytesAsync(modelPath, Encoding.ASCII.GetBytes("GGUFfixture"));
        var runner = new FakeCommandRunner(new(0, "[]", ""), new(2, "", "invalid model"));
        try
        {
            using var adapter = new LmStudioEngineAdapter(new PathSettings(cli, ""), runner);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ImportModelAsync(modelPath, default));

            Assert.Contains("безопасной проверке", exception.Message);
            Assert.Equal(2, runner.Calls.Count);
            Assert.Contains("--dry-run", runner.Calls[1].Arguments);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(@"C:\Models\Qwen Model Q4_K_M.gguf")]
    [InlineData(@"D:\Модели\Локальная модель.gguf")]
    public void LmStudioAdapter_LocalImportRepositoryIsOfflineSafeAndStable(string path)
    {
        var first = LmStudioEngineAdapter.BuildLocalImportRepository(path);
        var second = LmStudioEngineAdapter.BuildLocalImportRepository(path);

        Assert.Equal(first, second);
        Assert.Matches("^local-imports/[a-z0-9-]+$", first);
        Assert.DoesNotContain(' ', first);
    }

    [Fact]
    public void LmStudioAdapter_LoadArgumentsApplyContextGpuTtlAndSingleParallelRequest()
    {
        var request = new LocalAiLoadRequest("local/model", 2048, TimeSpan.FromMinutes(2), "off");

        var load = LmStudioEngineAdapter.BuildLoadArguments(request, estimateOnly: false);
        var estimate = LmStudioEngineAdapter.BuildLoadArguments(request with { GpuOffload = "auto" }, estimateOnly: true);

        Assert.Equal(["load", "local/model", "--context-length", "2048", "--parallel", "1", "--yes", "--gpu", "off", "--ttl", "120"], load);
        Assert.Equal(["load", "local/model", "--context-length", "2048", "--parallel", "1", "--yes", "--estimate-only"], estimate);
        Assert.DoesNotContain("--gpu", estimate);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1.5")]
    [InlineData("-0.1")]
    public void LmStudioAdapter_LoadArgumentsRejectInvalidGpuProfile(string gpu) =>
        Assert.Throws<ArgumentException>(() => LmStudioEngineAdapter.BuildLoadArguments(new("local/model", 2048, TimeSpan.FromMinutes(2), gpu), false));

    [Fact]
    public void LmStudioAdapter_ReadsDesktopInstallLocationMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-lm-metadata-" + Guid.NewGuid().ToString("N"));
        var metadataDirectory = Path.Combine(directory, ".lmstudio", ".internal");
        var application = Path.Combine(directory, "apps", "LM Studio.exe");
        Directory.CreateDirectory(metadataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(application)!);
        File.WriteAllBytes(application, []);
        File.WriteAllText(Path.Combine(metadataDirectory, "app-install-location.json"),
            JsonSerializer.Serialize(new { path = application }), Encoding.UTF8);
        try
        {
            var found = LmStudioEngineAdapter.FindDesktopAppFromInstallMetadata(directory);

            Assert.Equal(Path.GetFullPath(application), found);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CommandRunner_TimeoutTerminatesLongRunningProcessTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new LocalAiCommandRunner();
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var started = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            command,
            ["/d", "/s", "/c", "ping.exe 127.0.0.1 -n 30 > nul"],
            TimeSpan.FromMilliseconds(250),
            default));

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CommandRunner_CancellationTerminatesLongRunningProcessTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new LocalAiCommandRunner();
        var command = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            command,
            ["/d", "/s", "/c", "ping.exe 127.0.0.1 -n 30 > nul"],
            TimeSpan.FromSeconds(30),
            cancellation.Token));
    }

    private sealed record PathSettings(string? LmStudioCliPath, string? LmStudioApplicationPath) : ILocalAiPathSettings;

    private sealed class FakeAdapter : ILocalAiEngineAdapter
    {
        public int Calls { get; private set; }
        public LocalAiEngineKind Kind => LocalAiEngineKind.LmStudio;
        public string DisplayName => "LM Studio";
        public Task<LocalAiEngineSnapshot> InspectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LocalAiEngineSnapshot(Kind, DisplayName, true, true, false, endpoint,
                LocalAiReadiness.ServerStopped, [], null, "stopped", DateTimeOffset.UtcNow));
        }
        public Task StartServerAsync(Uri endpoint, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<IReadOnlyList<LocalAiModelDescriptor>> GetModelsAsync(Uri endpoint, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LocalAiModelDescriptor>>([]);
        public Task LoadModelAsync(Uri endpoint, LocalAiLoadRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UnloadModelAsync(Uri endpoint, string instanceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LocalAiDownloadProgress> DownloadModelAsync(Uri endpoint, string modelKey, string? quantization, IProgress<LocalAiDownloadProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(new LocalAiDownloadProgress(modelKey, "completed", 1, 1, 0));
        public Task<LocalAiModelDescriptor> ImportModelAsync(string filePath, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LocalAiModelDescriptor("imported/model", "Imported", "gguf", null, 1, null, 1, false, null, false, false, null, LocalAiModelType.Llm));
        }
        public Task<LocalAiResourceEstimate> EstimateAsync(string modelKey, LocalAiLoadRequest request, CancellationToken cancellationToken) => Task.FromResult(new LocalAiResourceEstimate(1, 1, 0, null, "low", true, "ok"));
    }

    private sealed class FakeCommandRunner(params LocalAiCommandResult[] results) : ILocalAiCommandRunner
    {
        private readonly Queue<LocalAiCommandResult> _results = new(results);
        public List<CommandCall> Calls { get; } = [];

        public Task<LocalAiCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new(executable, arguments.ToArray(), timeout));
            if (_results.Count == 0) throw new InvalidOperationException("No fake CLI result configured.");
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record CommandCall(string Executable, IReadOnlyList<string> Arguments, TimeSpan Timeout);
}
