using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class AudioRingBufferTests
{
    [Fact] public void WriteBelowCapacity_ReturnsSamples() { var b = new AudioRingBuffer(TimeSpan.FromSeconds(1), 4); b.Write(AudioSourceKind.UserMicrophone, [1, 2]); Assert.Equal([1, 2], b.ReadLast(AudioSourceKind.UserMicrophone, TimeSpan.FromSeconds(1)).Samples); }
    [Fact] public void WriteAboveCapacity_KeepsNewest() { var b = new AudioRingBuffer(TimeSpan.FromSeconds(1), 4); b.Write(AudioSourceKind.UserMicrophone, [1, 2, 3, 4, 5, 6]); Assert.Equal([3, 4, 5, 6], b.ReadLast(AudioSourceKind.UserMicrophone, TimeSpan.FromSeconds(1)).Samples); }
    [Fact] public void WrapAround_PreservesOrder() { var b = new AudioRingBuffer(TimeSpan.FromSeconds(1), 4); b.Write(AudioSourceKind.UserMicrophone, [1, 2, 3]); b.Write(AudioSourceKind.UserMicrophone, [4, 5]); Assert.Equal([2, 3, 4, 5], b.ReadLast(AudioSourceKind.UserMicrophone, TimeSpan.FromSeconds(1)).Samples); }
    [Fact] public void ReadLast_ReturnsRequestedDuration() { var b = new AudioRingBuffer(TimeSpan.FromSeconds(2), 4); b.Write(AudioSourceKind.UserMicrophone, [1, 2, 3, 4, 5, 6]); Assert.Equal([5, 6], b.ReadLast(AudioSourceKind.UserMicrophone, TimeSpan.FromSeconds(.5)).Samples); }
    [Fact] public void Clear_RemovesAllSources() { var b = new AudioRingBuffer(TimeSpan.FromSeconds(1), 4); b.Write(AudioSourceKind.UserMicrophone, [1]); b.Clear(); Assert.Empty(b.ReadLast(AudioSourceKind.UserMicrophone, TimeSpan.FromSeconds(1)).Samples); }
    [Fact] public void Sources_AreIndependent() { var b = new AudioRingBuffer(TimeSpan.FromSeconds(1), 4); b.Write(AudioSourceKind.UserMicrophone, [1]); b.Write(AudioSourceKind.GameAudio, [2]); Assert.Equal([1], b.ReadLast(AudioSourceKind.UserMicrophone, TimeSpan.FromSeconds(1)).Samples); }
}
