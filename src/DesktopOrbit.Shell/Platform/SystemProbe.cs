using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Platform;

public sealed record MonitorBounds(int Left, int Top, int Right, int Bottom, bool IsPrimary)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

public static class SystemProbe
{
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;
    private const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    private const int QUNS_QUIET_TIME = 6;
    private const int QUNS_APP = 7;

    public static bool IsCursorDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static (int X, int Y) GetCursorPosition()
    {
        return NativeMethods.GetCursorPos(out POINT point) ? (point.X, point.Y) : (0, 0);
    }

    public static bool IsFullscreenOrPresenting()
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out int state) != 0) return false;
            return state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE or QUNS_BUSY;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    public static MonitorBounds GetMonitorWorkArea(int screenX, int screenY)
    {
        var point = new POINT(screenX, screenY);
        IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            return new MonitorBounds(
                info.Work.Left,
                info.Work.Top,
                info.Work.Right,
                info.Work.Bottom,
                (info.Flags & MONITORINFOF_PRIMARY) != 0);
        }

        return new MonitorBounds(0, 0, 1920, 1080, true);
    }

    public static double GetMonitorDpiScale(int screenX, int screenY)
    {
        try
        {
            return GetDpiForPoint(screenX, screenY) / 96.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private static double GetDpiForPoint(int screenX, int screenY)
    {
        var point = new POINT(screenX, screenY);
        IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return 96.0;
        return GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX : 96.0;
    }
}
