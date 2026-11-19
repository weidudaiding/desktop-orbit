using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Platform;

public static class WindowNative
{
    public static void HideFromAltTabAndTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        long updated = style.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(updated));
    }

    public static void SetNoActivate(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        long updated = enabled
            ? style.ToInt64() | NativeMethods.WS_EX_NOACTIVATE
            : style.ToInt64() & ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(updated));
    }

    public static void PlacePhysical(IntPtr hwnd, int x, int y, int width, int height, bool show)
    {
        if (hwnd == IntPtr.Zero) return;
        uint flags = NativeMethods.SWP_NOACTIVATE | (show ? NativeMethods.SWP_SHOWWINDOW : 0u);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height, flags);
    }

    public static void BringToTop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    public static double GetDpiScale(IntPtr hwnd)
    {
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    public static long GetExtendedStyle(IntPtr hwnd)
    {
        return hwnd == IntPtr.Zero ? 0 : NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
    }

    /// <summary>
    /// SetForegroundWindow is refused for processes that do not own the current
    /// foreground window. Attaching to the foreground thread's input queue is the
    /// supported workaround, and the menu needs real keyboard focus for IME input.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();
        bool attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.SetActiveWindow(hwnd);
            NativeMethods.SetFocus(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    /// <summary>
    /// Clips the window to a circle plus an optional rectangle, so clicks in the
    /// transparent corners fall through to whatever is underneath. The window may be
    /// taller than wide when a panel hangs below the ring, so width and height are
    /// separate. The system takes ownership of the region, so never delete it here.
    /// </summary>
    public static void SetRingRegion(IntPtr hwnd, int width, int height, int centerX, int centerY, int radius,
        int panelLeft, int panelTop, int panelWidth, int panelHeight)
    {
        if (hwnd == IntPtr.Zero || radius <= 1 || width <= 1 || height <= 1) return;

        IntPtr region = CreateRoundRectRgn(
            centerX - radius, centerY - radius,
            centerX + radius + 1, centerY + radius + 1,
            radius * 2, radius * 2);

        if (region == IntPtr.Zero) return;

        if (panelWidth > 0 && panelHeight > 0)
        {
            IntPtr panel = CreateRectRgn(panelLeft, panelTop, panelLeft + panelWidth, panelTop + panelHeight);
            if (panel != IntPtr.Zero)
            {
                CombineRgn(region, region, panel, 2);
                DeleteObject(panel);
            }
        }

        SetWindowRgn(hwnd, region, true);
    }

    public static void ClearRegion(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
}
