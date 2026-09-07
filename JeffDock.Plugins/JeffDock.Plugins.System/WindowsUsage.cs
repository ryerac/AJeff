using System.ComponentModel;
using System.Runtime.InteropServices;

namespace JeffDock.Plugins.System;

internal sealed class WindowsCpuUsageReader
{
    private CpuTimes? _previous;

    public double? Read()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _previous = null;
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        var current = new CpuTimes(idle, kernel, user);
        var percentage = _previous is { } previous ? Calculate(previous, current) : null;
        _previous = current;
        return percentage;
    }

    internal static double? Calculate(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
            return null;
        // GetSystemTimes includes idle time in the kernel counter.
        var total = (double)(current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total <= 0) return null;
        return Math.Clamp(100 * (1 - (current.Idle - previous.Idle) / total), 0, 100);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}

internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

internal static class WindowsMemoryUsage
{
    public static double? Read()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Calculate(status.TotalPhysical, status.AvailablePhysical);
    }

    internal static double? Calculate(ulong total, ulong available) => total == 0 || available > total
        ? null : 100.0 * (total - available) / total;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
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
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
