using GtaRpAssistant.App;
using GtaRpAssistant.App.Shell;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.App.Tests;

public sealed class VoiceHotkeyAndRecoveryTests
{
    [Fact]
    public void HoldGesture_EmitsOnePressAndReleaseDespiteKeyRepeat()
    {
        var tracker = new VoiceHotkeyGestureTracker();

        Assert.Equal(VoiceHotkeyGesture.Pressed, tracker.Update(0x41, true, false, true, true));
        Assert.Equal(VoiceHotkeyGesture.None, tracker.Update(0x41, true, false, true, true));
        Assert.Equal(VoiceHotkeyGesture.Released, tracker.Update(0x41, false, true, false, false));
        Assert.Equal(VoiceHotkeyGesture.None, tracker.Update(0x41, false, true, false, false));
    }

    [Fact]
    public void HoldGesture_IgnoresIncompleteChordAndOtherKeys()
    {
        var tracker = new VoiceHotkeyGestureTracker();

        Assert.Equal(VoiceHotkeyGesture.None, tracker.Update(0x41, true, false, true, false));
        Assert.Equal(VoiceHotkeyGesture.None, tracker.Update(0x51, true, false, true, true));
    }

    [Fact]
    public void HoldGesture_RemainsBalancedAcrossOneHundredCycles()
    {
        var tracker = new VoiceHotkeyGestureTracker();
        for (var cycle = 0; cycle < 100; cycle++)
        {
            Assert.Equal(VoiceHotkeyGesture.Pressed, tracker.Update(0x41, true, false, true, true));
            Assert.Equal(VoiceHotkeyGesture.None, tracker.Update(0x41, true, false, true, true));
            Assert.Equal(VoiceHotkeyGesture.Released, tracker.Update(0x41, false, true, false, false));
        }
    }

    [Fact]
    public void RecoveryPolicy_OnlyReturnsTheOriginallySelectedDevice()
    {
        var policy = MicrophoneRecoveryPolicy.Default;
        MicrophoneDeviceInfo[] devices =
        [
            new("default", "Default microphone", true),
            new("preferred", "USB microphone", false),
        ];

        Assert.Equal("preferred", policy.FindPreferred(devices, "preferred")?.Id);
        Assert.Null(policy.FindPreferred(devices, "missing"));
        Assert.Equal(10, policy.MaximumAttempts);
    }

    [Theory]
    [InlineData(0, VoiceInteractionMode.Toggle)]
    [InlineData(1, VoiceInteractionMode.Hold)]
    [InlineData(99, VoiceInteractionMode.Toggle)]
    public void VoiceHotkeySetting_MapsSafely(int value, VoiceInteractionMode expected) =>
        Assert.Equal(expected, SettingValues.VoiceHotkey(new AppSettings(VoiceHotkeyMode: value)));
}
