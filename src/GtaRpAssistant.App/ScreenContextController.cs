using System.Globalization;
using System.Text.RegularExpressions;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App;

public sealed partial class ScreenContextController(WindowCaptureService capture, GameSessionMonitor gameMonitor, IScreenFrameDiffer differ,
    ILocalScreenOcr ocr, IScreenContextStore store, SettingsService settings, ILogger<ScreenContextController> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    public void Start()
    {
        if (_worker is not null) return;
        _lifetime = new();
        _worker = RunAsync(_lifetime.Token);
    }
    public async Task StopAsync()
    {
        if (_lifetime is null) return;
        _lifetime.Cancel();
        try { if (_worker is not null) await _worker; } catch (OperationCanceledException) { }
        _lifetime.Dispose(); _lifetime = null; _worker = null; store.Clear();
    }
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        ScreenFrame? previous = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var mode = SettingValues.ScreenObservation(settings.Current);
            var game = gameMonitor.Current;
            if (mode == ScreenObservationMode.Off || game is null) { previous = null; await Task.Delay(1000, cancellationToken); continue; }
            try
            {
                var current = await Task.Run(() => capture.CaptureAnalysisFrame(game.MainWindowHandle), cancellationToken);
                var diff = previous is null ? new ScreenFrameDiff(1, [ScreenRegion.Full]) : differ.Compare(previous, current);
                previous = current;
                if (diff.HasMeaningfulChange(settings.Current.ScreenDiffThreshold) && ocr.IsAvailable)
                    await AnalyzeAsync(game.MainWindowHandle, current.CapturedAt, diff, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug("Screen observation iteration failed; type={ErrorType}", ex.GetType().Name); }
            await Task.Delay(mode == ScreenObservationMode.EventTriggered ? 1500 : Math.Clamp(settings.Current.ScreenCaptureIntervalMs, 500, 5000), cancellationToken);
        }
    }
    private async Task AnalyzeAsync(nint handle, DateTimeOffset capturedAt, ScreenFrameDiff diff, CancellationToken cancellationToken)
    {
        byte[]? png = null;
        try
        {
            png = await Task.Run(() => capture.CapturePng(handle), cancellationToken);
            var ocrResult = await ocr.RecognizeAsync(png, cancellationToken);
            if (ocrResult.Fields.Count == 0) return;
            var recognition = KnownScreenRecognizer.Recognize(ocrResult.Fields);
            var profiledFields = ScreenFieldProfiler.Apply(recognition.Kind, ocrResult.Fields);
            store.Publish(new(capturedAt, recognition.Kind, recognition.Confidence, diff.ChangedRegions, profiledFields, ExtractNumbers(profiledFields), capturedAt.AddSeconds(Math.Clamp(settings.Current.ScreenContextTtlSeconds, 5, 60))));
            logger.LogInformation("Local screen context updated; screen={Screen}; fields={FieldCount}; change={ChangedRatio:0.000}", recognition.Kind, profiledFields.Count, diff.ChangedRatio);
        }
        finally { if (png is not null) Array.Clear(png); }
    }
    private static IReadOnlyList<ScreenNumberField> ExtractNumbers(IEnumerable<ScreenTextField> fields)
    {
        var result = new List<ScreenNumberField>();
        foreach (var field in fields) foreach (Match match in NumberPattern().Matches(field.Text))
        {
            var normalized = match.Value.Replace(" ", "").Replace(',', '.');
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) result.Add(new("number", value, match.Value, field.Confidence, field.Bounds));
        }
        return result.Take(8).ToArray();
    }
    [GeneratedRegex(@"\d[\d ]*(?:[.,]\d+)?")] private static partial Regex NumberPattern();
    public async ValueTask DisposeAsync() => await StopAsync();
}
