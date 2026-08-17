using System.Runtime.InteropServices;
using System.Windows;

namespace MonkeyTV;

internal static class WindowPlacementService
{
    private const uint MonitorDefaultToNull = 0;

    internal static void Restore(Window window, MonkeySettings settings)
    {
        if (settings.WindowWidth is not { } width || settings.WindowHeight is not { } height ||
            settings.WindowLeft is not { } left || settings.WindowTop is not { } top) return;

        width = Math.Max(window.MinWidth, width);
        height = Math.Max(window.MinHeight, height);
        var rect = new NativeRect
        {
            Left = (int)Math.Round(left), Top = (int)Math.Round(top),
            Right = (int)Math.Round(left + width), Bottom = (int)Math.Round(top + height)
        };
        if (MonitorFromRect(ref rect, MonitorDefaultToNull) == IntPtr.Zero)
        {
            left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - width) / 2);
            top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - height) / 2);
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left; window.Top = top; window.Width = width; window.Height = height;
        if (settings.WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    internal static void Capture(Window window, MonkeySettings settings, bool appFullscreen)
    {
        if (appFullscreen) return;
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        if (bounds.Width < window.MinWidth || bounds.Height < window.MinHeight) return;
        settings.WindowLeft = bounds.Left; settings.WindowTop = bounds.Top;
        settings.WindowWidth = bounds.Width; settings.WindowHeight = bounds.Height;
        settings.WindowMaximized = window.WindowState == WindowState.Maximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
}
