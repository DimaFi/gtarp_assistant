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
    [Fact] public void SpokenArticleNumbers_AreNormalizedLikeDigits() =>
        Assert.Equal(TranscriptDeduplicator.Normalize("УК 12.6, 12.1 и 17.4"), TranscriptDeduplicator.Normalize("УК двенадцать точка шесть, двенадцать точка один и семнадцать точка четыре"));
    [Fact] public void MultipleSpokenCriminalArticles_AreExtracted() =>
        Assert.Equal([new("УК", "12.6"), new("УК", "12.1"), new("УК", "17.4")],
            LegalArticleReferenceExtractor.Extract("статьи уголовного кодекса двенадцать точка шесть, двенадцать точка один и семнадцать точка четыре"));
}
