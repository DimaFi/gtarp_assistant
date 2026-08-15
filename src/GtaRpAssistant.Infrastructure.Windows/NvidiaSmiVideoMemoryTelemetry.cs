using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class NvidiaSmiVideoMemoryTelemetry : IVideoMemoryTelemetry
{
    private const long Mib = 1024L * 1024;

    public bool TryCapture(out long totalBytes, out long availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Executable(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.StartInfo.ArgumentList.Add("--query-gpu=memory.total,memory.free");
            process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
            if (!process.Start()) return false;
            if (!process.WaitForExit(750))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return false;
            }
            var output = process.StandardOutput.ReadToEnd();
            return process.ExitCode == 0 && TryParse(output, out totalBytes, out availableBytes);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public static bool TryParse(string output, out long totalBytes, out long availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;
        var candidates = new List<(long Total, long Free)>();
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalMib)
                || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var freeMib)
                || totalMib <= 0 || freeMib < 0 || freeMib > totalMib) continue;
            candidates.Add((totalMib, freeMib));
        }
        if (candidates.Count == 0) return false;
        var selected = candidates.OrderByDescending(x => x.Total).First();
        totalBytes = checked(selected.Total * Mib);
        availableBytes = checked(selected.Free * Mib);
        return true;
    }

    private static string Executable()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        return File.Exists(installed) ? installed : "nvidia-smi.exe";
    }
}
