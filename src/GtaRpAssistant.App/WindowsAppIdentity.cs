using System.Runtime.InteropServices;

namespace GtaRpAssistant.App;

internal static class WindowsAppIdentity
{
    public const string AppUserModelId = "LABAI.GtaRpAssistant";

    public static void Apply()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
