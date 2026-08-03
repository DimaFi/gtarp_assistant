using GtaRpAssistant.App;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Tests;

public sealed class VoiceInteractionCoordinatorTests
{
    [Fact]
    public void SecondToggle_CancelsCurrentRequestAndToken()
    {
        using var coordinator = new VoiceInteractionCoordinator(new VoiceInteractionStateMachine());
        Assert.True(coordinator.Toggle(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20)));
        var token = coordinator.RequestCancellationToken;

        Assert.False(coordinator.Toggle(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20)));

        Assert.True(token.IsCancellationRequested);
        Assert.Equal(VoiceInteractionState.Cancelled, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task Deadline_CancelsRequest()
    {
        using var coordinator = new VoiceInteractionCoordinator(new VoiceInteractionStateMachine());
        coordinator.Toggle(VoiceInteractionMode.Hold, autoSubmit: false, TimeSpan.FromMilliseconds(20));

        await Task.Delay(150);

        Assert.Equal(VoiceInteractionState.Cancelled, coordinator.Snapshot.State);
        Assert.Contains("время", coordinator.Snapshot.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_CanBeEditedAndConfirmed()
    {
        using var coordinator = new VoiceInteractionCoordinator(new VoiceInteractionStateMachine());
        coordinator.Toggle(VoiceInteractionMode.Toggle, autoSubmit: false, TimeSpan.FromSeconds(20));

        Assert.True(coordinator.TryMarkListening());
        Assert.True(coordinator.TryMarkSpeechDetected());
        Assert.True(coordinator.TryMarkTranscribing());
        var decisionTask = coordinator.WaitForPreviewDecisionAsync("где находится клб", CancellationToken.None);
        Assert.Equal(VoiceInteractionState.Preview, coordinator.Snapshot.State);
        Assert.True(coordinator.ConfirmPreview("где находится клуб"));
        var decision = await decisionTask;
        Assert.True(coordinator.TryMarkSubmitting());

        Assert.Equal(VoiceInteractionState.Submitting, coordinator.Snapshot.State);
        Assert.Equal("где находится клуб", decision.Text);
        Assert.Equal("где находится клб", coordinator.Snapshot.Transcript);
    }

    [Fact]
    public async Task AutoSubmit_PassesPreviewWithoutWaiting()
    {
        using var coordinator = new VoiceInteractionCoordinator(new VoiceInteractionStateMachine());
        coordinator.Toggle(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20));
        coordinator.TryMarkListening();
        coordinator.TryMarkTranscribing();

        var decision = await coordinator.WaitForPreviewDecisionAsync("когда следующий ивент", CancellationToken.None);

        Assert.Equal("когда следующий ивент", decision.Text);
        Assert.Equal(VoiceInteractionState.Preview, coordinator.Snapshot.State);
    }
}
