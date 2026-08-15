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
        try
        {
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardInput.BaseStream.WriteAsync(pngImage, cancellationToken);
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            _ = await errors;
            if (process.ExitCode != 0) return new([]);
            var dimensions = ReadPngDimensions(pngImage.Span);
            return ParseTsv(output, dimensions.Width, dimensions.Height);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
            }
        }
    }

    internal static ScreenOcrResult ParseTsv(string tsv, int imageWidth = 1, int imageHeight = 1)
    {
        var fields = new List<ScreenTextField>();
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length < 12 || !double.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || confidence < 35) continue;
            if (!int.TryParse(parts[6], out var left) || !int.TryParse(parts[7], out var top) || !int.TryParse(parts[8], out var width) || !int.TryParse(parts[9], out var height)) continue;
            var text = parts[11].Trim();
            if (text.Length < 2) continue;
            fields.Add(new("text", text, confidence / 100, new(
                Math.Clamp((double)left / imageWidth, 0, 1),
                Math.Clamp((double)top / imageHeight, 0, 1),
                Math.Clamp((double)width / imageWidth, 0, 1),
                Math.Clamp((double)height / imageHeight, 0, 1))));
        }
        return new(fields);
    }

    private static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71) return (1, 1);
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[16..20]);
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
        return (Math.Max(1, width), Math.Max(1, height));
    }

    private static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("GTA_RP_TESSERACT_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator).Select(x => Path.Combine(x, "tesseract.exe")).FirstOrDefault(File.Exists);
    }
}
