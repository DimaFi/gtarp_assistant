using GtaRpAssistant.Core;

namespace GtaRpAssistant.Core.Tests;

public sealed class ProactivePolicyTests
{
    [Fact]
    public void AutomaticHints_RespectMinuteAndTopicCooldowns()
    {
        var policy = new ProactivePolicy();
        var now = DateTimeOffset.UtcNow;
        Assert.True(policy.CanProcess(AssistantActivationKind.AutomaticVoice, "контракт", now, out _));
        policy.RecordShown(AssistantActivationKind.AutomaticVoice, "контракт", now);
        Assert.False(policy.CanProcess(AssistantActivationKind.AutomaticVoice, "другая тема", now.AddSeconds(30), out var minuteReason));
        Assert.Equal("one_per_minute", minuteReason);
        Assert.False(policy.CanProcess(AssistantActivationKind.AutomaticVoice, "контракт", now.AddSeconds(90), out var topicReason));
        Assert.Equal("topic_cooldown", topicReason);
    }

    [Fact]
    public void ManualRequest_BypassesDoNotDisturb()
    {
        var policy = new ProactivePolicy();
        policy.SnoozeForSession();
        Assert.False(policy.CanProcess(AssistantActivationKind.AutomaticVoice, "контракт", DateTimeOffset.UtcNow, out _));
        Assert.True(policy.CanProcess(AssistantActivationKind.ManualText, "контракт", DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void AutomaticHints_AreLimitedToThreePerTenMinutes()
    {
        var policy = new ProactivePolicy();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 3; index++)
        {
            var at = now.AddMinutes(index * 2 + index * .1);
            Assert.True(policy.CanProcess(AssistantActivationKind.AutomaticVoice, $"topic-{index}", at, out _));
            policy.RecordShown(AssistantActivationKind.AutomaticVoice, $"topic-{index}", at);
        }
        Assert.False(policy.CanProcess(AssistantActivationKind.AutomaticVoice, "topic-4", now.AddMinutes(7), out var reason));
        Assert.Equal("three_per_ten_minutes", reason);
    }
}
