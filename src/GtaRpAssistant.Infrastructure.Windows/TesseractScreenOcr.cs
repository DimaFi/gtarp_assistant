using System.Diagnostics;
using System.Globalization;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class TesseractScreenOcr : ILocalScreenOcr
{
    private readonly string? _executable = FindExecutable();
    public bool IsAvailable => _executable is not null;

    public async Task<ScreenOcrResult> RecognizeAsync(ReadOnlyMemory<byte> pngImage, CancellationToken cancellationToken)
    {
        if (_executable is null) return new([]);
        var start = new ProcessStartInfo(_executable, "stdin stdout -l rus+eng tsv --psm 11")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить локальный OCR.");
        await process.StandardInput.BaseStream.WriteAsync(pngImage, cancellationToken);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) return new([]);
        return ParseTsv(output);
    }

    internal static ScreenOcrResult ParseTsv(string tsv)
    {
        var fields = new List<ScreenTextField>();
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length < 12 || !double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || confidence < 35) continue;
            if (!int.TryParse(parts[6], out var left) || !int.TryParse(parts[7], out var top) || !int.TryParse(parts[8], out var width) || !int.TryParse(parts[9], out var height)) continue;
            var text = parts[11].Trim();
            if (text.Length < 2) continue;
            fields.Add(new("text", text, confidence / 100, new(left, top, width, height)));
        }
        return new(fields);
    }

    private static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("GTA_RP_TESSERACT_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator).Select(x => Path.Combine(x, "tesseract.exe")).FirstOrDefault(File.Exists);
    }
}
