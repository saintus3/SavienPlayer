using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SavienPlayer.App;

internal sealed class PlaybackKeyboardRouter : IDisposable
{
    private readonly Window _owner;
    private readonly FrameworkElement _overlay;
    private readonly Func<Key, ModifierKeys, bool, bool> _handleKey;
    private readonly HashSet<int> _consumedKeys = new();

    public PlaybackKeyboardRouter(Window owner, FrameworkElement overlay, Func<Key, ModifierKeys, bool, bool> handleKey)
    {
        _owner = owner;
        _overlay = overlay;
        _handleKey = handleKey;
        // Filter before WPF/HwndHost preprocessing so native video and overlay focus use one route.
        ComponentDispatcher.ThreadFilterMessage += FilterMessage;
    }

    private void FilterMessage(ref MSG message, ref bool handled)
    {
        const int keyDown = 0x0100;
        const int keyUp = 0x0101;
        if (message.message is not (keyDown or keyUp)) return;
        var virtualKey = message.wParam.ToInt32();
        if (message.message == keyUp)
        {
            if (_consumedKeys.Remove(virtualKey) && IsPlayerMessage(message.hwnd)) handled = true;
            return;
        }
        if (handled || !IsPlayerMessage(message.hwnd) || IsEditingInput(message.hwnd)) return;
        var repeat = (message.lParam.ToInt64() & (1L << 30)) != 0;
        if (_handleKey(KeyInterop.KeyFromVirtualKey(virtualKey), Keyboard.Modifiers, repeat))
        {
            _consumedKeys.Add(virtualKey);
            handled = true;
        }
    }

    private bool IsPlayerMessage(IntPtr hwnd)
    {
        if (!_owner.IsVisible || !_owner.IsEnabled) return false;
        var ownerHandle = new WindowInteropHelper(_owner).Handle;
        if (ownerHandle == IntPtr.Zero || !IsWindowEnabled(ownerHandle)) return false;
        var root = GetAncestor(hwnd, 2); // GA_ROOT includes native child video HWNDs, but not dialogs/popups.
        var overlayWindow = Window.GetWindow(_overlay);
        var overlayHandle = overlayWindow == null ? IntPtr.Zero : new WindowInteropHelper(overlayWindow).Handle;
        var foreground = GetForegroundWindow();
        return (root == ownerHandle || (overlayHandle != IntPtr.Zero && root == overlayHandle)) &&
               (foreground == ownerHandle || (overlayHandle != IntPtr.Zero && foreground == overlayHandle));
    }

    private static bool IsEditingInput(IntPtr hwnd)
    {
        // Native video focus can leave a stale WPF focused element; only inspect it for WPF HWNDs.
        if (HwndSource.FromHwnd(hwnd) == null) return false;
        for (var element = Keyboard.FocusedElement as DependencyObject; element != null;)
        {
            if (element is TextBoxBase or PasswordBox or ComboBox or Selector) return true;
            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    public void Dispose()
    {
        ComponentDispatcher.ThreadFilterMessage -= FilterMessage;
        _consumedKeys.Clear();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hwnd);
}
