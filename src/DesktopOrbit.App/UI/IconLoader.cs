using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DesktopOrbit.Shell.Shell;

namespace DesktopOrbit.App.UI;

/// <summary>
/// Decodes cached PNG icon bytes into frozen BitmapSource instances. All decode
/// work happens off the UI thread and the result is frozen so it can be handed
/// over without marshal cost.
/// </summary>
public sealed class IconLoader
{
    private readonly ShellIconCache _cache;
    private readonly Dictionary<string, BitmapSource?> _decoded = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public IconLoader(ShellIconCache cache)
    {
        _cache = cache;
    }

    public string CacheDirectory => ShellIconCache.DefaultDirectory;

    public BitmapSource? TryGetCached(string path, bool isFolder, int pixelSize, bool thumbnail)
    {
        string key = Key(path, pixelSize, thumbnail);
        lock (_gate)
        {
            if (_decoded.TryGetValue(key, out BitmapSource? existing)) return existing;
        }

        byte[]? bytes = _cache.Get(path, isFolder, pixelSize, thumbnail);
        return bytes is null ? null : Store(key, bytes);
    }

    /// <summary>
    /// Fetches bytes on a worker thread (shell COM + PNG encoding are the expensive
    /// parts) and decodes the small PNG there too, then freezes it. Do not set
    /// BitmapCreateOptions.IgnoreImageCache with OnLoad: that combination defers
    /// reads past EndInit and the frozen image ends up empty.
    /// </summary>
    public async Task<BitmapSource?> LoadAsync(string path, bool isFolder, int pixelSize, bool thumbnail, CancellationToken cancellationToken)
    {
        string key = Key(path, pixelSize, thumbnail);
        lock (_gate)
        {
            if (_decoded.TryGetValue(key, out BitmapSource? existing)) return existing;
        }

        byte[]? bytes = await _cache.GetOrLoadAsync(path, isFolder, pixelSize, thumbnail, cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null) return null;

        BitmapSource? image = Decode(bytes);
        if (image is null) return null;

        lock (_gate)
        {
            _decoded[key] = image;
        }
        return image;
    }

    private BitmapSource? Store(string key, byte[] bytes)
    {
        BitmapSource? image = Decode(bytes);
        lock (_gate)
        {
            _decoded[key] = image;
        }
        return image;
    }

    private static string Key(string path, int pixelSize, bool thumbnail) => $"{path}|{pixelSize}|{thumbnail}";

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
