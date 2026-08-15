using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class ResourceBudgetCoordinatorTests
{
    private const long Gib = 1024L * 1024 * 1024;

    [Fact]
    public async Task Balanced_DoesNotOverlapLocalChatAndVision()
    {
        var coordinator = Normal();
        var chat = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Chat, LocalAiPerformanceProfile.Balanced, true), default);
        var vision = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Vision, LocalAiPerformanceProfile.Balanced, true), default);

        Assert.True(chat.Granted);
        Assert.False(vision.Granted);
        Assert.Equal("chat_vision_mutual_exclusion", vision.Reason);

        await chat.Lease!.DisposeAsync();
        vision = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Vision, LocalAiPerformanceProfile.Balanced, true), default);
        Assert.True(vision.Granted);
        await vision.Lease!.DisposeAsync();
    }

    [Fact]
    public async Task HardPressure_DeniesLocalButNotCloudWorkload()
    {
        var coordinator = new ResourceBudgetCoordinator();
        coordinator.Update(Snapshot(availableRam: Gib, gta: true));

        var local = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Chat, LocalAiPerformanceProfile.Balanced, true), default);
        var cloud = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Chat, LocalAiPerformanceProfile.Balanced, false), default);

        Assert.Equal(ResourcePressureLevel.Hard, coordinator.Pressure);
        Assert.False(local.Granted);
        Assert.True(cloud.Granted);
        await cloud.Lease!.DisposeAsync();
    }

    [Fact]
    public void PressureRecovery_RequiresThreeHealthySamplesPerLevel()
    {
        var coordinator = new ResourceBudgetCoordinator();
        coordinator.Update(Snapshot(Gib, gta: true));
        for (var i = 0; i < 2; i++) coordinator.Update(Snapshot(8 * Gib, gta: true));
        Assert.Equal(ResourcePressureLevel.Hard, coordinator.Pressure);

        coordinator.Update(Snapshot(8 * Gib, gta: true));
        Assert.Equal(ResourcePressureLevel.Soft, coordinator.Pressure);
        for (var i = 0; i < 3; i++) coordinator.Update(Snapshot(8 * Gib, gta: true));
        Assert.Equal(ResourcePressureLevel.Normal, coordinator.Pressure);
    }

    [Fact]
    public async Task GtaRunning_PausesBackgroundWorkButKeepsManualKnowledgeOutsideCoordinator()
    {
        var coordinator = Normal(gta: true);
        var indexing = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.BackgroundIndexing, LocalAiPerformanceProfile.Quality, true), default);
        Assert.False(indexing.Granted);
        Assert.Equal("background_ai_paused_while_gta_is_running", indexing.Reason);
    }

    [Fact]
    public async Task EstimatedModelLoad_PreservesRamReserve()
    {
        var coordinator = new ResourceBudgetCoordinator();
        coordinator.Update(Snapshot(4 * Gib, gta: true));
        var result = await coordinator.TryAcquireAsync(new(
            AssistantWorkloadKind.Chat,
            LocalAiPerformanceProfile.Quality,
            true,
            3 * Gib), default);

        Assert.False(result.Granted);
        Assert.Equal("insufficient_ram_reserve", result.Reason);
    }

    [Fact]
    public async Task CancellationAndDoubleDispose_AreSafe()
    {
        var coordinator = Normal();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Chat, LocalAiPerformanceProfile.Balanced, true), cancelled.Token));

        var first = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Chat, LocalAiPerformanceProfile.Balanced, true), default);
        first.Lease!.Dispose();
        first.Lease.Dispose();
        var second = await coordinator.TryAcquireAsync(new(AssistantWorkloadKind.Chat, LocalAiPerformanceProfile.Balanced, true), default);
        Assert.True(second.Granted);
        second.Lease!.Dispose();
    }

    private static ResourceBudgetCoordinator Normal(bool gta = false)
    {
        var coordinator = new ResourceBudgetCoordinator();
        coordinator.Update(Snapshot(8 * Gib, gta));
        return coordinator;
    }

    private static ResourceSnapshot Snapshot(long availableRam, bool gta) =>
        new(16 * Gib, availableRam, null, null, 2, 150 * 1024 * 1024, gta, DateTimeOffset.UtcNow);
}
