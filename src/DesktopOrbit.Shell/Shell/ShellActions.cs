using System.Runtime.InteropServices;

namespace DesktopOrbit.Shell.Shell;

/// <summary>
/// Opens items through the shell so file associations, UAC verbs and default
/// applications behave exactly like Explorer.
/// </summary>
public static class ShellActions
{
    public static bool Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        return ShellExecute(path, "open");
    }

    public static bool OpenWith(string path) => ShellExecute(path, "openas");

    public static bool RunAs(string path) => ShellExecute(path, "runas");

    public static bool ShowProperties(string path) => ShellExecute(path, "properties");

    public static bool RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            string arguments = File.Exists(path) || Directory.Exists(path)
                ? $"/select,\"{path}\""
                : $"\"{path}\"";

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        return ShellExecute(path, "open");
    }

    private static bool ShellExecute(string path, string verb)
    {
        try
        {
            IntPtr result = Interop.NativeMethods.ShellExecuteW(IntPtr.Zero, verb, path, null, null, Interop.NativeMethods.SW_SHOWNORMAL);
            return result.ToInt64() > 32;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsManagedDragTargetSupported => true;

    public static void ReleaseComObjects()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
