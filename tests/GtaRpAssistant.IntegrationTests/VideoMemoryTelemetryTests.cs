using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.IntegrationTests;

public sealed class VideoMemoryTelemetryTests
{
    [Fact]
    public void NvidiaOutput_SelectsLargestAdapterAndConvertsMib()
    {
        var parsed = NvidiaSmiVideoMemoryTelemetry.TryParse("4096, 1024\n16384, 6144\n", out var total, out var free);
        Assert.True(parsed);
        Assert.Equal(16L * 1024 * 1024 * 1024, total);
        Assert.Equal(6L * 1024 * 1024 * 1024, free);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not supported")]
    [InlineData("4096, 8192")]
    public void InvalidNvidiaOutput_IsUnavailable(string output)
    {
        Assert.False(NvidiaSmiVideoMemoryTelemetry.TryParse(output, out _, out _));
    }
}
