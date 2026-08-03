using GtaRpAssistant.App;

namespace GtaRpAssistant.App.Tests;

public sealed class MicrophoneTestServiceTests
{
    [Fact]
    public void CalculateLevel_HandlesSilenceAndLoudSignal()
    {
        Assert.Equal(0, MicrophoneTestService.CalculateLevel([]));
        Assert.Equal(0, MicrophoneTestService.CalculateLevel(new short[160]));

        var level = MicrophoneTestService.CalculateLevel(Enumerable.Repeat((short)8000, 160).ToArray());

        Assert.Equal(1, level, 3);
    }

    [Fact]
    public void CalculateLevel_ClampsExtremeSamples()
    {
        var level = MicrophoneTestService.CalculateLevel(Enumerable.Repeat(short.MaxValue, 160).ToArray());

        Assert.InRange(level, 0, 1);
    }
}
