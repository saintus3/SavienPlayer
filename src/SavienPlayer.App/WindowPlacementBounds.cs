namespace SavienPlayer.App;

internal static class WindowPlacementBounds
{
    public static (int Left, int Top, int Width, int Height) Remap(
        int left, int top, int width, int height,
        int oldMonitorLeft, int oldMonitorTop, int monitorLeft, int monitorTop,
        int workWidth, int workHeight, double scaleX, double scaleY)
    {
        var newWidth = Math.Clamp((int)Math.Round(width * scaleX), 1, workWidth);
        var newHeight = Math.Clamp((int)Math.Round(height * scaleY), 1, workHeight);
        // WINDOWPLACEMENT coordinates exclude the monitor's taskbar offset.
        var newLeft = monitorLeft + (int)Math.Round((left - oldMonitorLeft) * scaleX);
        var newTop = monitorTop + (int)Math.Round((top - oldMonitorTop) * scaleY);
        return (Math.Clamp(newLeft, monitorLeft, monitorLeft + workWidth - newWidth),
            Math.Clamp(newTop, monitorTop, monitorTop + workHeight - newHeight), newWidth, newHeight);
    }
}
