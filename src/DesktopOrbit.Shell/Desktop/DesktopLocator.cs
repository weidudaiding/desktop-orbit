using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Desktop;

public sealed record DesktopHit(IntPtr Window, string ClassName)
{
    public bool IsDesktopSurface => ClassName is "SysListView32" or "SHELLDLL_DefView" or "Progman" or "WorkerW";
}

public sealed class DesktopLocator
{
    private static readonly string[] DesktopClasses = ["SysListView32", "SHELLDLL_DefView", "Progman", "WorkerW"];

    public DesktopHit HitTestScreenPoint(int x, int y)
    {
        var point = new POINT(x, y);
        IntPtr window = NativeMethods.WindowFromPoint(point);
        return new DesktopHit(window, GetClassNameSafe(window));
    }

    public static string GetClassNameSafe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var buffer = new StringBuilder(256);
        int length = NativeMethods.GetClassName(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }

    public bool IsDesktopPoint(int x, int y) => HitTestScreenPoint(x, y).IsDesktopSurface;

    public static bool IsDesktopClass(string className) => Array.IndexOf(DesktopClasses, className) >= 0;

    public IntPtr FindDefView()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        IntPtr defView = FindDefViewUnder(progman);
        if (defView != IntPtr.Zero) return defView;

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            if (GetClassNameSafe(top) != "WorkerW") return true;
            IntPtr candidate = FindDefViewUnder(top);
            if (candidate == IntPtr.Zero) return true;
            found = candidate;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private static IntPtr FindDefViewUnder(IntPtr parent)
    {
        if (parent == IntPtr.Zero) return IntPtr.Zero;
        return NativeMethods.FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
    }

    public IntPtr FindDesktopListView()
    {
        IntPtr defView = FindDefView();
        if (defView == IntPtr.Zero) return IntPtr.Zero;
        return NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    public bool IsExplorerForeground()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        IntPtr shell = NativeMethods.GetShellWindow();
        if (foreground == shell) return true;

        string className = GetClassNameSafe(foreground);
        if (IsDesktopClass(className)) return true;

        NativeMethods.GetWindowThreadProcessId(foreground, out uint pid);
        IntPtr root = GetRootWindow(foreground);
        return root == foreground && pid != 0 && IsExplorerProcess(pid);
    }

    private static IntPtr GetRootWindow(IntPtr hWnd)
    {
        IntPtr current = hWnd;
        while (true)
        {
            IntPtr owner = GetOwner(current);
            if (owner == IntPtr.Zero) return current;
            current = owner;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    private static IntPtr GetOwner(IntPtr hWnd) => GetAncestor(hWnd, 3);

    private static bool IsExplorerProcess(uint pid)
    {
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public int GetDesktopItemCount()
    {
        IntPtr listView = FindDesktopListView();
        if (listView == IntPtr.Zero) return 0;
        IntPtr result = NativeMethods.SendMessageTimeout(listView, NativeMethods.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, 2u, 200u, out IntPtr count);
        return result == IntPtr.Zero ? 0 : (int)count;
    }
}
