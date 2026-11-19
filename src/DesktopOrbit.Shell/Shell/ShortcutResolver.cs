using System.Runtime.InteropServices;
using System.Text;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Shell;

public static class ShortcutResolver
{
    public static string? TryResolve(string shortcutPath)
    {
        object? instance = null;
        try
        {
            instance = new ShellLinkCoClass();
            var link = (IShellLinkW)instance;
            ((IPersistFile)link).Load(shortcutPath, 0);

            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            string target = buffer.ToString();
            return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.ReleaseComObject(instance);
            }
        }
    }

    public static string? TryResolveFromLinkFile(string lnkPath)
    {
        string? target = TryResolve(lnkPath);
        if (target is null) return null;
        return File.Exists(target) || Directory.Exists(target) ? target : null;
    }
}
