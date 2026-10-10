using System.Runtime.InteropServices;

namespace SystemUsagePlugin;

/// <summary>
/// 跨平台系统资源采集（CPU 使用率 + 物理内存占用）。
///
/// 平台实现：
///   - Windows：GetSystemTimes（CPU）+ GlobalMemoryStatusEx（内存），均为 kernel32 P/Invoke；
///   - Linux：/proc/stat（CPU）+ /proc/meminfo（内存），标准伪文件系统；
///   - macOS：host_statistics64（CPU/内存）+ sysctlbyname("hw.memsize")（总内存），libSystem P/Invoke；
///   - 其他平台：返回 0/空值，保证插件不崩溃。
/// </summary>
internal sealed class SystemUsageService : IDisposable
{
    private readonly CpuUsageReader _cpu;
    private readonly MemoryReader _mem;
    private bool _disposed;

    internal SystemUsageService()
    {
        if (OperatingSystem.IsWindows())
        {
            _cpu = new WindowsCpuReader();
            _mem = new WindowsMemoryReader();
        }
        else if (OperatingSystem.IsLinux())
        {
            _cpu = new LinuxCpuReader();
            _mem = new LinuxMemoryReader();
        }
        else if (OperatingSystem.IsMacOS())
        {
            _cpu = new MacOsCpuReader();
            _mem = new MacOsMemoryReader();
        }
        else
        {
            _cpu = new FallbackCpuReader();
            _mem = new FallbackMemoryReader();
        }
    }

    /// <summary>读取当前 CPU 使用率（0-100%）与内存占用情况</summary>
    internal (double CpuPercent, double MemoryPercent, long MemoryTotalBytes, long MemoryUsedBytes) Read()
    {
        var cpu = _cpu.GetUsagePercent();
        var mem = _mem.Read();
        return (cpu, mem.UsagePercent, mem.TotalBytes, mem.UsedBytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cpu.Dispose();
        _mem.Dispose();
    }
}

// ─────────────────────────── CPU 使用率 ───────────────────────────

/// <summary>CPU 使用率读取器：两次采样差值计算平均占用（跨平台通用逻辑）</summary>
internal abstract class CpuUsageReader : IDisposable
{
    private ulong _lastIdle;
    private ulong _lastTotal;
    private bool _hasBaseline;

    /// <summary>读取 CPU 累计 ticks：(idleTicks, totalTicks)</summary>
    protected abstract (ulong Idle, ulong Total) ReadRaw();

    /// <summary>自上次采样以来的平均 CPU 使用率（0-100%），首次调用返回 0（建立基准）</summary>
    internal double GetUsagePercent()
    {
        try
        {
            var (idle, total) = ReadRaw();
            if (!_hasBaseline)
            {
                _lastIdle = idle;
                _lastTotal = total;
                _hasBaseline = true;
                return 0;
            }

            // 计数器回绕/未变化时仅更新基准，避免出现负值或除零
            if (total <= _lastTotal || idle < _lastIdle)
            {
                _lastIdle = idle;
                _lastTotal = total;
                return 0;
            }

            var dTotal = total - _lastTotal;
            var dIdle = idle - _lastIdle;
            _lastIdle = idle;
            _lastTotal = total;

            return dTotal == 0 ? 0 : Math.Clamp((1 - (double)dIdle / dTotal) * 100.0, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    public virtual void Dispose() { }
}

/// <summary>Windows：GetSystemTimes（kernel32），idle 与 kernel+user 差值</summary>
internal sealed class WindowsCpuReader : CpuUsageReader
{
    protected override (ulong Idle, ulong Total) ReadRaw()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return (0, 0);

        // kernel 时间已包含 idle 时间
        return (ToUInt64(idle), ToUInt64(kernel) + ToUInt64(user));
    }

    private static ulong ToUInt64(FILETIME ft) =>
        ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);
}

/// <summary>Linux：/proc/stat 第一行 "cpu ..."（user nice system idle iowait ...）</summary>
internal sealed class LinuxCpuReader : CpuUsageReader
{
    protected override (ulong Idle, ulong Total) ReadRaw()
    {
        var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
        if (line == null) return (0, 0);

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return (0, 0);

        ulong Parse(int i) => i < parts.Length && ulong.TryParse(parts[i], out var v) ? v : 0;

        var user = Parse(1);
        var nice = Parse(2);
        var system = Parse(3);
        var idle = Parse(4);
        var iowait = Parse(5); // iowait 也属于空闲等待，计入 idle

        return (idle + iowait, user + nice + system + idle + iowait);
    }
}

/// <summary>macOS：host_statistics64(HOST_CPU_LOAD_INFO)，返回 [user, system, idle, nice] ticks</summary>
internal sealed class MacOsCpuReader : CpuUsageReader
{
    private const int HOST_CPU_LOAD_INFO = 2;

