using System.Runtime.InteropServices;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public interface IVideoMemoryTelemetry
{
    bool TryCapture(out long totalBytes, out long availableBytes);
}

public sealed class WindowsHardwareTelemetry(IVideoMemoryTelemetry? videoMemory = null) : IHardwareTelemetry
{
    private static readonly TimeSpan VideoSampleInterval = TimeSpan.FromSeconds(30);
    private readonly object _videoSync = new();
    private DateTimeOffset _lastVideoSample;
    private long? _totalVramBytes;
    private long? _availableVramBytes;

    public ResourceSnapshot Capture(double processCpuPercent, long processWorkingSetBytes, bool gtaRunning)
    {
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var available = GlobalMemoryStatusEx(ref memory);
        RefreshVideoMemory();
        return new(
            available ? checked((long)memory.TotalPhysical) : null,
            available ? checked((long)memory.AvailablePhysical) : null,
            _totalVramBytes,
            _availableVramBytes,
            processCpuPercent,
            processWorkingSetBytes,
            gtaRunning,
            DateTimeOffset.UtcNow);
    }

    private void RefreshVideoMemory()
    {
        if (videoMemory is null || DateTimeOffset.UtcNow - _lastVideoSample < VideoSampleInterval) return;
        lock (_videoSync)
        {
            if (DateTimeOffset.UtcNow - _lastVideoSample < VideoSampleInterval) return;
            _lastVideoSample = DateTimeOffset.UtcNow;
            if (videoMemory.TryCapture(out var total, out var free))
            {
                _totalVramBytes = total;
                _availableVramBytes = free;
            }
            else
            {
                _totalVramBytes = null;
                _availableVramBytes = null;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
