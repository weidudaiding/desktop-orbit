using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DesktopOrbit.Shell.Shell;

/// <summary>
/// Two-level icon cache: in-memory PNG bytes keyed by path/metadata/size, backed
/// by a disk cache under %LOCALAPPDATA%\DesktopOrbit\cache\icons.
/// </summary>
public sealed class ShellIconCache
{
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, byte[]?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _maxDiskBytes;

    public ShellIconCache(string directory, long maxDiskBytes = 200L * 1024 * 1024)
    {
        _directory = directory;
        _maxDiskBytes = maxDiskBytes;
        Directory.CreateDirectory(_directory);
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopOrbit", "cache", "icons");

    public byte[]? Get(string path, bool isFolder, int pixelSize, bool thumbnail = false)
    {
        string key = BuildKey(path, pixelSize, thumbnail);
        if (_memory.TryGetValue(key, out byte[]? cached) && cached is not null) return cached;

        byte[]? disk = TryReadDisk(key);
        if (disk is not null) _memory[key] = disk;
        return disk;
    }

    public async Task<byte[]?> GetOrLoadAsync(string path, bool isFolder, int pixelSize, bool thumbnail, CancellationToken cancellationToken)
    {
        string key = BuildKey(path, pixelSize, thumbnail);
        if (_memory.TryGetValue(key, out byte[]? cached) && cached is not null) return cached;

        byte[]? disk = TryReadDisk(key);
        if (disk is not null)
        {
            _memory[key] = disk;
            return disk;
        }

        byte[]? png = await Task.Run(() => ShellIcons.TryGetPng(path, pixelSize, thumbnail), cancellationToken).ConfigureAwait(false);
        if (png is not null) _memory[key] = png;
        if (png is not null) TryWriteDisk(key, png);
        return png;
    }

    private static string BuildKey(string path, int pixelSize, bool thumbnail)
    {
        long ticks = 0;
        int size = 0;
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                ticks = info.LastWriteTimeUtc.Ticks;
                size = (int)Math.Min(info.Length, int.MaxValue);
            }
            else if (Directory.Exists(path))
            {
                ticks = Directory.GetLastWriteTimeUtc(path).Ticks;
            }
        }
        catch (Exception)
        {
        }

        string raw = $"{path.ToLowerInvariant()}|{ticks}|{size}|{pixelSize}|{thumbnail}";
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private string PathFor(string key) => Path.Combine(_directory, key + ".png");

    private byte[]? TryReadDisk(string key)
    {
        try
        {
            string file = PathFor(key);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void TryWriteDisk(string key, byte[] png)
    {
        try
        {
            File.WriteAllBytes(PathFor(key), png);
        }
        catch (Exception)
        {
        }
    }

    public void TrimDiskCache()
    {
        try
        {
            var files = new DirectoryInfo(_directory).GetFiles("*.png");
            long total = files.Sum(f => f.Length);
            if (total <= _maxDiskBytes) return;

            foreach (FileInfo file in files.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (total <= _maxDiskBytes) break;
                total -= file.Length;
                file.Delete();
            }
        }
        catch (Exception)
        {
        }
    }
}
