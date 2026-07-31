using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class StateAndTranscriptTests
{
    [Fact] public void InvalidTransition_Throws() => Assert.Throws<InvalidOperationException>(() => new SessionStateMachine().TransitionTo(AssistantSessionState.Listening));
    [Fact] public void ValidTransition_ChangesState() { var s = new SessionStateMachine(); s.TransitionTo(AssistantSessionState.WaitingForGame); Assert.Equal(AssistantSessionState.WaitingForGame, s.State); }
    [Fact] public void TranscriptBuffer_Sorts() { var now = DateTimeOffset.UtcNow; var b = new TranscriptBuffer(TimeSpan.FromMinutes(3)); b.Add(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "b", 1)); b.Add(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now.AddSeconds(-1), now, "a", 1)); Assert.Equal("a", b.Snapshot()[0].Text); }
    [Fact] public void TranscriptBuffer_ClearWorks() { var now = DateTimeOffset.UtcNow; var b = new TranscriptBuffer(TimeSpan.FromMinutes(3)); b.Add(new(Guid.NewGuid(), AudioSourceKind.UserMicrophone, now, now, "a", 1)); b.Clear(); Assert.Empty(b.Snapshot()); }
    [Fact] public void TranscriptBuffer_RemoveSupportsMicPreference() { var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var b = new TranscriptBuffer(TimeSpan.FromMinutes(3)); b.Add(new(id, AudioSourceKind.GameAudio, now, now, "дубль", 1)); Assert.True(b.Remove(id)); Assert.Empty(b.Snapshot()); }
}
