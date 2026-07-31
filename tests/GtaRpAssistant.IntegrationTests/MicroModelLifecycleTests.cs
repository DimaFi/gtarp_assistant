using System.Text.Json;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.IntegrationTests;

public sealed class MicroModelLifecycleTests
{
    [Fact]
    public async Task MockHost_StartsOnDemand_ReturnsStrictJson_AndStopsAfterIdleTtl()
    {
        await using var manager = Manager(TimeSpan.FromMilliseconds(300));
        var states = new List<MicroModelState>();
        manager.StateChanged += (_, args) => states.Add(args.Status.State);
        await manager.VerifyPackageAsync(default);

        var response = await manager.GenerateAsync(Request(), default);

        using var json = JsonDocument.Parse(response.Json);
        Assert.Equal("show", json.RootElement.GetProperty("decision").GetString());
        Assert.Equal("fact.1", json.RootElement.GetProperty("usedFactIds")[0].GetString());
        Assert.Contains(MicroModelState.Starting, states);
        Assert.Contains(MicroModelState.Generating, states);
        Assert.Contains(MicroModelState.Idle, states);

        await WaitUntilAsync(async () => (await manager.GetStatusAsync(default)).State == MicroModelState.Stopped, TimeSpan.FromSeconds(5));
        Assert.Equal(MicroModelState.Stopped, (await manager.GetStatusAsync(default)).State);
    }

    [Fact]
    public async Task Manager_UsesOneHostForActiveAndQueuedRequest()
    {
        await using var manager = Manager(TimeSpan.FromSeconds(3));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var hostProcessIds = new System.Collections.Concurrent.ConcurrentBag<int>();
        manager.StateChanged += (_, args) =>
        {
            if (args.Status.ProcessId is { } id) hostProcessIds.Add(id);
        };
        var first = manager.GenerateAsync(Request("request-1"), timeout.Token);
        var second = manager.GenerateAsync(Request("request-2"), timeout.Token);
        await Task.WhenAll(first, second);
        var processId = (await manager.GetStatusAsync(timeout.Token)).ProcessId;

        Assert.NotNull(processId);
        Assert.Equal(processId, (await manager.GetStatusAsync(timeout.Token)).ProcessId);
        Assert.Single(hostProcessIds.Distinct());
        await manager.StopAsync(timeout.Token);
        Assert.Equal(MicroModelState.Stopped, (await manager.GetStatusAsync(timeout.Token)).State);
    }

    [Fact]
    public async Task Manager_RejectsContextLargerThanProtocolBudget()
    {
        await using var manager = Manager(TimeSpan.FromSeconds(3));
        var request = Request() with
        {
            Transcript = Enumerable.Range(0, 7).Select(index => new MicroModelTranscriptEvidence(index.ToString(), AudioSourceKind.GameAudio, "context")).ToArray(),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => manager.GenerateAsync(request, default));
        Assert.Equal(MicroModelState.Stopped, (await manager.GetStatusAsync(default)).State);
    }

    [Fact]
    public async Task Manager_AllowsOnlyOneActiveAndOneQueuedRequest()
    {
        await using var manager = Manager(TimeSpan.FromSeconds(3));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var first = manager.GenerateAsync(Request("queue-1"), timeout.Token);
        var second = manager.GenerateAsync(Request("queue-2"), timeout.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GenerateAsync(Request("queue-3"), timeout.Token));
        await Task.WhenAll(first, second);
    }

    private static MicroModelManager Manager(TimeSpan idleTtl) => new(
        new(HostPath(), idleTtl, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
        new MicroModelResourceGuard());

    private static MicroModelRequest Request(string id = "request-1") => new(
        id,
        MicroModelTask.GroundedShortAnswer,
        "Что делать?",
        "all",
        [new("transcript.1", AudioSourceKind.UserMicrophone, "Подскажи следующий шаг")],
        [new("fact.1", "article.1", "По данным игроков: выполните безопасный следующий шаг.", true, DateTimeOffset.UtcNow)]);

    private static string HostPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = output.Parent!.Name;
        var root = output;
        for (var index = 0; index < 5; index++) root = root.Parent!;
        var path = Path.Combine(root.FullName, "src", "GtaRpAssistant.MicroModelHost", "bin", configuration, "net8.0", "GtaRpAssistant.MicroModelHost.dll");
        Assert.True(File.Exists(path), $"MicroModelHost was not built: {path}");
        return path;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail("Condition was not reached before timeout.");
    }
}
