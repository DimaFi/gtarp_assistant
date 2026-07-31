using System.Diagnostics;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class GameProcessDetector : IGameProcessDetector
{
    public Task<GameProcessInfo?> FindAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var name in profile.ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        var title = process.MainWindowTitle;
                        if (profile.WindowTitlePatterns.Count == 0 || profile.WindowTitlePatterns.Any(x => title.Contains(x, StringComparison.OrdinalIgnoreCase)) || process.MainWindowHandle != 0)
                            return Task.FromResult<GameProcessInfo?>(new(process.Id, process.MainWindowHandle, process.ProcessName));
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        return Task.FromResult<GameProcessInfo?>(null);
    }
}
