using DesktopOrbit.Core.Config;
using DesktopOrbit.Core.Models;

namespace DesktopOrbit.Shell.Shell;

public sealed class FolderLister
{
    private readonly GeneralOptions _options;

    public FolderLister(GeneralOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<MenuItem> ListDesktop()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<MenuItem>();

        foreach (string directory in DesktopDirectories())
        {
            if (!Directory.Exists(directory)) continue;
            foreach (MenuItem item in Enumerate(directory, 400))
            {
                if (seen.Add(item.Path)) results.Add(item);
            }
        }

        var sorted = results.ToList();
        sorted.Sort(Compare);
        return sorted;
    }

    public static IEnumerable<string> DesktopDirectories()
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(user)) yield return user;

        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrWhiteSpace(common) && !string.Equals(common, user, StringComparison.OrdinalIgnoreCase))
        {
            yield return common;
        }
    }

    public IReadOnlyList<MenuItem> ListFolder(string path, int limit = 200)
    {
        var items = Enumerate(path, limit).ToList();
        items.Sort(Compare);
        return items;
    }

    private IEnumerable<MenuItem> Enumerate(string directory, int limit)
    {
        if (!Directory.Exists(directory)) yield break;

        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = _options.ShowHiddenItems
                ? FileAttributes.None
                : FileAttributes.Hidden | FileAttributes.System
        };

        var directories = new List<string>();
        var files = new List<string>();

        try
        {
            int count = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", enumeration))
            {
                if (count++ >= limit) break;
                if (IsNoise(entry)) continue;

                if (Directory.Exists(entry)) directories.Add(entry);
                else files.Add(entry);
            }
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (string entry in directories)
        {
            yield return new MenuItem
            {
                Path = entry,
                Label = Path.GetFileName(entry),
                Kind = ItemKind.Folder
            };
        }

        foreach (string entry in files)
        {
            yield return new MenuItem
            {
                Path = entry,
                Label = Path.GetFileName(entry),
                Kind = ResolveKind(entry)
            };
        }
    }

    private static bool IsNoise(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    private static ItemKind ResolveKind(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return ItemKind.Shortcut;
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) return ItemKind.Url;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return ItemKind.Application;
        }

        return ItemKind.File;
    }

    private static int Compare(MenuItem left, MenuItem right)
    {
        if (left.IsFolder != right.IsFolder) return left.IsFolder ? -1 : 1;
        return CompareNatural(left.Label, right.Label);
    }

    public static int CompareNatural(string a, string b)
    {
        try
        {
            return Interop.NativeMethods.StrCmpLogicalW(a, b);
        }
        catch (Exception)
        {
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
