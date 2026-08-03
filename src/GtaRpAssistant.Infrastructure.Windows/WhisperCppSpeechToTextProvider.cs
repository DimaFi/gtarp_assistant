using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record EmbeddedSttRuntimeMetrics(int? ProcessId, long WorkingSetBytes, long PrivateBytes);

public sealed class WhisperCppSpeechToTextProvider : ISpeechToTextProvider, IAsyncDisposable
{
    private readonly EmbeddedSttPackLocator _packs;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _processGate = new();
    private Process? _process;
    private Uri? _baseUri;
    private EmbeddedSttPackInspection? _activePack;
    private Timer? _idleTimer;
    private bool _disposed;
    private readonly Queue<string> _diagnostics = new();

    public WhisperCppSpeechToTextProvider(EmbeddedSttPackLocator packs) => _packs = packs;

    public string Id => "embedded-whisper-cpp-stt";
    public ProviderKind Kind => ProviderKind.BuiltIn;
    public ProviderCapabilities Capabilities => new()
    {
        SupportsAudioInput = true,
        SupportsTranscription = true,
        IsLocal = true,
    };
    internal int? RuntimeProcessId
    {
        get
        {
            var process = GetProcess();
            try { return process is { HasExited: false } ? process.Id : null; }
            catch (InvalidOperationException) { return null; }
        }
    }
    public EmbeddedSttRuntimeMetrics GetRuntimeMetrics()
    {
        var process = GetProcess();
        if (process is null) return new(null, 0, 0);
        try
        {
            if (process.HasExited) return new(null, 0, 0);
            process.Refresh();
            return new(process.Id, process.WorkingSet64, process.PrivateMemorySize64);
        }
        catch (InvalidOperationException) { return new(null, 0, 0); }
    }

