using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SavienPlayer.App;

internal sealed class WindowDisplayController : IDisposable
{
    private readonly Window _window;
    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private WindowPlacement _placement;
    private WindowStyle _windowStyle;
    private ResizeMode _resizeMode;
    private bool _fullscreen;
    private bool _refreshPending;
    private bool _disposed;
    private IntPtr _savedMonitor;
    private MonitorInfo _savedMonitorInfo;
    private DpiScale _savedDpi;

    public WindowDisplayController(Window window)
    {
        _window = window;
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source.AddHook(WindowProc);
        QueueRefresh();
    }

    public void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(_handle, ref _placement)) return;
            _savedMonitor = MonitorFromWindow(_handle, 2);
            _savedMonitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(_savedMonitor, ref _savedMonitorInfo)) return;
            _savedDpi = VisualTreeHelper.GetDpi(_window);
            _windowStyle = _window.WindowStyle;
            _resizeMode = _window.ResizeMode;
            _fullscreen = true;
            // Re-maximize even when entering from an already maximized window.
            _window.WindowState = WindowState.Normal;
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.WindowState = WindowState.Maximized;
            RefreshBounds();
        }
        else
        {
            _fullscreen = false;
            _window.WindowState = WindowState.Normal;
            _window.WindowStyle = _windowStyle;
            _window.ResizeMode = _resizeMode;
            // WINDOWPLACEMENT uses native workspace coordinates; do not mix these with WPF DIPs.
            RemapSavedPlacement();
            SetWindowPlacement(_handle, ref _placement);
            QueueRefresh();
        }
    }

    private void RemapSavedPlacement()
    {
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var targetMonitor = _savedMonitor;
        if (!GetMonitorInfo(targetMonitor, ref monitor))
        {
            targetMonitor = MonitorFromWindow(_handle, 2);
            if (!GetMonitorInfo(targetMonitor, ref monitor)) return;
        }
        var dpiX = _savedDpi.PixelsPerInchX;
        var dpiY = _savedDpi.PixelsPerInchY;
        if (GetDpiForMonitor(targetMonitor, 0, out var currentX, out var currentY) == 0)
        {
            dpiX = currentX;
            dpiY = currentY;
        }
        var normal = _placement.NormalPosition;
        var bounds = WindowPlacementBounds.Remap(normal.Left, normal.Top,
            normal.Right - normal.Left, normal.Bottom - normal.Top,
            _savedMonitorInfo.Monitor.Left, _savedMonitorInfo.Monitor.Top,
            monitor.Monitor.Left, monitor.Monitor.Top,
            monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top,
            dpiX / _savedDpi.PixelsPerInchX, dpiY / _savedDpi.PixelsPerInchY);
        _placement.NormalPosition = new NativeRect
        {
            Left = bounds.Left, Top = bounds.Top,
            Right = bounds.Left + bounds.Width, Bottom = bounds.Top + bounds.Height
        };
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int getMinMaxInfo = 0x0024;
        const int dpiChanged = 0x02E0;
        const int displayChanged = 0x007E;
        const int settingsChanged = 0x001A;
        const int windowPositionChanged = 0x0047;

        if (message == getMinMaxInfo && _fullscreen && TryGetMonitor(out var monitor))
        {
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MaxPosition = new NativePoint();
            limits.MaxSize = new NativePoint
            {
                X = monitor.Monitor.Right - monitor.Monitor.Left,
                Y = monitor.Monitor.Bottom - monitor.Monitor.Top
            };
            limits.MaxTrackSize = limits.MaxSize;
            Marshal.StructureToPtr(limits, lParam, false);
            handled = true;
        }
        else if (message is dpiChanged or displayChanged or settingsChanged ||
                 (message == windowPositionChanged && _fullscreen))
        {
            // Let WPF consume WM_DPICHANGED's suggested rectangle before reading the destination monitor.
            QueueRefresh();
        }
        return IntPtr.Zero;
    }

    private void QueueRefresh()
    {
        if (_refreshPending || _disposed) return;
        _refreshPending = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _refreshPending = false;
            if (!_disposed) RefreshBounds();
        }));
    }

    private void RefreshBounds()
    {
        if (!TryGetMonitor(out var monitor)) return;
        var dpi = VisualTreeHelper.GetDpi(_window);
        _window.MinWidth = Math.Min(640, (monitor.Work.Right - monitor.Work.Left) / dpi.DpiScaleX);
        _window.MinHeight = Math.Min(480, (monitor.Work.Bottom - monitor.Work.Top) / dpi.DpiScaleY);

        if (!GetWindowRect(_handle, out var current)) return;
        NativeRect target;
        if (_fullscreen)
        {
            target = monitor.Monitor;
        }
        else if (_window.WindowState == WindowState.Normal)
        {
            var width = Math.Min(current.Right - current.Left, monitor.Work.Right - monitor.Work.Left);
            var height = Math.Min(current.Bottom - current.Top, monitor.Work.Bottom - monitor.Work.Top);
            var left = Math.Clamp(current.Left, monitor.Work.Left, monitor.Work.Right - width);
            var top = Math.Clamp(current.Top, monitor.Work.Top, monitor.Work.Bottom - height);
            target = new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
        }
        else
        {
            return;
        }

        if (current.Left == target.Left && current.Top == target.Top &&
            current.Right == target.Right && current.Bottom == target.Bottom) return;
        SetWindowPos(_handle, IntPtr.Zero, target.Left, target.Top,
            target.Right - target.Left, target.Bottom - target.Top, 0x0004 | 0x0010);
    }

    private bool TryGetMonitor(out MonitorInfo monitor)
    {
        monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(_handle, 2), ref monitor);
    }

    public void Dispose()
    {
        _disposed = true;
        _source.RemoveHook(WindowProc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
}
