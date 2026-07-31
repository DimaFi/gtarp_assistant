using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed record MicroModelManagerOptions(
    string HostPath,
    TimeSpan IdleTtl,
    TimeSpan StartTimeout,
    TimeSpan RequestTimeout,
    string? PackageDirectory = null)
{
    public static MicroModelManagerOptions CreateDefault(string applicationDirectory) => new(
        ResolveHostPath(applicationDirectory),
        TimeSpan.FromSeconds(25),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30));

    private static string ResolveHostPath(string applicationDirectory)
    {
        var directory = Path.Combine(applicationDirectory, "micro-model-host");
        var executable = Path.Combine(directory, "GtaRpAssistant.MicroModelHost.exe");
        return File.Exists(executable) ? executable : Path.Combine(directory, "GtaRpAssistant.MicroModelHost.dll");
    }
}

public sealed class MicroModelManager(
    MicroModelManagerOptions options,
    IMicroModelResourceGuard resourceGuard) : IMicroModelManager
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _stateGate = new();
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _exitMonitor;
    private int _pendingRequests;
    private bool _disposed;
    private MicroModelStatus _status = new(MicroModelState.Stopped, null, "MicroModel остановлена.", DateTimeOffset.UtcNow);

    public event EventHandler<MicroModelStateChangedEventArgs>? StateChanged;

    public Task<MicroModelStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate) return Task.FromResult(_status);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false } && _pipe is { IsConnected: true }) return;
            CleanupProcess();
            if (!File.Exists(options.HostPath))
            {
                SetStatus(MicroModelState.NotInstalled, null, "MicroModelHost не найден.");
                throw new FileNotFoundException("MicroModelHost не найден.", options.HostPath);
            }

            SetStatus(MicroModelState.Starting, null, "Запуск локальной MicroModel…");
            var pipeName = $"gta-rp-micro-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var startInfo = CreateStartInfo(options.HostPath, pipeName, options.IdleTtl);
            var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить MicroModelHost.");
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(options.StartTimeout, cancellationToken);
            }
            catch
            {
                TryKill(process);
                process.Dispose();
                pipe.Dispose();
                throw;
            }

            _process = process;
            _pipe = pipe;
            _reader = new StreamReader(pipe, leaveOpen: true);
            _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            SetStatus(MicroModelState.Ready, process.Id, "MicroModel готова.");
            _exitMonitor = MonitorExitAsync(process);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not FileNotFoundException)
        {
            SetStatus(MicroModelState.Faulted, null, $"MicroModelHost: {ex.GetType().Name}");
            CleanupProcess();
            throw;
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task<MicroModelResponse> GenerateAsync(MicroModelRequest request, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        if (Interlocked.Increment(ref _pendingRequests) > 2)
        {
            Interlocked.Decrement(ref _pendingRequests);
            throw new InvalidOperationException("Очередь MicroModel уже содержит один ожидающий запрос.");
        }

        try
        {
            await _requestGate.WaitAsync(cancellationToken);
            try
            {
                await StartAsync(cancellationToken);
                var process = _process ?? throw new InvalidOperationException("MicroModelHost не запущен.");
                await EnforceResourcePolicyAsync(process, cancellationToken);
                SetStatus(MicroModelState.Generating, process.Id, "MicroModel формирует grounded-ответ…");

                var protocolRequest = new MicroModelPipeRequest(request.RequestId, "generate", request);
                await _writer!.WriteLineAsync(JsonSerializer.Serialize(protocolRequest).AsMemory(), cancellationToken);
                var line = await _reader!.ReadLineAsync(cancellationToken).AsTask().WaitAsync(options.RequestTimeout, cancellationToken);
                if (line is null) throw new EndOfStreamException("MicroModelHost закрыл named pipe без ответа.");
                var response = JsonSerializer.Deserialize<MicroModelPipeResponse>(line) ?? throw new InvalidDataException("MicroModelHost вернул пустой protocol response.");
                if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal)) throw new InvalidDataException("MicroModel response ID не совпадает с request ID.");
                if (!response.Success || response.Response is null) throw new InvalidDataException($"MicroModelHost отклонил запрос: {response.Error ?? "unknown"}.");
                using (JsonDocument.Parse(response.Response.Json)) { }
                await EnforceResourcePolicyAsync(process, cancellationToken);
                SetStatus(MicroModelState.Idle, process.Id, "MicroModel ожидает завершения idle TTL.");
                return response.Response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not MicroModelResourceException)
            {
                SetStatus(MicroModelState.Faulted, _process is { HasExited: false } process ? process.Id : null, $"MicroModel request: {ex.GetType().Name}");
                throw;
            }
            finally { _requestGate.Release(); }
        }
        finally { Interlocked.Decrement(ref _pendingRequests); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            try { await StopCoreAsync(cancellationToken); }
            finally { _lifecycleGate.Release(); }
        }
        finally { _requestGate.Release(); }
    }

    public async Task VerifyPackageAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        SetStatus(MicroModelState.Verifying, _process is { HasExited: false } process ? process.Id : null, "Проверка MicroModel package…");
        if (!File.Exists(options.HostPath))
        {
            SetStatus(MicroModelState.NotInstalled, null, "MicroModelHost не найден.");
            throw new FileNotFoundException("MicroModelHost не найден.", options.HostPath);
        }
        if (string.IsNullOrWhiteSpace(options.PackageDirectory))
        {
            SetStatus(MicroModelState.Stopped, null, "Mock runtime готов; model package не требуется.");
            return;
        }

        var manifestPath = Path.Combine(options.PackageDirectory, "manifest.json");
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<MicroModelPackageManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("MicroModel manifest пуст.");
        var modelPath = SafePackagePath(options.PackageDirectory, manifest.ModelFile);
        var licensePath = SafePackagePath(options.PackageDirectory, manifest.LicenseFile);
        var promptPath = SafePackagePath(options.PackageDirectory, manifest.PromptTemplateFile);
        if (!File.Exists(modelPath) || !File.Exists(licensePath) || !File.Exists(promptPath)) throw new FileNotFoundException("MicroModel package неполон.");
        await using var model = File.OpenRead(modelPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(model, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(hash, manifest.ModelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("MicroModel checksum не совпадает.");
        SetStatus(MicroModelState.Stopped, null, $"MicroModel package {manifest.Id} {manifest.Version} проверен.");
    }

    private async Task EnforceResourcePolicyAsync(Process process, CancellationToken cancellationToken)
    {
        process.Refresh();
        var metrics = new MicroModelProcessMetrics(process.WorkingSet64, process.PrivateMemorySize64, process.PrivateMemorySize64, 0);
        var decision = resourceGuard.Evaluate(metrics);
        if (decision == ResourceDecision.Continue) return;
        if (decision == ResourceDecision.StopGeneration)
            throw new MicroModelResourceException("MicroModel достигла soft memory limit; запрос передан fallback.");

        SetStatus(MicroModelState.MemoryLimitExceeded, process.Id, "MicroModel превысила hard memory limit; процесс завершается.");
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            TryKill(process);
            CleanupProcess();
        }
        finally { _lifecycleGate.Release(); }
        throw new MicroModelResourceException("MicroModel превысила hard memory limit.");
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            CleanupProcess();
            SetStatus(MicroModelState.Stopped, null, "MicroModel остановлена.");
            return;
        }

        SetStatus(MicroModelState.Stopping, process.Id, "Остановка MicroModel…");
        try
        {
            var request = new MicroModelPipeRequest(Guid.NewGuid().ToString("N"), "shutdown");
            if (_writer is not null)
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken);
                if (_reader is not null)
                    await _reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            }
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            TryKill(process);
        }
        CleanupProcess();
        SetStatus(MicroModelState.Stopped, null, "MicroModel остановлена.");
    }

    private async Task MonitorExitAsync(Process process)
    {
        try { await process.WaitForExitAsync(); }
        catch (InvalidOperationException) { }
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(_process, process)) return;
            CleanupProcess();
            var state = _status.State;
            if (state is not (MicroModelState.Faulted or MicroModelState.MemoryLimitExceeded or MicroModelState.NotInstalled))
                SetStatus(MicroModelState.Stopped, null, "MicroModelHost завершён после idle TTL.");
        }
        finally { _lifecycleGate.Release(); }
    }

    private static ProcessStartInfo CreateStartInfo(string hostPath, string pipeName, TimeSpan idleTtl)
    {
        var isDll = string.Equals(Path.GetExtension(hostPath), ".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : hostPath,
            WorkingDirectory = Path.GetDirectoryName(hostPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (isDll) info.ArgumentList.Add(hostPath);
        info.ArgumentList.Add("--pipe");
        info.ArgumentList.Add(pipeName);
        info.ArgumentList.Add("--idle-ttl-ms");
        info.ArgumentList.Add(Math.Clamp((int)idleTtl.TotalMilliseconds, 100, 300_000).ToString(CultureInfo.InvariantCulture));
        return info;
    }

    private static void ValidateRequest(MicroModelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId)) throw new ArgumentException("MicroModel request ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Question)) throw new ArgumentException("MicroModel question is required.", nameof(request));
        if (request.Transcript.Count > 6) throw new ArgumentException("MicroModel accepts at most 6 transcript entries.", nameof(request));
        if (request.VerifiedFacts.Count > 8) throw new ArgumentException("MicroModel accepts at most 8 verified facts.", nameof(request));
    }

    private static string SafePackagePath(string packageDirectory, string relativePath)
    {
        var root = Path.GetFullPath(packageDirectory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("MicroModel manifest содержит небезопасный путь.");
        return path;
    }

    private void SetStatus(MicroModelState state, int? processId, string message)
    {
        MicroModelStatus status;
        lock (_stateGate)
        {
            status = new(state, processId, message, DateTimeOffset.UtcNow);
            _status = status;
        }
        StateChanged?.Invoke(this, new(status));
    }

    private void CleanupProcess()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _process?.Dispose();
        _writer = null;
        _reader = null;
        _pipe = null;
        _process = null;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var monitor = _exitMonitor;
        try
        {
            await StopAsync(CancellationToken.None);
            if (monitor is not null)
            {
                try { await monitor; }
                catch (ObjectDisposedException) { }
            }
        }
        finally
        {
            _disposed = true;
            _lifecycleGate.Dispose();
            _requestGate.Dispose();
        }
    }

    private sealed class MicroModelResourceException(string message) : InvalidOperationException(message);
}
