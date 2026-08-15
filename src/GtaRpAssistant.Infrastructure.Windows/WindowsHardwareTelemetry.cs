using System.Runtime.InteropServices;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class WindowsHardwareTelemetry : IHardwareTelemetry
{
    public ResourceSnapshot Capture(double processCpuPercent, long processWorkingSetBytes, bool gtaRunning)
    {
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var available = GlobalMemoryStatusEx(ref memory);
        return new(
            available ? checked((long)memory.TotalPhysical) : null,
            available ? checked((long)memory.AvailablePhysical) : null,
            null,
            null,
            processCpuPercent,
            processWorkingSetBytes,
            gtaRunning,
            DateTimeOffset.UtcNow);
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
