using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Speak2Text.Services;

public sealed class SystemResourceMonitor : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;

    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasCpuSample;

    private IntPtr _gpuQuery;
    private IntPtr _gpuCounter;
    private bool _gpuReady;

    public SystemResourceMonitor()
    {
        TryInitializeGpuCounter();
    }

    public ResourceUsageSample Sample()
    {
        var cpu = SampleCpu();
        var gpu = SampleGpu();
        return new ResourceUsageSample(cpu, gpu);
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return 0;

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);

        if (!_hasCpuSample)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasCpuSample = true;
            return 0;
        }

        var idleDelta = idle - _previousIdle;
        var kernelDelta = kernel - _previousKernel;
        var userDelta = user - _previousUser;
        var total = kernelDelta + userDelta;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        if (total == 0)
            return 0;

        var busy = 100d * (1d - idleDelta / (double)total);
        return Math.Clamp(busy, 0, 100);
    }

    private double? SampleGpu()
    {
        if (!_gpuReady)
            return null;

        if (PdhCollectQueryData(_gpuQuery) != 0)
            return null;

        uint bufferSize = 0;
        uint itemCount = 0;
        var status = PdhGetFormattedCounterArray(
            _gpuCounter,
            PdhFmtDouble,
            ref bufferSize,
            ref itemCount,
            IntPtr.Zero);

        if (status != PdhMoreData || bufferSize == 0 || itemCount == 0)
            return 0;

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = PdhGetFormattedCounterArray(
                _gpuCounter,
                PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                buffer);

            if (status != 0)
                return null;

            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItemDouble>();
            var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0u; i < itemCount; i++)
            {
                var ptr = IntPtr.Add(buffer, checked((int)(i * (uint)itemSize)));
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItemDouble>(ptr);

                if (item.Value.CStatus > 1 || double.IsNaN(item.Value.DoubleValue))
                    continue;

                var name = Marshal.PtrToStringUni(item.Name) ?? string.Empty;
                var key = GetPhysicalEngineKey(name);
                var value = Math.Max(0, item.Value.DoubleValue);

                engines.TryGetValue(key, out var current);
                engines[key] = current + value;
            }

            if (engines.Count == 0)
                return 0;

            return Math.Clamp(engines.Values.Max(), 0, 100);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void TryInitializeGpuCounter()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _gpuQuery) != 0)
                return;

            if (PdhAddEnglishCounter(
                    _gpuQuery,
                    @"\GPU Engine(*)\Utilization Percentage",
                    IntPtr.Zero,
                    out _gpuCounter) != 0)
            {
                PdhCloseQuery(_gpuQuery);
                _gpuQuery = IntPtr.Zero;
                return;
            }

            _gpuReady = true;
            PdhCollectQueryData(_gpuQuery);
        }
        catch
        {
            _gpuReady = false;
        }
    }

    private static string GetPhysicalEngineKey(string instanceName)
    {
        var index = instanceName.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? instanceName[index..] : instanceName;
    }

    private static ulong ToUInt64(FILETIME value)
        => ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    public void Dispose()
    {
        if (_gpuQuery != IntPtr.Zero)
        {
            PdhCloseQuery(_gpuQuery);
            _gpuQuery = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueDouble
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItemDouble
    {
        public IntPtr Name;
        public PdhFmtCounterValueDouble Value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FILETIME idleTime,
        out FILETIME kernelTime,
        out FILETIME userTime);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(
        string? dataSource,
        IntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(
        IntPtr query,
        string fullCounterPath,
        IntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}

public sealed record ResourceUsageSample(double CpuPercent, double? GpuPercent);
