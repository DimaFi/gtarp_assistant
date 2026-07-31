using Microsoft.Win32;
using GtaRpAssistant.App.Services;

namespace GtaRpAssistant.App;

public sealed class WindowsStartupService(ApplicationExecutionMode executionMode)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public void Apply(bool enabled)
    {
        if (executionMode.IsAutomation) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue("GtaRpAssistant", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("GtaRpAssistant", throwOnMissingValue: false);
    }
}
