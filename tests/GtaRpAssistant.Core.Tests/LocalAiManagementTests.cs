using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class LocalAiManagementTests
{
    [Fact]
    public void BalancedHardwareTier_IsBoundedAndVisionIsOnDemand()
    {
        var tier = LocalAiHardwareTierCatalog.For(LocalAiPerformanceProfile.Balanced);
        Assert.Equal(32, tier.MinimumSystemRamGb);
        Assert.Equal(6, tier.MaximumAssistantRamGb);
        Assert.True(tier.VisionOnDemand);
    }

    [Fact]
    public void Recommendation_PrefersPrimaryBalancedModelWhenMemoryFits()
    {
        var model = LocalAiRecommendedModelCatalog.Recommend(12L * 1024 * 1024 * 1024);

        Assert.Equal("qwen3-4b-2507", model.Id);
        Assert.True(model.SupportsRussian);
        Assert.True(model.SupportsJson);
    }

    [Fact]
    public void VisionRecommendation_OnlyReturnsVisionModel()
    {
        Assert.True(LocalAiRecommendedModelCatalog.Recommend(16L * 1024 * 1024 * 1024, needsVision: true).SupportsVision);
    }

    [Theory]
    [InlineData(LocalAiPerformanceProfile.Compact, 220, 2)]
    [InlineData(LocalAiPerformanceProfile.Balanced, 420, 4)]
    [InlineData(LocalAiPerformanceProfile.Quality, 700, 6)]
    public void GenerationProfile_HasBoundedOutputAndCpu(LocalAiPerformanceProfile profile, int tokens, int threads)
    {
        var settings = LocalAiGenerationSettings.For(profile);

        Assert.Equal(tokens, settings.MaxOutputTokens);
        Assert.Equal(threads, settings.CpuThreads);
        Assert.Equal(1, settings.QueueLimit);
    }

    [Fact]
    public void BalancedProfile_UsesAutomaticGpuOffload()
    {
        var settings = LocalAiGenerationSettings.For(LocalAiPerformanceProfile.Balanced);

        Assert.Equal(-1, settings.GpuOffloadLayers);
    }
}