    protected override (ulong Idle, ulong Total) ReadRaw()
    {
        var info = new uint[4]; // CPU_STATE_USER / SYSTEM / IDLE / NICE
        var count = info.Length;
        if (host_statistics64(mach_host_self(), HOST_CPU_LOAD_INFO, info, ref count) != 0)
            return (0, 0);

        ulong user = info[0], system = info[1], idle = info[2], nice = info[3];
        return (idle, user + system + idle + nice);
    }

    [DllImport("libSystem.dylib")]
    private static extern int mach_host_self();

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics64(int host_priv, int flavor, [Out] uint[] info, ref int count);
}

/// <summary>不支持平台：返回 0，保证不崩溃</summary>
internal sealed class FallbackCpuReader : CpuUsageReader
{
    protected override (ulong Idle, ulong Total) ReadRaw() => (0, 0);
}

// ─────────────────────────── 内存占用 ───────────────────────────

internal readonly record struct MemoryInfo(double UsagePercent, long TotalBytes, long UsedBytes);

/// <summary>物理内存读取器（跨平台抽象）</summary>
internal abstract class MemoryReader : IDisposable
{
    internal abstract MemoryInfo Read();
    public virtual void Dispose() { }
}

/// <summary>Windows：GlobalMemoryStatusEx（kernel32）</summary>
internal sealed class WindowsMemoryReader : MemoryReader
{
    internal override MemoryInfo Read()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return new MemoryInfo(0, 0, 0);

        var total = (long)status.ullTotalPhys;
        var used = (long)(status.ullTotalPhys - status.ullAvailPhys);
        var percent = total <= 0 ? 0 : used * 100.0 / total;
        return new MemoryInfo(percent, total, used);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}

/// <summary>Linux：/proc/meminfo（MemTotal / MemAvailable，单位 kB）</summary>
internal sealed class LinuxMemoryReader : MemoryReader
{
    internal override MemoryInfo Read()
    {
        long total = 0, available = 0;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:")) total = ParseKb(line);
            else if (line.StartsWith("MemAvailable:"))
            {
                available = ParseKb(line);
                break;
            }
        }

        var used = total - available;
        var percent = total <= 0 ? 0 : used * 100.0 / total;
        return new MemoryInfo(percent, total, used);
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var kb) ? kb * 1024 : 0;
    }
}

/// <summary>macOS：sysctlbyname("hw.memsize") 总内存 + host_statistics64(HOST_VM_INFO64) 可用内存</summary>
internal sealed class MacOsMemoryReader : MemoryReader
{
    private const int HOST_VM_INFO64 = 4;
    private const long PageSize = 4096; // macOS 统一 4K 页

    internal override MemoryInfo Read()
    {
        var total = GetTotalMemory();
        if (total <= 0) return new MemoryInfo(0, 0, 0);

        // vm_statistics64 前三个字段：free_count(0) active_count(4) inactive_count(8)
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            var count = 64; // HOST_VM_INFO64_COUNT
            if (host_statistics64(mach_host_self(), HOST_VM_INFO64, buffer, ref count) != 0)
                return new MemoryInfo(0, total, 0);

            var free = (long)Marshal.ReadInt32(buffer, 0);
            var inactive = (long)Marshal.ReadInt32(buffer, 8);
            var available = (free + inactive) * PageSize;
            var used = total - available;
            var percent = total <= 0 ? 0 : used * 100.0 / total;
            return new MemoryInfo(percent, total, used);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private long GetTotalMemory()
    {
        var buffer = Marshal.AllocHGlobal(sizeof(long));
        try
        {
            nuint len = (nuint)sizeof(long);
            if (sysctlbyname("hw.memsize", buffer, ref len, IntPtr.Zero, 0) != 0) return 0;
            return Marshal.ReadInt64(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("libSystem.dylib")]
    private static extern int mach_host_self();

    [DllImport("libSystem.dylib")]
    private static extern int host_statistics64(int host_priv, int flavor, IntPtr info, ref int count);

    [DllImport("libSystem.dylib", EntryPoint = "sysctlbyname")]
    private static extern int sysctlbyname(string name, IntPtr oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);
}

/// <summary>不支持平台：返回空值，保证不崩溃</summary>
internal sealed class FallbackMemoryReader : MemoryReader
{
    internal override MemoryInfo Read() => new(0, 0, 0);
}
