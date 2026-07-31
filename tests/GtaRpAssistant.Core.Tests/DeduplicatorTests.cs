using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class DeduplicatorTests
{
    private static TranscriptEntry Entry(AudioSourceKind source, string text, int offset = 0) { var at = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddSeconds(offset); return new(Guid.NewGuid(), source, at, at, text, 1); }
    [Fact] public void IdenticalCrossSource_IsDuplicate() => Assert.True(new TranscriptDeduplicator().IsDuplicate(Entry(AudioSourceKind.GameAudio, "Привет"), [Entry(AudioSourceKind.UserMicrophone, "привет")]));
    [Fact] public void MinorDifference_IsDuplicate() => Assert.True(new TranscriptDeduplicator(threshold: .7).IsDuplicate(Entry(AudioSourceKind.GameAudio, "почему контракт не запускается"), [Entry(AudioSourceKind.UserMicrophone, "почему контракт не запускается?")]));
    [Fact] public void DifferentText_IsNotDuplicate() => Assert.False(new TranscriptDeduplicator().IsDuplicate(Entry(AudioSourceKind.GameAudio, "другая фраза"), [Entry(AudioSourceKind.UserMicrophone, "привет")]));
    [Fact] public void OutsideWindow_IsNotDuplicate() => Assert.False(new TranscriptDeduplicator().IsDuplicate(Entry(AudioSourceKind.GameAudio, "привет", 3), [Entry(AudioSourceKind.UserMicrophone, "привет")]));
    [Fact] public void YoAndYe_AreEquivalent() => Assert.Equal(TranscriptDeduplicator.Normalize("Ёлка"), TranscriptDeduplicator.Normalize("елка"));
}
