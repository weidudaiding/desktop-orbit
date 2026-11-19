using System.Runtime.InteropServices;
using DesktopOrbit.Shell.Interop;

namespace DesktopOrbit.Shell.Shell;

/// <summary>
/// Writes .lnk files through IShellLink so autostart needs no external process
/// and no registry writes outside HKCU.
/// </summary>
public static class ShortcutWriter
{
    public static bool Create(string linkPath, string targetPath, string? workingDirectory = null, string? arguments = null)
    {
        object? instance = null;
        try
        {
            string? directory = Path.GetDirectoryName(linkPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            instance = new ShellLinkCoClass();
            var link = (IShellLinkW)instance;
            link.SetPath(targetPath);
            if (!string.IsNullOrEmpty(workingDirectory)) link.SetWorkingDirectory(workingDirectory);
            if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
            link.SetDescription("Desktop Orbit");

            ((IPersistFile)link).Save(linkPath, true);
            return File.Exists(linkPath);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.ReleaseComObject(instance);
            }
        }
    }
}
