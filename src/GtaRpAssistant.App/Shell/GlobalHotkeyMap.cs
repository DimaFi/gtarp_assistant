namespace GtaRpAssistant.App.Shell;

public enum GlobalHotkeyAction
{
    None,
    ToggleOverlay,
    TogglePause,
    ManualVoice,
    ManualVoiceHold,
    ManualVision,
}

public static class GlobalHotkeyMap
{
    public static GlobalHotkeyAction FromRegistrationId(int id) => id switch
    {
        1 => GlobalHotkeyAction.ToggleOverlay,
        2 => GlobalHotkeyAction.TogglePause,
        3 => GlobalHotkeyAction.ManualVoice,
        4 => GlobalHotkeyAction.ManualVision,
        _ => GlobalHotkeyAction.None,
    };
}
