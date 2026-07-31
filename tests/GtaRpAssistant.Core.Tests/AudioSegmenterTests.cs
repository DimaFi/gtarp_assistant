using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class AudioSegmenterTests
{
    [Fact]
    public void SpeechFollowedBySilence_ProducesNormalizedSegment()
    {
        var s = new EnergyAudioSegmenter(1000, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(20));
        var now = DateTimeOffset.UtcNow;
        s.Process(AudioSourceKind.UserMicrophone, new short[250], false, now);
        s.Process(AudioSourceKind.UserMicrophone, Enumerable.Repeat((short)1000, 300).ToArray(), true, now.AddMilliseconds(300));
        var result = s.Process(AudioSourceKind.UserMicrophone, new short[700], false, now.AddSeconds(1));
        Assert.NotNull(result); Assert.Equal(1000, result.SampleRate); Assert.Equal(1, result.Channels); Assert.Equal(950 * 2, result.PcmData.Length);
    }

    [Fact] public void ShortNoise_IsDiscarded() { var s = new EnergyAudioSegmenter(1000, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300)); var now = DateTimeOffset.UtcNow; s.Process(AudioSourceKind.UserMicrophone, new short[50], true, now); Assert.Null(s.Process(AudioSourceKind.UserMicrophone, new short[100], false, now.AddMilliseconds(100))); }
    [Fact] public void NoSpeech_DoesNotCreateSegment() => Assert.Null(new EnergyAudioSegmenter(1000).Process(AudioSourceKind.UserMicrophone, new short[1000], false, DateTimeOffset.UtcNow));
    [Fact] public void Reset_CancelsActiveSegment() { var s = new EnergyAudioSegmenter(1000); s.Process(AudioSourceKind.UserMicrophone, new short[500], true, DateTimeOffset.UtcNow); s.Reset(); Assert.False(s.IsActive); }
    [Fact] public void MaximumDuration_ClosesSegment() { var s = new EnergyAudioSegmenter(1000, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1)); Assert.NotNull(s.Process(AudioSourceKind.UserMicrophone, new short[1000], true, DateTimeOffset.UtcNow)); }
}
