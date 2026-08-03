using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class VoiceInteractionStateMachineTests
{
    [Fact]
    public void FullAutomaticPreviewFlow_PreservesTranscript()
    {
        var machine = new VoiceInteractionStateMachine();

        var started = machine.Start(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20));

        Assert.NotEqual(Guid.Empty, started.RequestId);
        Assert.True(machine.TryTransition(VoiceInteractionState.Listening));
        Assert.True(machine.TryTransition(VoiceInteractionState.SpeechDetected));
        Assert.True(machine.TryTransition(VoiceInteractionState.Transcribing));
        Assert.True(machine.TryTransition(VoiceInteractionState.Preview, "как прокачать rednecks"));
        Assert.True(machine.TryTransition(VoiceInteractionState.Submitting));
        Assert.True(machine.TryTransition(VoiceInteractionState.AnswerReady));
        Assert.Equal("как прокачать rednecks", machine.Snapshot.Transcript);
        Assert.True(machine.Snapshot.AutoSubmit);
    }

    [Fact]
    public void InvalidTransition_IsRejectedWithoutChangingState()
    {
        var machine = new VoiceInteractionStateMachine();
        machine.Start(VoiceInteractionMode.Hold, autoSubmit: false, TimeSpan.FromSeconds(10));

        Assert.False(machine.TryTransition(VoiceInteractionState.AnswerReady));
        Assert.Equal(VoiceInteractionState.Arming, machine.Snapshot.State);
    }

    [Fact]
    public void CancelledRequest_CanStartAgain()
    {
        var machine = new VoiceInteractionStateMachine();
        var first = machine.Start(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20));
        Assert.True(machine.TryTransition(VoiceInteractionState.Cancelled, detail: "cancel"));

        var second = machine.Start(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20));

        Assert.NotEqual(first.RequestId, second.RequestId);
        Assert.Equal(VoiceInteractionState.Arming, machine.Snapshot.State);
    }

    [Fact]
    public void AnswerReady_DoesNotBlockNextRequest()
    {
        var machine = new VoiceInteractionStateMachine();
        machine.Start(VoiceInteractionMode.Toggle, autoSubmit: true, TimeSpan.FromSeconds(20));
        machine.TryTransition(VoiceInteractionState.Listening);
        machine.TryTransition(VoiceInteractionState.Transcribing);
        machine.TryTransition(VoiceInteractionState.Preview, "вопрос");
        machine.TryTransition(VoiceInteractionState.Submitting);
        machine.TryTransition(VoiceInteractionState.AnswerReady);

        var next = machine.Start(VoiceInteractionMode.Toggle, autoSubmit: false, TimeSpan.FromSeconds(20));

        Assert.Equal(VoiceInteractionState.Arming, next.State);
        Assert.False(next.AutoSubmit);
    }
}
