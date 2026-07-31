using System.Runtime.InteropServices;
using GtaRpAssistant.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class ProcessLoopbackCaptureService : IAudioCaptureService, IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    private const string VirtualProcessLoopbackDevice = "VAD\\Process_Loopback";
    private const ushort VariantBlob = 65;
    private const uint StreamFlagLoopback = 0x00020000;
    private const uint StreamFlagAutoConvertPcm = 0x80000000;
    private const uint BufferFlagSilent = 0x2;
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid AudioCaptureClientId = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    private readonly uint _processId;
    private readonly BufferedWaveProvider _buffer = new(new WaveFormat(44_100, 16, 2)) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = true, ReadFully = false };
    private readonly WdlResamplingSampleProvider _resampler;
    private readonly float[] _floatBuffer = new float[4096];
    private readonly short[] _shortBuffer = new short[4096];
    private byte[] _packetBuffer = new byte[16_384];
    private TaskCompletionSource<IAudioClient> _activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private IActivateAudioInterfaceAsyncOperation? _activationOperation;
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private bool _disposed;

    public ProcessLoopbackCaptureService(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        _processId = checked((uint)processId);
        ISampleProvider samples = new DownmixSampleProvider(_buffer.ToSampleProvider());
        _resampler = new WdlResamplingSampleProvider(samples, 16_000);
    }

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
    public AudioSourceKind SourceKind => AudioSourceKind.GameAudio;
    public event EventHandler<AudioFrameEventArgs>? FrameCaptured;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported) throw new PlatformNotSupportedException("Process loopback требует Windows build 20348 или новее.");
        if (_captureTask is not null) return;
        _activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var activationParameters = new AudioClientActivationParameters
        {
            ActivationType = 1,
            ProcessLoopbackParameters = new AudioClientProcessLoopbackParameters { TargetProcessId = _processId, ProcessLoopbackMode = 0 },
        };
        var parametersPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParameters>());
        try
        {
            Marshal.StructureToPtr(activationParameters, parametersPointer, false);
            var variant = new PropVariant { VariantType = VariantBlob, Blob = new Blob { Size = Marshal.SizeOf<AudioClientActivationParameters>(), Data = parametersPointer } };
            var interfaceId = AudioClientId;
            var result = ActivateAudioInterfaceAsync(VirtualProcessLoopbackDevice, ref interfaceId, ref variant, this, out _activationOperation);
            ThrowIfFailed(result, "ActivateAudioInterfaceAsync");
            _audioClient = await _activation.Task.WaitAsync(cancellationToken);
        }
        finally { Marshal.FreeHGlobal(parametersPointer); }

        var format = ProcessLoopbackWaveFormatEx.CreatePcmStereo44100();
        ThrowIfFailed(_audioClient.Initialize(0, StreamFlagLoopback | StreamFlagAutoConvertPcm, 0, 0, ref format, IntPtr.Zero), "IAudioClient.Initialize");
        var captureClientId = AudioCaptureClientId;
        ThrowIfFailed(_audioClient.GetService(ref captureClientId, out var captureObject), "IAudioClient.GetService");
        _captureClient = (IAudioCaptureClient)captureObject;
        ThrowIfFailed(_audioClient.Start(), "IAudioClient.Start");
        _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = Task.Run(() => CaptureLoopAsync(_captureCancellation.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_captureTask is null) return;
        _captureCancellation?.Cancel();
        try { await _captureTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (_captureCancellation?.IsCancellationRequested == true) { /* Normal capture stop. */ }
        if (_audioClient is not null) ThrowIfFailed(_audioClient.Stop(), "IAudioClient.Stop");
        _captureTask = null;
        _captureCancellation?.Dispose(); _captureCancellation = null;
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (await timer.WaitForNextTickAsync(cancellationToken)) DrainPackets();
    }

    private void DrainPackets()
    {
        var capture = _captureClient;
        if (capture is null) return;
        while (true)
        {
            ThrowIfFailed(capture.GetNextPacketSize(out var framesAvailable), "IAudioCaptureClient.GetNextPacketSize");
            if (framesAvailable == 0) return;
            ThrowIfFailed(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _), "IAudioCaptureClient.GetBuffer");
            try
            {
                var byteCount = checked((int)frames * 4);
                if (_packetBuffer.Length < byteCount) _packetBuffer = new byte[byteCount];
                if ((flags & BufferFlagSilent) != 0) Array.Clear(_packetBuffer, 0, byteCount); else Marshal.Copy(data, _packetBuffer, 0, byteCount);
                _buffer.AddSamples(_packetBuffer, 0, byteCount);
                EmitNormalizedSamples();
            }
            finally { ThrowIfFailed(capture.ReleaseBuffer(frames), "IAudioCaptureClient.ReleaseBuffer"); }
        }
    }

    private void EmitNormalizedSamples()
    {
        while (true)
        {
            var read = _resampler.Read(_floatBuffer, 0, _floatBuffer.Length);
            if (read <= 0) return;
            for (var i = 0; i < read; i++) _shortBuffer[i] = (short)Math.Clamp((int)Math.Round(_floatBuffer[i] * short.MaxValue), short.MinValue, short.MaxValue);
            FrameCaptured?.Invoke(this, new AudioFrameEventArgs(SourceKind, _shortBuffer.AsMemory(0, read), 16_000));
            if (read < _floatBuffer.Length) return;
        }
    }

    public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
    {
        operation.GetActivateResult(out var activationResult, out var activatedInterface);
        if (activationResult < 0) _activation.TrySetException(Marshal.GetExceptionForHR(activationResult) ?? new COMException("Process loopback activation failed", activationResult));
        else if (activatedInterface is IAudioClient client) _activation.TrySetResult(client);
        else _activation.TrySetException(new InvalidCastException("Activated interface is not IAudioClient."));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None);
        ReleaseComObject(_captureClient); ReleaseComObject(_audioClient); ReleaseComObject(_activationOperation);
        _captureClient = null; _audioClient = null; _activationOperation = null; _disposed = true;
    }

    private static void ReleaseComObject(object? value) { if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static void ThrowIfFailed(int result, string operation) { if (result < 0) throw new COMException($"{operation} failed: 0x{result:X8}", result); }

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid interfaceId, ref PropVariant activationParameters, [MarshalAs(UnmanagedType.Interface)] IActivateAudioInterfaceCompletionHandler completionHandler, [MarshalAs(UnmanagedType.Interface)] out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)] private struct AudioClientActivationParameters { public int ActivationType; public AudioClientProcessLoopbackParameters ProcessLoopbackParameters; }
    [StructLayout(LayoutKind.Sequential)] private struct AudioClientProcessLoopbackParameters { public uint TargetProcessId; public int ProcessLoopbackMode; }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Explicit)] private struct PropVariant { [FieldOffset(0)] public ushort VariantType; [FieldOffset(8)] public Blob Blob; }
    private sealed class DownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source; private float[] _sourceBuffer = [];
        public DownmixSampleProvider(ISampleProvider source) { _source = source; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1); }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count) { var channels = _source.WaveFormat.Channels; var required = count * channels; if (_sourceBuffer.Length < required) _sourceBuffer = new float[required]; var sourceRead = _source.Read(_sourceBuffer, 0, required); var frames = sourceRead / channels; for (var frame = 0; frame < frames; frame++) { float sum = 0; for (var channel = 0; channel < channels; channel++) sum += _sourceBuffer[frame * channels + channel]; buffer[offset + frame] = sum / channels; } return frames; }
    }
}

[ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation); }

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface); }

[ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] public interface IAgileObject { }

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioClient
{
    [PreserveSig] int Initialize(uint shareMode, uint streamFlags, long bufferDuration, long periodicity, ref ProcessLoopbackWaveFormatEx format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint bufferFrames); [PreserveSig] int GetStreamLatency(out long latency); [PreserveSig] int GetCurrentPadding(out uint padding); [PreserveSig] int IsFormatSupported(uint shareMode, IntPtr format, out IntPtr closestMatch); [PreserveSig] int GetMixFormat(out IntPtr format); [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod); [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset(); [PreserveSig] int SetEventHandle(IntPtr handle); [PreserveSig] int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[StructLayout(LayoutKind.Sequential)]
public struct ProcessLoopbackWaveFormatEx
{
    public ushort FormatTag; public ushort Channels; public uint SamplesPerSecond; public uint AverageBytesPerSecond; public ushort BlockAlign; public ushort BitsPerSample; public ushort ExtraSize;
    public static ProcessLoopbackWaveFormatEx CreatePcmStereo44100() => new() { FormatTag = 1, Channels = 2, SamplesPerSecond = 44_100, AverageBytesPerSecond = 176_400, BlockAlign = 4, BitsPerSample = 16 };
}

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition); [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
}
