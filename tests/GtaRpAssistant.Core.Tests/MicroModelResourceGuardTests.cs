using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class MicroModelResourceGuardTests
{
    private readonly MicroModelResourceGuard _guard = new();

    [Theory]
    [InlineData(749, ResourceDecision.Continue)]
    [InlineData(750, ResourceDecision.StopGeneration)]
    [InlineData(899, ResourceDecision.StopGeneration)]
    [InlineData(900, ResourceDecision.TerminateAndFallback)]
    [InlineData(1024, ResourceDecision.TerminateAndFallback)]
    public void Guard_UsesSoftHardAndAbsoluteLimits(long mebibytes, ResourceDecision expected)
    {
        var bytes = mebibytes * 1024 * 1024;
        Assert.Equal(expected, _guard.Evaluate(new(bytes, bytes, bytes, 0)));
    }

    [Fact]
    public void Guard_UsesLargestMemoryMetric()
    {
        var result = _guard.Evaluate(new(100 * 1024 * 1024, 901 * 1024 * 1024, 200 * 1024 * 1024, 0));
        Assert.Equal(ResourceDecision.TerminateAndFallback, result);
    }
}
