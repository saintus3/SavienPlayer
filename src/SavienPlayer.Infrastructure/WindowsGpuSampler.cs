using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SavienPlayer.Infrastructure;

internal sealed class WindowsGpuSampler : IDisposable
{
    private const uint MoreData = 0x800007D2;
    private const uint DoubleFormat = 0x00000200;
    private readonly QueryHandle? _query;
    private readonly IntPtr _counter;
    private readonly string _processPrefix = $"pid_{Environment.ProcessId}_";
    private bool _hasBaseline;

    public WindowsGpuSampler()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != 0)
        {
            query.Dispose();
            return;
        }

        _query = query;
        if (PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
        {
            _query.Dispose();
            _query = null;
        }
    }

    public double? Sample()
    {
        if (_query is null) return null;
        if (PdhCollectQueryData(_query) != 0)
        {
            _hasBaseline = false;
            return null;
        }
        if (!_hasBaseline)
        {
            _hasBaseline = true;
            return null;
        }

        uint bytes = 0;
        uint count = 0;
        if (PdhGetFormattedCounterArray(_counter, DoubleFormat, ref bytes, ref count, IntPtr.Zero) != MoreData ||
            bytes == 0 || bytes > 16 * 1024 * 1024) return null;

        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(_counter, DoubleFormat, ref bytes, ref count, buffer) != 0) return null;
            var size = Marshal.SizeOf<CounterItem>();
            if ((long)count * size > bytes) return null;
            double? busiest = null;
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, index * size));
                var name = Marshal.PtrToStringUni(item.Name);
                if (name?.StartsWith(_processPrefix, StringComparison.OrdinalIgnoreCase) != true ||
                    item.Value.Status > 1 || !double.IsFinite(item.Value.Value)) continue;
                busiest = Math.Max(busiest ?? 0, Math.Clamp(item.Value.Value, 0, 100));
            }
            return busiest;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => _query?.Dispose();

    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    private sealed class QueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public QueryHandle() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr userData, out QueryHandle query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(QueryHandle query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(QueryHandle query);

    [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format,
        ref uint bytes, ref uint count, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