    public async Task<IReadOnlyList<ProviderModelInfo>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var pack = await _packs.InspectAsync(cancellationToken);
        return pack.IsValid && pack.Manifest is not null
            ? [new(pack.Manifest.ModelId, $"{pack.Manifest.ModelId} (локально)", new HashSet<ProviderTask> { ProviderTask.SpeechToText })]
            : [];
    }

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var pack = await _packs.InspectAsync(cancellationToken);
        return new(pack.IsValid, pack.Message, pack.Manifest is null ? null : [pack.Manifest.ModelId]);
    }

    public async Task<TranscriptResult> TranscribeAsync(AudioSegment segment, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (segment.SampleRate != 16_000 || segment.Channels != 1)
            throw new ArgumentException("Локальный STT ожидает PCM16 mono 16 kHz.", nameof(segment));
        if (segment.PcmData.IsEmpty) throw new ArgumentException("Аудиосегмент пуст.", nameof(segment));

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            DisableIdleTimer();
            var pack = await _packs.InspectAsync(cancellationToken);
            if (!pack.IsValid || pack.Manifest is null) throw new InvalidOperationException(pack.Message);
            await EnsureStartedAsync(pack, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(pack.Manifest.Limits.RequestTimeoutSeconds));
            var memoryExceeded = false;
            using var watchdogCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var watchdog = WatchMemoryAsync(pack.Manifest.Limits.HardMemoryLimitBytes, () =>
            {
                memoryExceeded = true;
                timeout.Cancel();
            }, watchdogCancellation.Token);
            try
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("json"), "response_format");
                form.Add(new StringContent(pack.Manifest.Language), "language");
                form.Add(new StringContent(0.0.ToString(CultureInfo.InvariantCulture)), "temperature");
                form.Add(new StringContent(0.2.ToString(CultureInfo.InvariantCulture)), "temperature_inc");
                var audio = new ByteArrayContent(CreateWave(segment.PcmData.Span, segment.SampleRate, segment.Channels));
                audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(audio, "file", "segment.wav");

                var endpoint = new Uri(_baseUri!, pack.Manifest.InferencePath.TrimStart('/'));
                using var response = await _http.PostAsync(endpoint, form, timeout.Token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
                var text = json.RootElement.TryGetProperty("text", out var value) ? value.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Локальный STT вернул пустой transcript.");
                return new(text, 1);
            }
            catch (OperationCanceledException) when (memoryExceeded)
            {
                await StopRuntimeAsync();
                throw new InvalidOperationException("Локальный STT остановлен: превышен лимит памяти.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await StopRuntimeAsync();
                throw new TimeoutException("Локальный STT не завершил распознавание вовремя.");
            }
            catch (OperationCanceledException)
            {
                await StopRuntimeAsync();
                throw;
            }
            catch
            {
                if (GetProcess()?.HasExited != false) await StopRuntimeAsync();
                throw;
            }
            finally
            {
                watchdogCancellation.Cancel();
                try { await watchdog; } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            ScheduleIdleStop();
            _requestGate.Release();
        }
    }

    private async Task EnsureStartedAsync(EmbeddedSttPackInspection pack, CancellationToken cancellationToken)
    {
        var running = GetProcess();
        if (running is { HasExited: false } && _activePack?.Directory.Equals(pack.Directory, StringComparison.OrdinalIgnoreCase) == true)
            return;
        await StopRuntimeAsync();

        var port = ReserveLoopbackPort();
        var manifest = pack.Manifest!;
        var publicDirectory = Path.Combine(Path.GetTempPath(), "GtaRpAssistant", "stt-public");
        Directory.CreateDirectory(publicDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = pack.EntryPointPath!,
            WorkingDirectory = pack.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        AddArguments(startInfo, pack.ModelPath!, publicDirectory, port, manifest);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить whisper.cpp.");
        lock (_processGate) _diagnostics.Clear();
        _ = DrainAsync(process.StandardOutput, capture: false);
        _ = DrainAsync(process.StandardError, capture: true);
        lock (_processGate)
        {
            _process = process;
            _baseUri = new($"http://127.0.0.1:{port}/");
            _activePack = pack;
        }

        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(TimeSpan.FromSeconds(manifest.Limits.StartupTimeoutSeconds));
        try
        {
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException(
                    $"whisper.cpp завершился с кодом {process.ExitCode} до загрузки модели. {GetDiagnostics()}");
                if (MaximumMemory(process) >= manifest.Limits.HardMemoryLimitBytes)
                    throw new InvalidOperationException("whisper.cpp превысил лимит памяти при загрузке модели.");
                try
                {
                    using var response = await _http.GetAsync(new Uri(_baseUri!, "health"), startup.Token);
                    if (response.StatusCode == HttpStatusCode.OK) return;
                }
                catch (HttpRequestException) { }
                await Task.Delay(200, startup.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopRuntimeAsync();
            throw new TimeoutException("whisper.cpp не успел загрузить модель.");
        }
        catch
        {
            await StopRuntimeAsync();
            throw;
        }
    }

    internal static void AddArguments(ProcessStartInfo info, string modelPath, string publicDirectory, int port, EmbeddedSttPackManifest manifest)
    {
        foreach (var argument in new[]
        {
            "--model", modelPath,
            "--host", "127.0.0.1",
            "--port", port.ToString(CultureInfo.InvariantCulture),
            "--public", publicDirectory,
            "--language", manifest.Language,
            "--threads", manifest.Limits.Threads.ToString(CultureInfo.InvariantCulture),
            "--processors", "1",
            "--no-gpu",
            "--no-timestamps",
            "--no-flash-attn",
        }) info.ArgumentList.Add(argument);
    }

    private async Task WatchMemoryAsync(long hardLimitBytes, Action onExceeded, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var process = GetProcess();
            if (process is null) return;
            try { if (process.HasExited) return; }
            catch (InvalidOperationException) { return; }
            if (MaximumMemory(process) >= hardLimitBytes)
            {
                onExceeded();
                return;
            }
            await Task.Delay(200, cancellationToken);
        }
    }

    private void ScheduleIdleStop()
    {
        var seconds = _activePack?.Manifest?.Limits.IdleTtlSeconds;
        if (seconds is null || _disposed) return;
        _idleTimer ??= new Timer(static state => _ = ((WhisperCppSpeechToTextProvider)state!).StopIfIdleAsync(), this, Timeout.Infinite, Timeout.Infinite);
        _idleTimer.Change(TimeSpan.FromSeconds(seconds.Value), Timeout.InfiniteTimeSpan);
    }

    private void DisableIdleTimer() => _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

    private async Task StopIfIdleAsync()
    {
        if (!await _requestGate.WaitAsync(0)) return;
        try { await StopRuntimeAsync(); }
        finally { _requestGate.Release(); }
    }

    private async Task StopRuntimeAsync()
    {
        Process? process;
        lock (_processGate)
        {
            process = _process;
            _process = null;
            _baseUri = null;
            _activePack = null;
        }
        if (process is not null)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (!process.HasExited)
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (InvalidOperationException) { }
            catch (TimeoutException) { }
            finally { process.Dispose(); }
        }
    }

    private Process? GetProcess() { lock (_processGate) return _process; }

    private static long MaximumMemory(Process process)
    {
        try
        {
            process.Refresh();
            return Math.Max(process.WorkingSet64, process.PrivateMemorySize64);
        }
        catch (InvalidOperationException) { return 0; }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private async Task DrainAsync(StreamReader reader, bool capture)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!capture || string.IsNullOrWhiteSpace(line)) continue;
                lock (_processGate)
                {
                    _diagnostics.Enqueue(line.Trim());
                    while (_diagnostics.Count > 8) _diagnostics.Dequeue();
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private string GetDiagnostics()
    {
        lock (_processGate) return string.Join(" | ", _diagnostics);
    }

    private static byte[] CreateWave(ReadOnlySpan<byte> pcm, int sampleRate, int channels)
    {
        var result = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(result); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 36 + pcm.Length); "WAVEfmt "u8.CopyTo(result.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(22), checked((short)channels));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), sampleRate); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), sampleRate * channels * 2); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(32), checked((short)(channels * 2))); BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(34), 16);
        "data"u8.CopyTo(result.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), pcm.Length); pcm.CopyTo(result.AsSpan(44));
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        DisableIdleTimer();
        await _requestGate.WaitAsync();
        try { await StopRuntimeAsync(); }
        finally
        {
            _requestGate.Release();
            _idleTimer?.Dispose();
            _http.Dispose();
            _requestGate.Dispose();
        }
    }
}
